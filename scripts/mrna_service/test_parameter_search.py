"""
Batched GPU hyperparameter search for NSGA-II mRNA optimizer.

All trials scored in one GPU pass per generation.
Tensor shape: [T, P, L] where T=trials, P=pop_size, L=protein_len.

Key optimizations over sequential version:
  - Single GPU call scores all trials x all individuals (17,500+ seqs)
  - Mutation/crossover fully vectorized via numpy broadcasting
  - Selection uses composite fitness truncation (O(n log n))
    instead of full NSGA-II O(n^2) Pareto sort
  - No Python threads, no per-individual loops

Usage:
    python test_parameter_search.py
"""

import time
import json
import itertools
import numpy as np
import torch
from datetime import datetime
from pathlib import Path

from scoring_gpu import GpuScorer
from codon_data import CFTR_PROTEIN, SYNONYMOUS_CODONS, HUMAN_FREQ


TRIAL_GENERATIONS = 30
SEED = 42


# --- Precomputed lookup tables ---

def build_synonymous_lookup(protein):
    return [SYNONYMOUS_CODONS.get(aa, ["AUG"]) for aa in protein]


def build_decode_tables(syn_lookup):
    """
    Build numpy lookup: nuc_table[position, codon_choice, 0..2] = nucleotide id.
    """
    nuc_map = {"A": 0, "U": 1, "G": 2, "C": 3}
    L = len(syn_lookup)
    max_syn = max(len(s) for s in syn_lookup)

    nuc_table = np.zeros((L, max_syn, 3), dtype=np.int8)
    choice_counts = np.zeros(L, dtype=np.int8)

    for j, codons in enumerate(syn_lookup):
        choice_counts[j] = len(codons)
        for k, codon in enumerate(codons):
            nuc_table[j, k] = [nuc_map[codon[0]], nuc_map[codon[1]], nuc_map[codon[2]]]

    return nuc_table, choice_counts


def vectorized_decode(population, nuc_table, choice_counts, device):
    """
    Decode codon indices -> nucleotide sequences. Fully vectorized.
    population: [N, L] int8  ->  returns: torch.Tensor [N, L*3] int8 on device
    """
    N, L = population.shape
    choices = population % choice_counts[np.newaxis, :]
    pos_idx = np.arange(L)[np.newaxis, :]
    triplets = nuc_table[pos_idx, choices]  # [N, L, 3]
    nuc_array = triplets.reshape(N, L * 3)
    return torch.from_numpy(np.ascontiguousarray(nuc_array)).to(device)


# --- Population init ---

def init_population(pop_size, prot_len, syn_lookup, choice_counts, rng):
    pop = np.zeros((pop_size, prot_len), dtype=np.int8)
    for j in range(prot_len):
        n = int(choice_counts[j])
        if n <= 1:
            continue
        codons = syn_lookup[j]
        freqs = np.array([HUMAN_FREQ.get(c, 1.0) for c in codons])
        freqs /= freqs.sum()
        pop[2:, j] = rng.choice(n, size=pop_size - 2, p=freqs).astype(np.int8)
        if rng.random() < 0.1:
            pop[1, j] = rng.integers(n)
    return pop


# --- Vectorized genetic operators ---

def vectorized_mutation(offspring, mutation_rates, choice_counts, rng):
    """Mutate [T, P, L] in-place. All trials at once."""
    T, P, L = offspring.shape
    mask = rng.random((T, P, L), dtype=np.float32) < mutation_rates[:, None, None]
    mutable = choice_counts[None, None, :] > 1
    apply_mask = mask & mutable
    max_c = int(choice_counts.max())
    new_vals = (rng.integers(max_c, size=(T, P, L), dtype=np.int8)
                % choice_counts[None, None, :])
    offspring[apply_mask] = new_vals[apply_mask]
    return offspring


