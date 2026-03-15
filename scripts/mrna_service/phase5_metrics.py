"""
Phase 5 mRNA rescoring pipeline metric calculation module.

All functions return float scores in [0, 1] where 1.0 = best.
"""

from collections import defaultdict

from folding_backend import FoldingBackend, get_best_backend
from motif_rules import MotifRuleEngine, compute_aggregate_scores
from phase5_config import Phase5Config, get_default_config


# ---------------------------------------------------------------------------
# A. Structural accessibility (uses folding_backend)
# ---------------------------------------------------------------------------


# Max length for global folding; longer sequences use sliding-window sampling.
# ViennaRNA is O(n^3) so folding full 4k+ nt can take minutes or hang.
MAX_GLOBAL_FOLD_LENGTH = 600


def score_global_folding(sequence: str, backend: FoldingBackend) -> float:
    """
    Score global folding stability. Moderate stability is good (MFE around -0.3 to -0.5 kcal/mol per nt).

    Formula: score = 1.0 - abs(mfe_per_nt - ideal) / range, clamped to [0, 1].
    ideal = -0.4, range = 0.4.

    For sequences > 600 nt, folds representative windows to avoid O(n^3) blow-up.
    """
    if not sequence:
        return 0.5
    seq = sequence.upper().replace("T", "U")
    n = len(seq)
    if n <= MAX_GLOBAL_FOLD_LENGTH:
        result = backend.fold(seq)
        mfe_per_nt = result.mfe / n
    else:
        # Sample 3 non-overlapping 400 nt windows (5', middle, 3') for representative MFE
        window = 400
        step = max(1, (n - window) // 2)  # 0, mid, end
        starts = [0, min(step, n - window), max(0, n - window)]
        mfes = []
        for start in starts:
            if start + window <= n:
                subseq = seq[start : start + window]
                result = backend.fold(subseq)
                mfes.append(result.mfe / len(subseq))
        mfe_per_nt = sum(mfes) / len(mfes) if mfes else -0.4
    ideal = -0.4
    range_val = 0.4
    score = 1.0 - abs(mfe_per_nt - ideal) / range_val
    return max(0.0, min(1.0, score))


def score_5prime_accessibility(sequence: str, backend: FoldingBackend) -> float:
    """
    Score 5' end accessibility. Strong negative MFE in first 50nt blocks ribosome loading.

    score_50 = max(0, 1.0 + mfe_50 / 30.0); score_100 = max(0, 1.0 + mfe_100 / 50.0).
    Return 0.6 * score_50 + 0.4 * score_100.
    """
    if not sequence:
        return 0.5
    seq_50 = sequence[:50]
    seq_100 = sequence[:100]
    result_50 = backend.fold(seq_50)
    result_100 = backend.fold(seq_100)
    score_50 = max(0.0, 1.0 + result_50.mfe / 30.0)
    score_100 = max(0.0, 1.0 + result_100.mfe / 50.0)
    return 0.6 * score_50 + 0.4 * score_100


def score_early_cds_penalty(
    sequence: str,
    backend: FoldingBackend,
    window_size: int = 120,
    window_step: int = 60,
) -> float:
    """
    Penalize strongly folded windows in the first 480nt of CDS.

    Per-window score: max(0, 1.0 + mfe / 40.0). Return average of all window scores.
    """
    if not sequence:
        return 0.5
    region = sequence[:480]
    if len(region) < window_size:
        return 0.5
    scores = []
    start = 0
    while start + window_size <= len(region):
        subseq = region[start : start + window_size]
        result = backend.fold(subseq)
        win_score = max(0.0, 1.0 + result.mfe / 40.0)
        scores.append(win_score)
        start += window_step
    return sum(scores) / len(scores) if scores else 0.5


# ---------------------------------------------------------------------------
# B. Sliding-window GC smoothness
# ---------------------------------------------------------------------------


def score_gc_windows(
    sequence: str,
    window_size: int,
    target_low: float,
    target_high: float,
) -> float:
    """
    Score fraction of sliding windows with GC%% within [target_low, target_high].

    Score = count(in-band) / count(total).
    """
    if not sequence or len(sequence) < window_size:
        return 0.5
    seq_upper = sequence.upper().replace("T", "U")
    in_band = 0
    total = 0
    for i in range(len(seq_upper) - window_size + 1):
        window = seq_upper[i : i + window_size]
        gc_count = window.count("G") + window.count("C")
        gc_frac = gc_count / window_size
        total += 1
        if target_low <= gc_frac <= target_high:
            in_band += 1
    return in_band / total if total > 0 else 0.5


def score_gc_variance(sequence: str) -> float:
    """
    Score GC variance across 50nt windows. Low variance = smooth GC profile.

    Score = max(0, 1.0 - variance / 0.02). Variance >= 0.02 yields 0.
    """
    if not sequence or len(sequence) < 50:
        return 0.5
    seq_upper = sequence.upper().replace("T", "U")
    gc_values = []
    for i in range(len(seq_upper) - 50 + 1):
        window = seq_upper[i : i + 50]
        gc_frac = (window.count("G") + window.count("C")) / 50
        gc_values.append(gc_frac)
    n = len(gc_values)
    mean_gc = sum(gc_values) / n
    variance = sum((x - mean_gc) ** 2 for x in gc_values) / n
    return max(0.0, 1.0 - variance / 0.02)


# ---------------------------------------------------------------------------
# C. Codon diversity / autocorrelation
# ---------------------------------------------------------------------------


def score_codon_diversity(
    codon_indices: list[int],
    syn_lookup: list[list[str]],
) -> float:
    """
    Score synonymous codon diversity. Higher unique/available ratio per AA is better.

    Score = average of (unique_used / available) across AAs with >1 option. Met/Trp skipped.
    """
    if not codon_indices or not syn_lookup or len(codon_indices) != len(syn_lookup):
        return 0.5
    groups: dict[tuple[str, ...], list[int]] = defaultdict(list)
    for i in range(len(codon_indices)):
        opts = syn_lookup[i]
        if len(opts) > 1:
            key = tuple(opts)
            groups[key].append(codon_indices[i])
    if not groups:
        return 0.5
    ratios = []
    for key, indices in groups.items():
        available = len(key)
        unique_used = len(set(indices))
        ratios.append(unique_used / available)
    return sum(ratios) / len(ratios) if ratios else 0.5


def score_codon_runs(codon_indices: list[int]) -> float:
    """
    Penalize runs of the same codon index repeated 3+ times consecutively.

    Score = max(0, 1.0 - 0.1 * run_count).
    """
    if not codon_indices:
        return 0.5
    run_count = 0
    i = 0
    while i < len(codon_indices):
        j = i + 1
        while j < len(codon_indices) and codon_indices[j] == codon_indices[i]:
            j += 1
        if j - i >= 3:
            run_count += 1
        i = j
    return max(0.0, 1.0 - 0.1 * run_count)


def score_codon_autocorrelation(codon_indices: list[int]) -> float:
    """
    Penalize high lag-1 autocorrelation (repetitive codon patterns).

    Score = max(0, 1.0 - abs(autocorrelation)).
    """
    if not codon_indices or len(codon_indices) < 2:
        return 0.5
    n = len(codon_indices)
    mean_x = sum(codon_indices) / n
    var_x = sum((x - mean_x) ** 2 for x in codon_indices) / n
    if var_x == 0:
        return 0.5
    cov = sum(
        (codon_indices[i] - mean_x) * (codon_indices[i + 1] - mean_x)
        for i in range(n - 1)
    ) / (n - 1)
    autocorr = cov / var_x
    return max(0.0, 1.0 - abs(autocorr))


# ---------------------------------------------------------------------------
# D. Motif / manufacturability (delegates to MotifRuleEngine)
# ---------------------------------------------------------------------------


def score_manufacturability(sequence: str, config: Phase5Config) -> dict[str, float]:
    """
    Evaluate sequence against motif rules and forbidden motifs.

    Returns dict: homopolymer_score, motif_risk_score, forbidden_motif_score,
    local_sequence_quality_score.
    """
    engine = MotifRuleEngine(forbidden_motifs=config.forbidden_motifs)
    results = engine.evaluate(sequence)
    return compute_aggregate_scores(results)


# ---------------------------------------------------------------------------
# E. Master function
# ---------------------------------------------------------------------------


def compute_all_phase5_metrics(
    sequence: str,
    codon_indices: list[int] | None = None,
    syn_lookup: list[list[str]] | None = None,
    config: Phase5Config | None = None,
    backend: FoldingBackend | None = None,
) -> dict[str, float]:
    """
    Compute all Phase 5 metrics. Returns flat dict of scores in [0, 1].

    If codon_indices/syn_lookup are None, codon metrics default to 0.5.
    If backend raises, structural metrics default to 0.5 and _folding_degraded is set.
    """
    cfg = config or get_default_config()
    be = backend or get_best_backend()

    out: dict[str, float] = {}
    folding_degraded = False

    # Structural
    try:
        out["structure_global"] = score_global_folding(sequence, be)
        out["structure_5prime"] = score_5prime_accessibility(sequence, be)
        out["structure_local_penalty"] = score_early_cds_penalty(
            sequence, be,
            window_size=cfg.folding_window_size,
            window_step=cfg.folding_window_step,
        )
    except Exception:
        out["structure_global"] = 0.5
        out["structure_5prime"] = 0.5
        out["structure_local_penalty"] = 0.5
        folding_degraded = True

    # GC
    out["gc_window_30"] = score_gc_windows(
        sequence, 30, cfg.gc_window_30_low, cfg.gc_window_30_high
    )
    out["gc_window_50"] = score_gc_windows(
        sequence, 50, cfg.gc_window_50_low, cfg.gc_window_50_high
    )
    out["gc_variance"] = score_gc_variance(sequence)

    # Manufacturability
    mfg = score_manufacturability(sequence, cfg)
    out["homopolymer"] = mfg["homopolymer_score"]
    out["motif_risk"] = mfg["motif_risk_score"]
    out["forbidden_motif"] = mfg["forbidden_motif_score"]

    # Codon
    if codon_indices is not None and syn_lookup is not None:
        out["codon_diversity"] = score_codon_diversity(codon_indices, syn_lookup)
        out["codon_run_penalty"] = score_codon_runs(codon_indices)
        out["codon_pattern"] = score_codon_autocorrelation(codon_indices)
    else:
        out["codon_diversity"] = 0.5
        out["codon_run_penalty"] = 0.5
        out["codon_pattern"] = 0.5

    if folding_degraded:
        out["_folding_degraded"] = 1.0  # sentinel; caller can check

    return out
