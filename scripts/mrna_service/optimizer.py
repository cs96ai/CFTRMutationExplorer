"""
NSGA-II optimizer with GPU-accelerated scoring and vectorized genetic operators.
Runs until a fitness threshold is reached or manually stopped.
Checkpoints to disk every N generations for crash recovery.

Key optimizations over the original:
  - Vectorized decode (numpy advanced indexing, no Python loops)
  - Vectorized mutation via broadcasting masks
  - Vectorized crossover via broadcasting masks
  - Vectorized tournament selection via batched argmax
  - Composite-fitness elitist truncation (O(n log n) vs O(n^2) Pareto sort)
  - Full NSGA-II Pareto sort only every N generations for front reporting
"""

import json
import time
import traceback
import numpy as np
import threading
from datetime import datetime
from pathlib import Path
from scoring_gpu import GpuScorer
from codon_data import CFTR_PROTEIN, SYNONYMOUS_CODONS, HUMAN_FREQ
from persistence import save_checkpoint, load_checkpoint

# Log file for top 10 Stage 1 candidates (for manual Phase 5 testing)
STAGE1_TOP10_LOG = "stage1_top10_candidates.json"

# Lazy import to avoid circular -- main.py sets these
_log_fn = None
_status_fn = None

def set_log_fn(fn):
    global _log_fn
    _log_fn = fn

def set_status_fn(fn):
    global _status_fn
    _status_fn = fn

def _log(level, msg):
    if _log_fn:
        _log_fn(level, msg)
    else:
        print(f"[{level}] {msg}", flush=True)

def _notify_status():
    if _status_fn:
        _status_fn()


class OptimizerState:
    """Thread-safe state shared between optimizer and API."""

    def __init__(self):
        self.lock = threading.Lock()
        self.running = False
        self.generation = 0
        self.best_fitness = 0.0
        self.avg_fitness = 0.0
        self.pareto_front_size = 0
        self.seqs_per_sec = 0
        self.status = "idle"
        self.run_id = ""
        self.history = []
        self.pareto_front = []
        self.best_candidate = None
        self.config = {}
        self.started_at = None
        self.threshold_reached = False


state = OptimizerState()

METRIC_KEYS = ["cai", "gc_score", "cpg_score", "uridine_score",
               "rare_codon_score", "repeat_score", "codon_pair_score"]

PARETO_SORT_INTERVAL = 25


# --- Lookup tables (built once per run) ---

def _build_synonymous_lookup(protein):
    return [SYNONYMOUS_CODONS.get(aa, ["AUG"]) for aa in protein]


def _build_choice_counts(syn_lookup):
    return np.array([len(s) for s in syn_lookup], dtype=np.int8)


# --- Vectorized population init ---

def _init_population(pop_size, prot_len, syn_lookup, choice_counts):
    rng = np.random.default_rng()
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

def _vectorized_tournament_select(composite, pop_size, tournament_size, rng):
    """
    Vectorized tournament selection using composite fitness.
    Returns parent indices [pop_size].
    """
    cands = rng.integers(pop_size, size=(pop_size, tournament_size))
    cand_fitness = composite[cands]
    best_local = cand_fitness.argmax(axis=1)
    winners = np.take_along_axis(cands, best_local[:, None], axis=1).squeeze(1)
    return winners


def _vectorized_crossover(parents1, parents2, crossover_rate, rng):
    """Uniform crossover via broadcasting. Returns offspring [P, L]."""
    P, L = parents1.shape
    cross_mask = rng.random((P, L), dtype=np.float32) < 0.5
    do_cross = rng.random((P, 1), dtype=np.float32) < crossover_rate
    effective = cross_mask & do_cross
    return np.where(effective, parents1, parents2)


def _vectorized_mutate(offspring, mutation_rate, choice_counts, rng):
    """Mutate population [P, L] in-place via broadcasting mask."""
    P, L = offspring.shape
    mask = rng.random((P, L), dtype=np.float32) < mutation_rate
    mutable = choice_counts[np.newaxis, :] > 1
    apply_mask = mask & mutable
    max_c = int(choice_counts.max())
    new_vals = (rng.integers(max_c, size=(P, L), dtype=np.int8)
                % choice_counts[np.newaxis, :])
    offspring[apply_mask] = new_vals[apply_mask]
    return offspring


# --- Composite fitness ---

def _compute_composite(scores, weights):
    w = np.array(weights, dtype=np.float64)
    w_sum = w.sum()
    if w_sum < 1e-10:
        return np.zeros(len(scores["cai"]))
    total = np.zeros(len(scores["cai"]), dtype=np.float64)
    for i, k in enumerate(METRIC_KEYS):
        if i < len(w):
            total += scores[k] * w[i]
    return total / w_sum