def vectorized_crossover_and_select(population, composite, crossover_rates,
                                    tournament_size_arr, rng):
    """
    Tournament selection + uniform crossover for all trials. Fully vectorized.
    population: [T, P, L], composite: [T, P]
    Returns: offspring [T, P, L]
    """
    T, P, L = population.shape

    # Vectorized tournament selection: pick parents for all trials
    def select_parents(ts_max):
        cands = rng.integers(P, size=(T, P, ts_max))  # [T, P, ts_max]
        # Gather fitness for candidates
        t_idx = np.arange(T)[:, None, None]  # [T, 1, 1]
        cand_fitness = composite[t_idx, cands]  # [T, P, ts_max]
        # Best candidate per tournament
        best_local = cand_fitness.argmax(axis=2)  # [T, P]
        # Gather actual population index of winner
        winners = np.take_along_axis(cands, best_local[:, :, None], axis=2).squeeze(2)
        return winners  # [T, P]

    ts_max = int(tournament_size_arr.max())
    p1_idx = select_parents(ts_max)  # [T, P]
    p2_idx = select_parents(ts_max)  # [T, P]

    # Gather parent genomes
    t_idx = np.arange(T)[:, None, None]
    parents1 = population[t_idx, p1_idx[:, :, None], np.arange(L)[None, None, :]]
    parents2 = population[t_idx, p2_idx[:, :, None], np.arange(L)[None, None, :]]

    # Uniform crossover
    cross_mask = rng.random((T, P, L), dtype=np.float32) < 0.5
    do_cross = rng.random((T, P, 1), dtype=np.float32) < crossover_rates[:, None, None]
    effective = cross_mask & do_cross
    offspring = np.where(effective, parents1, parents2)

    return offspring


# --- Batched phase runner (composite truncation selection) ---

def run_batched_bucket(configs, scorer, syn_lookup, prot_len, shared_pop,
                       nuc_table, choice_counts):
    """
    Run all configs (same pop size) as one batched tensor workload.
    Uses composite fitness truncation instead of NSGA-II Pareto sort.
    """
    T = len(configs)
    P = configs[0]["population_size"]
    L = prot_len
    G = TRIAL_GENERATIONS

    rng = np.random.default_rng(SEED)

    mr = np.array([c["mutation_rate"] for c in configs], dtype=np.float32)
    cr = np.array([c["crossover_rate"] for c in configs], dtype=np.float32)
    ts = np.array([c["tournament_size"] for c in configs], dtype=np.int32)
    weights = np.array([c["weights"] for c in configs], dtype=np.float64)  # [T, 7]
    w_sums = weights.sum(axis=1, keepdims=True)  # [T, 1]

    base = shared_pop[:P].copy() if P <= len(shared_pop) else init_population(
        P, L, syn_lookup, choice_counts, rng)
    population = np.stack([base.copy() for _ in range(T)], axis=0)  # [T, P, L]

    history_best = np.zeros((T, G), dtype=np.float64)
    history_avg = np.zeros((T, G), dtype=np.float64)

    metric_keys = ["cai", "gc_score", "cpg_score", "uridine_score",
                   "rare_codon_score", "repeat_score", "codon_pair_score"]
    device = scorer.device
    t_start = time.time()

    for gen in range(G):
        # --- Score all trials in ONE GPU call ---
        flat_pop = population.reshape(T * P, L)
        flat_seqs = vectorized_decode(flat_pop, nuc_table, choice_counts, device)
        flat_scores = scorer.score_batch(flat_seqs)

        metric_stack = np.stack(
            [flat_scores[k].reshape(T, P) for k in metric_keys], axis=2
        )  # [T, P, 7]

        # Composite fitness: [T, P]
        composite = (metric_stack * weights[:, np.newaxis, :]).sum(axis=2) / (w_sums + 1e-8)

        history_best[:, gen] = composite.max(axis=1)
        history_avg[:, gen] = composite.mean(axis=1)

        # --- Select + crossover (vectorized) ---
        offspring = vectorized_crossover_and_select(population, composite, cr, ts, rng)

        # --- Mutate (vectorized) ---
        offspring = vectorized_mutation(offspring, mr, choice_counts, rng)

        # --- Score offspring in ONE GPU call ---
        flat_off = offspring.reshape(T * P, L)
        flat_off_seqs = vectorized_decode(flat_off, nuc_table, choice_counts, device)
        flat_off_scores = scorer.score_batch(flat_off_seqs)

        off_metric_stack = np.stack(
            [flat_off_scores[k].reshape(T, P) for k in metric_keys], axis=2
        )
        off_composite = (off_metric_stack * weights[:, np.newaxis, :]).sum(axis=2) / (w_sums + 1e-8)

        # --- Elitist truncation: combine + keep top P per trial ---
        combined_pop = np.concatenate([population, offspring], axis=1)      # [T, 2P, L]
        combined_comp = np.concatenate([composite, off_composite], axis=1)  # [T, 2P]

        # argsort descending per trial, take top P
        top_indices = np.argsort(-combined_comp, axis=1)[:, :P]  # [T, P]

        # Gather top individuals
        t_idx = np.arange(T)[:, None, None]
        l_idx = np.arange(L)[None, None, :]
        population = combined_pop[t_idx, top_indices[:, :, None], l_idx]  # [T, P, L]

        elapsed = time.time() - t_start
        total_seqs = (gen + 1) * T * P * 2
        print(f"    Gen {gen+1:>3}/{G} | "
              f"trial-best: {history_best[:, gen].max():.4f} | "
              f"avg-best: {history_best[:, gen].mean():.4f} | "
              f"{int(total_seqs / elapsed):,} seq/s | "
              f"{elapsed:.1f}s", flush=True)

    elapsed_total = time.time() - t_start

    results = []
    for t in range(T):
        results.append({
            "config": configs[t],
            "best_fitness": float(history_best[t].max()),
            "final_avg": float(history_avg[t, -1]),
            "auc": float(history_best[t].mean()),
            "elapsed_sec": elapsed_total,
        })

    return sorted(results, key=lambda r: r["best_fitness"], reverse=True)


# --- Main ---

def main():
    print("=" * 80)
    print("  BATCHED GPU HYPERPARAMETER SEARCH -- CFTR mRNA OPTIMIZATION")
    print(f"  {TRIAL_GENERATIONS} gens per config | one GPU pass per generation")
    print("=" * 80)

    protein = CFTR_PROTEIN
    prot_len = len(protein)
    syn_lookup = build_synonymous_lookup(protein)
    nuc_table, choice_counts = build_decode_tables(syn_lookup)

    print(f"\n  Protein: {prot_len} aa | CDS: {prot_len*3} nt")
    print(f"  Decode table: {nuc_table.shape} | max synonymous: {int(choice_counts.max())}")
    print("  Initializing GPU scorer...")
    scorer = GpuScorer()
    print(f"  GPU scorer ready on: {scorer.device}\n")

    rng_init = np.random.default_rng(SEED)
    print("  Generating shared initial population (1000 individuals)...")
    shared_pop = init_population(1000, prot_len, syn_lookup, choice_counts, rng_init)
    print("  Done.\n")

    # --- Parameter grid ---
    mutation_rates   = [0.05, 0.10, 0.15, 0.20, 0.25, 0.30, 0.40]
    crossover_rates  = [0.70, 0.80, 0.85, 0.90, 0.95]
    population_sizes = [250, 500, 750, 1000]
    tournament_sizes = [2, 3, 5, 7]

    weight_presets = {
        "balanced":        [1.0, 0.8, 0.9, 0.7, 0.6, 0.5, 0.3],
        "cai_heavy":       [2.0, 0.5, 0.5, 0.5, 0.3, 0.3, 0.2],
        "immune_focus":    [0.8, 0.7, 1.5, 1.2, 0.5, 0.4, 0.3],
        "stability_focus": [0.8, 1.5, 0.7, 1.0, 0.8, 0.8, 0.5],
        "equal":           [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0],
        "top3_only":       [1.0, 1.0, 1.0, 0.0, 0.0, 0.0, 0.0],
        "aggressive":      [1.5, 1.0, 1.2, 0.9, 0.7, 0.6, 0.4],
    }

    # ===== PHASE 1: Mutation x Crossover (pop=500) =====
    p1_configs = [
        {"population_size": 500, "mutation_rate": m, "crossover_rate": c,
         "weights": weight_presets["balanced"], "tournament_size": 3,
         "label": f"mr={m:.2f} cr={c:.2f}"}
        for m, c in itertools.product(mutation_rates, crossover_rates)
    ]

    print(f"\n{'-'*80}")
    print(f"  PHASE 1: Mutation x Crossover  ({len(p1_configs)} configs, pop=500)")
    print(f"  Batch: [{len(p1_configs)}, 500, {prot_len}] = "
          f"{len(p1_configs)*500:,} seqs/GPU-call")
    print(f"{'-'*80}")

    p1 = run_batched_bucket(p1_configs, scorer, syn_lookup, prot_len,
                            shared_pop, nuc_table, choice_counts)
    best_mr = p1[0]["config"]["mutation_rate"]
    best_cr = p1[0]["config"]["crossover_rate"]
    print(f"\n  >> Phase 1 winner: mr={best_mr}, cr={best_cr} "
          f"(best={p1[0]['best_fitness']:.4f})")

    # ===== PHASE 2: Population x Tournament (bucketed) =====
    print(f"\n{'-'*80}")
    print(f"  PHASE 2: Population x Tournament")
    print(f"{'-'*80}")

    p2_all = []
    for ps in population_sizes:
        bucket = [
            {"population_size": ps, "mutation_rate": best_mr, "crossover_rate": best_cr,
             "weights": weight_presets["balanced"], "tournament_size": t,
             "label": f"pop={ps} tourn={t}"}
            for t in tournament_sizes
        ]
        print(f"\n  Bucket pop={ps}: [{len(bucket)}, {ps}, {prot_len}] = "
              f"{len(bucket)*ps:,} seqs/call")
        ranked = run_batched_bucket(bucket, scorer, syn_lookup, prot_len,
                                    shared_pop, nuc_table, choice_counts)
        p2_all.extend(ranked)

    p2_ranked = sorted(p2_all, key=lambda r: r["best_fitness"], reverse=True)
    best_ps = p2_ranked[0]["config"]["population_size"]
    best_ts = p2_ranked[0]["config"]["tournament_size"]
    print(f"\n  >> Phase 2 winner: pop={best_ps}, tourn={best_ts} "
          f"(best={p2_ranked[0]['best_fitness']:.4f})")

    # ===== PHASE 3: Weight presets =====
    p3_configs = [
        {"population_size": best_ps, "mutation_rate": best_mr, "crossover_rate": best_cr,
         "weights": w, "tournament_size": best_ts, "label": f"weights={name}"}
        for name, w in weight_presets.items()
    ]

    print(f"\n{'-'*80}")
    print(f"  PHASE 3: Weight presets  ({len(p3_configs)} configs, pop={best_ps})")
    print(f"{'-'*80}")

    p3 = run_batched_bucket(p3_configs, scorer, syn_lookup, prot_len,
                            shared_pop, nuc_table, choice_counts)
    best_wname = p3[0]["config"]["label"].split("=", 1)[1]
    best_weights = p3[0]["config"]["weights"]
    print(f"\n  >> Phase 3 winner: {best_wname} (best={p3[0]['best_fitness']:.4f})")

    # ===== PHASE 4: Fine-tune around best =====
    fine_mrs = sorted(set(round(x, 3) for x in
        [best_mr-0.05, best_mr-0.02, best_mr, best_mr+0.02, best_mr+0.05] if x > 0))
    fine_crs = sorted(set(round(x, 3) for x in
        [best_cr-0.05, best_cr-0.02, best_cr, best_cr+0.02, min(0.99, best_cr+0.05)] if x > 0))

    p4_configs = [
        {"population_size": best_ps, "mutation_rate": m, "crossover_rate": c,
         "weights": best_weights, "tournament_size": best_ts,
         "label": f"fine mr={m:.3f} cr={c:.3f}"}
        for m, c in itertools.product(fine_mrs, fine_crs)
    ]

    print(f"\n{'-'*80}")
    print(f"  PHASE 4: Fine-tune  ({len(p4_configs)} configs)")
    print(f"{'-'*80}")

    p4 = run_batched_bucket(p4_configs, scorer, syn_lookup, prot_len,
                            shared_pop, nuc_table, choice_counts)
    final_mr = p4[0]["config"]["mutation_rate"]
    final_cr = p4[0]["config"]["crossover_rate"]
    print(f"\n  >> Phase 4 winner: mr={final_mr}, cr={final_cr} "
          f"(best={p4[0]['best_fitness']:.4f})")

    # ===== RESULTS =====
    champion = {
        "population_size": best_ps,
        "mutation_rate": final_mr,
        "crossover_rate": final_cr,
        "weights": best_weights,
        "tournament_size": best_ts,
    }

    all_results = p1 + p2_all + p3 + p4
    all_ranked = sorted(all_results, key=lambda r: r["best_fitness"], reverse=True)

    print(f"\n{'='*80}")
    print(f"  CHAMPION CONFIGURATION")
    print(f"{'='*80}")
    print(f"  Population size:   {best_ps}")
    print(f"  Mutation rate:     {final_mr}")
    print(f"  Crossover rate:    {final_cr}")
    print(f"  Tournament size:   {best_ts}")
    print(f"  Weights ({best_wname}): {best_weights}")
    print(f"  Best fitness in {TRIAL_GENERATIONS} gens: {p4[0]['best_fitness']:.4f}")

    default = next((r for r in all_results if r["config"].get("label") == "mr=0.15 cr=0.85"), None)
    if default:
        delta = p4[0]["best_fitness"] - default["best_fitness"]
        print(f"\n  vs defaults (mr=0.15 cr=0.85):")
        print(f"    Default:  {default['best_fitness']:.4f}")
        print(f"    Champion: {p4[0]['best_fitness']:.4f}")
        print(f"    Delta:    {delta:+.4f} ({delta/max(default['best_fitness'],0.001)*100:+.1f}%)")

    print(f"\n{'-'*80}")
    print(f"  TOP 15 ACROSS ALL {len(all_results)} TRIALS")
    print(f"{'-'*80}")
    print(f"  {'Rank':<5} {'Config':<42} {'Best':>8} {'Avg':>8} {'AUC':>8}")
    print(f"  {'----':<5} {'----':<42} {'----':>8} {'----':>8} {'----':>8}")
    for i, r in enumerate(all_ranked[:15]):
        print(f"  {i+1:<5} {r['config']['label']:<42} {r['best_fitness']:>8.4f} "
              f"{r['final_avg']:>8.4f} {r['auc']:>8.4f}")
    print(f"{'='*80}")

    output = {
        "timestamp": datetime.now().isoformat(),
        "trial_generations": TRIAL_GENERATIONS,
        "seed": SEED,
        "total_trials": len(all_results),
        "champion": champion,
        "champion_fitness": p4[0]["best_fitness"],
        "phase1_top5": [{"label": r["config"]["label"], "best": r["best_fitness"],
                         "avg": r["final_avg"], "auc": r["auc"]} for r in p1[:5]],
        "phase2_top5": [{"label": r["config"]["label"], "best": r["best_fitness"],
                         "avg": r["final_avg"], "auc": r["auc"]} for r in p2_ranked[:5]],
        "phase3_all":  [{"label": r["config"]["label"], "best": r["best_fitness"],
                         "avg": r["final_avg"], "auc": r["auc"]} for r in p3],
        "phase4_top5": [{"label": r["config"]["label"], "best": r["best_fitness"],
                         "avg": r["final_avg"], "auc": r["auc"]} for r in p4[:5]],
        "top15": [{"label": r["config"]["label"], "best": r["best_fitness"],
                   "avg": r["final_avg"], "auc": r["auc"],
                   "config": r["config"]} for r in all_ranked[:15]],
    }

    out_path = Path(__file__).parent / "parameter_search_results.json"
    with open(out_path, "w") as f:
        json.dump(output, f, indent=2)
    print(f"\n  Results saved to {out_path}")


if __name__ == "__main__":
    main()