# --- NSGA-II Pareto sort (used periodically for front reporting) ---

def _dominates(a, b):
    return np.all(a >= b) and np.any(a > b)


def _non_dominated_sort(objectives):
    n = len(objectives)
    domination_count = np.zeros(n, dtype=np.int32)
    dominated_sets = [[] for _ in range(n)]
    ranks = np.zeros(n, dtype=np.int32)

    for i in range(n):
        for j in range(i + 1, n):
            if _dominates(objectives[i], objectives[j]):
                dominated_sets[i].append(j)
                domination_count[j] += 1
            elif _dominates(objectives[j], objectives[i]):
                dominated_sets[j].append(i)
                domination_count[i] += 1

    fronts = []
    current = [i for i in range(n) if domination_count[i] == 0]
    rank = 0
    while current:
        fronts.append(current)
        for i in current:
            ranks[i] = rank
        nxt = []
        for i in current:
            for j in dominated_sets[i]:
                domination_count[j] -= 1
                if domination_count[j] == 0:
                    nxt.append(j)
        current = nxt
        rank += 1
    return fronts, ranks


def _crowding_distance(objectives, front):
    n = len(front)
    if n <= 2:
        return np.full(n, 1e12)
    distances = np.zeros(n)
    for m in range(objectives.shape[1]):
        vals = objectives[front, m]
        si = np.argsort(vals)
        distances[si[0]] = 1e12
        distances[si[-1]] = 1e12
        rng_val = vals[si[-1]] - vals[si[0]]
        if rng_val < 1e-10:
            continue
        for i in range(1, n - 1):
            distances[si[i]] += (vals[si[i + 1]] - vals[si[i - 1]]) / rng_val
    return distances


# --- Main optimization loop ---

def run_optimization(
    population_size=750,
    crossover_rate=0.75,
    mutation_rate=0.03,
    fitness_threshold=0.90,
    checkpoint_interval=100,
    tournament_size=2,
    weights=None,
    results_dir="results",
    resume_from=None,
):
    if weights is None:
        weights = [1.0, 0.9, 0.9, 0.7, 0.6, 0.5, 0.4]

    protein = CFTR_PROTEIN
    prot_len = len(protein)
    syn_lookup = _build_synonymous_lookup(protein)
    choice_counts = _build_choice_counts(syn_lookup)

    _log("INFO", f"Initializing GpuScorer (protein length={prot_len}, pop_size={population_size})...")
    scorer = GpuScorer()
    _log("INFO", f"GpuScorer ready on device: {scorer.device}")

    run_id = datetime.now().strftime("%Y%m%d_%H%M%S")
    results_path = Path(results_dir)
    results_path.mkdir(parents=True, exist_ok=True)

    rng = np.random.default_rng()

    if resume_from and Path(resume_from).exists():
        _log("INFO", f"Loading checkpoint from {resume_from}...")
        checkpoint = load_checkpoint(resume_from)
        population = np.array(checkpoint["population"], dtype=np.int8)
        start_gen = checkpoint.get("generation", 0)
        history = checkpoint.get("history", [])
        run_id = checkpoint.get("run_id", run_id)
        _log("INFO", f"Resumed from generation {start_gen}, run {run_id}, pop shape={population.shape}")
    else:
        _log("INFO", f"Initializing fresh population of {population_size}...")
        population = _init_population(population_size, prot_len, syn_lookup, choice_counts)
        start_gen = 0
        history = []
        _log("INFO", f"Population initialized: shape={population.shape}")

    with state.lock:
        state.running = True
        state.generation = start_gen
        state.run_id = run_id
        state.status = "running"
        state.config = {
            "population_size": population_size,
            "crossover_rate": crossover_rate,
            "mutation_rate": mutation_rate,
            "fitness_threshold": fitness_threshold,
            "tournament_size": tournament_size,
            "weights": weights,
        }
        state.started_at = datetime.now().isoformat()
        state.history = history

    gen = start_gen
    t_start = time.time()
    stagnation = 0
    prev_best = 0.0
    pareto_size = 0
    pareto_candidates = []

    try:
        while True:
            with state.lock:
                if not state.running:
                    break

            gen += 1

            # --- Score population (vectorized decode + GPU scoring) ---
            seqs = scorer.decode_population_fast(population)
            scores = scorer.score_batch(seqs)
            composite = _compute_composite(scores, weights)

            # --- Vectorized selection ---
            p1_idx = _vectorized_tournament_select(composite, population_size, tournament_size, rng)
            p2_idx = _vectorized_tournament_select(composite, population_size, tournament_size, rng)

            parents1 = population[p1_idx]
            parents2 = population[p2_idx]

            # --- Vectorized crossover + mutation ---
            offspring = _vectorized_crossover(parents1, parents2, crossover_rate, rng)
            offspring = _vectorized_mutate(offspring, mutation_rate, choice_counts, rng)

            # --- Score offspring (vectorized decode + GPU scoring) ---
            off_seqs = scorer.decode_population_fast(offspring)
            off_scores = scorer.score_batch(off_seqs)
            off_composite = _compute_composite(off_scores, weights)

            # --- Elitist truncation: combine + keep top P by composite ---
            combined_pop = np.vstack([population, offspring])
            combined_composite = np.concatenate([composite, off_composite])

            top_indices = np.argsort(-combined_composite)[:population_size]
            population = combined_pop[top_indices]

            # --- Stats ---
            best_idx = top_indices[0]
            best_composite = float(combined_composite[best_idx])
            selected_composite = combined_composite[top_indices]
            avg_composite = float(selected_composite.mean())

            elapsed = time.time() - t_start
            seqs_per_sec = int(gen * population_size * 2 / max(elapsed, 0.01))

            # --- Periodic full Pareto sort for front reporting ---
            combined_objectives = None
            if gen % PARETO_SORT_INTERVAL == 0 or best_composite >= fitness_threshold:
                combined_obj_all = np.column_stack(
                    [np.concatenate([scores[k], off_scores[k]]) for k in METRIC_KEYS]
                )
                try:
                    combined_fronts, _ = _non_dominated_sort(combined_obj_all[top_indices[:100]])
                    pareto_size = len(combined_fronts[0]) if combined_fronts else 0
                    pareto_candidates = []
                    selected_obj = combined_obj_all[top_indices]
                    for idx in range(min(50, len(top_indices))):
                        pareto_candidates.append({
                            "index": int(idx),
                            "cai": float(selected_obj[idx, 0]),
                            "gc_score": float(selected_obj[idx, 1]),
                            "cpg_score": float(selected_obj[idx, 2]),
                            "uridine_score": float(selected_obj[idx, 3]),
                            "rare_codon_score": float(selected_obj[idx, 4]),
                            "repeat_score": float(selected_obj[idx, 5]),
                            "codon_pair_score": float(selected_obj[idx, 6]),
                            "composite": float(combined_composite[top_indices[idx]]),
                            "coding_sequence": _codons_to_rna(combined_pop[top_indices[idx]], syn_lookup),
                        })
                    pareto_candidates.sort(key=lambda x: x["composite"], reverse=True)
                except Exception:
                    pass

            gen_stats = {
                "generation": gen,
                "best_fitness": best_composite,
                "avg_fitness": avg_composite,
                "pareto_front_size": pareto_size,
                "best_cai": float(scores["cai"].max()),
                "best_gc": float(scores["gc_score"].max()),
                "best_cpg": float(scores["cpg_score"].max()),
                "best_uridine": float(scores["uridine_score"].max()),
                "elapsed_seconds": elapsed,
                "seqs_per_sec": seqs_per_sec,
            }

            # Build best candidate RNA
            best_pop_idx = 0  # after sort, index 0 is best
            best_codons = population[best_pop_idx]
            best_rna = _codons_to_rna(best_codons, syn_lookup)

            best_obj_row = np.column_stack(
                [np.concatenate([scores[k], off_scores[k]]) for k in METRIC_KEYS]
            )[best_idx]

            with state.lock:
                state.generation = gen
                state.best_fitness = best_composite
                state.avg_fitness = avg_composite
                state.pareto_front_size = pareto_size
                state.seqs_per_sec = seqs_per_sec
                state.history.append(gen_stats)
                state.pareto_front = pareto_candidates
                state.best_candidate = {
                    "composite": best_composite,
                    "cai": float(best_obj_row[0]),
                    "gc_score": float(best_obj_row[1]),
                    "cpg_score": float(best_obj_row[2]),
                    "uridine_score": float(best_obj_row[3]),
                    "rare_codon_score": float(best_obj_row[4]),
                    "repeat_score": float(best_obj_row[5]),
                    "codon_pair_score": float(best_obj_row[6]),
                    "rna_sequence_first_120": best_rna[:120],
                    "rna_length": len(best_rna),
                }
                state.status = f"Gen {gen} | Best: {best_composite:.4f} | Avg: {avg_composite:.4f} | {seqs_per_sec} seq/s"

            _notify_status()

            if gen % checkpoint_interval == 0:
                _save_checkpoint(results_path, run_id, gen, population, syn_lookup)
                _log("INFO", f"Gen {gen}: Best={best_composite:.4f} Avg={avg_composite:.4f} Pareto={pareto_size} ({seqs_per_sec} seq/s) -- checkpoint saved")
            elif gen % 10 == 0:
                _log("DEBUG", f"Gen {gen}: Best={best_composite:.4f} Avg={avg_composite:.4f} ({seqs_per_sec} seq/s)")

            if abs(best_composite - prev_best) < 1e-6:
                stagnation += 1
                if stagnation % 50 == 0:
                    _log("WARN", f"Gen {gen}: Stagnation detected ({stagnation} gens without improvement)")
            else:
                stagnation = 0
            prev_best = best_composite

            if best_composite >= fitness_threshold:
                with state.lock:
                    state.threshold_reached = True
                    state.status = f"THRESHOLD REACHED at gen {gen}! Best={best_composite:.4f}"
                _save_checkpoint(results_path, run_id, gen, population, syn_lookup, final=True)
                _log("INFO", f"Gen {gen}: THRESHOLD {fitness_threshold} REACHED! Best={best_composite:.4f}")
                break

    except Exception as e:
        _log("ERROR", f"Optimizer exception at gen {gen}: {type(e).__name__}: {e}")
        _log("ERROR", traceback.format_exc())
        with state.lock:
            state.status = f"Error: {str(e)}"
            state.running = False
        try:
            _save_checkpoint(results_path, run_id, gen, population, syn_lookup, final=True)
        except:
            pass
        raise
    finally:
        with state.lock:
            state.running = False
        _notify_status()
        _save_checkpoint(results_path, run_id, gen, population, syn_lookup, final=True)
        _log("INFO", f"Optimizer stopped at generation {gen}. Final best={prev_best:.4f}")


def _codons_to_rna(codon_choices, syn_lookup):
    rna = []
    for j, idx in enumerate(codon_choices):
        codons = syn_lookup[j]
        codon = codons[int(idx) % len(codons)]
        rna.append(codon)
    return "".join(rna)


def _write_stage1_top10_log(results_path: Path, run_id: str, population, syn_lookup):
    """Write top 10 Pareto candidates to a JSON log for manual Phase 5 testing."""
    with state.lock:
        pareto = state.pareto_front or []
    top10 = pareto[:10]
    # Fallback: if no Pareto front yet, build from top of population
    if not top10 and population is not None and syn_lookup is not None:
        for i in range(min(10, len(population))):
            rna = _codons_to_rna(population[i], syn_lookup)
            top10.append({"index": i, "coding_sequence": rna})
    if not top10:
        return
    # Add id for Phase 5 compatibility
    candidates = []
    for i, c in enumerate(top10):
        cand = dict(c)
        cand["id"] = cand.get("id", f"candidate_{i}")
        candidates.append(cand)
    log_data = {
        "run_id": run_id,
        "timestamp": datetime.now().isoformat(),
        "count": len(candidates),
        "candidates": candidates,
    }
    log_path = results_path / STAGE1_TOP10_LOG
    with open(log_path, "w") as f:
        json.dump(log_data, f, indent=2)
    _log("INFO", f"Stage 1 top 10 candidates written to {log_path}")


def _save_checkpoint(results_path, run_id, gen, population, syn_lookup, final=False):
    with state.lock:
        data = {
            "run_id": run_id,
            "generation": gen,
            "timestamp": datetime.now().isoformat(),
            "config": state.config,
            "best_fitness": state.best_fitness,
            "avg_fitness": state.avg_fitness,
            "pareto_front_size": state.pareto_front_size,
            "threshold_reached": state.threshold_reached,
            "history": state.history[-500:],
            "pareto_front": state.pareto_front,
            "best_candidate": state.best_candidate,
            "population": population.tolist(),
        }

    if state.best_candidate:
        top_rnas = []
        for choice_row in population[:min(10, len(population))]:
            top_rnas.append(_codons_to_rna(choice_row, syn_lookup))
        data["top_rna_sequences"] = top_rnas

    if final:
        _write_stage1_top10_log(results_path, run_id, population, syn_lookup)

    suffix = "_FINAL" if final else f"_gen{gen}"
    filename = results_path / f"optimization_{run_id}{suffix}.json"
    save_checkpoint(data, str(filename))

    latest = results_path / "latest.json"
    save_checkpoint(data, str(latest))
