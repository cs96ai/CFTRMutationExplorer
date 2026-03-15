"""
Phase 5 mRNA rescoring pipeline report generation.

Generates structured JSON output and human-readable reports for the Phase 5
rescoring pipeline.
"""

from dataclasses import asdict, is_dataclass
from datetime import datetime
from typing import Any

from phase5_metrics import MAX_GLOBAL_FOLD_LENGTH
from phase5_scoring import Phase5Result, ScoredCandidate


def _folding_summary(seq_len: int) -> str:
    """Human-readable description of Vienna folding windows used."""
    if seq_len <= 0:
        return "N/A"
    if seq_len <= MAX_GLOBAL_FOLD_LENGTH:
        global_desc = f"full ({seq_len} nt)"
    else:
        global_desc = "3 × 400 nt (5′, mid, 3′)"
    return f"{global_desc} + 50 nt + 100 nt (5′) + 8 × 120 nt (early CDS)"


def _candidate_to_dict(c: ScoredCandidate) -> dict[str, Any]:
    """
    Convert a ScoredCandidate to a JSON-serializable dict.

    Args:
        c: Scored candidate to convert.

    Returns:
        Dict with id, truncated coding_sequence, metrics, ranks, and metadata.
    """
    coding = c.coding_sequence or ""
    if len(coding) > 120:
        coding = coding[:120] + "..."
    return {
        "id": c.id,
        "coding_sequence": coding,
        "legacy_metrics": c.legacy_metrics,
        "phase5_metrics": c.phase5_metrics,
        "composite_legacy": c.composite_legacy,
        "composite_phase5": c.composite_phase5,
        "rank_legacy": c.rank_legacy,
        "rank_phase5": c.rank_phase5,
        "rank_change": c.rank_change,
        "warnings": c.warnings,
        "explanation": c.explanation,
        "diversity_cluster_id": c.diversity_cluster_id,
    }


def _get_top_metrics(metrics: dict[str, float], n: int = 3, strongest: bool = True) -> list[str]:
    """Return top N metric names by value (strongest = highest, weakest = lowest)."""
    if not metrics:
        return []
    # Exclude sentinel keys like _folding_degraded
    filtered = {k: v for k, v in metrics.items() if not k.startswith("_") and isinstance(v, (int, float))}
    if not filtered:
        return []
    sorted_items = sorted(filtered.items(), key=lambda x: x[1], reverse=strongest)
    return [name for name, _ in sorted_items[:n]]


def generate_report(result: Phase5Result) -> dict[str, Any]:
    """
    Build a JSON-serializable report dict from a Phase5Result.

    Args:
        result: Phase 5 rescoring result.

    Returns:
        Dict with timestamp, config, summary, candidates, and rank movement.
    """
    candidates_all = result.candidates_all
    candidates_diverse = result.candidates_diverse_topk

    # Config: ensure JSON-serializable
    config = result.config_used
    if is_dataclass(config) and not isinstance(config, type):
        config = asdict(config)

    # Summary with computed fields
    summary = dict(result.summary) if result.summary else {}
    summary["total_candidates"] = len(candidates_all)
    summary["diverse_candidates"] = len(candidates_diverse)

    top_id = None
    top_strengths: list[str] = []
    top_weaknesses: list[str] = []

    if candidates_diverse:
        top = candidates_diverse[0]
        top_id = top.id
        all_metrics = {**(top.legacy_metrics or {}), **(top.phase5_metrics or {})}
        top_strengths = _get_top_metrics(all_metrics, n=3, strongest=True)
        top_weaknesses = _get_top_metrics(all_metrics, n=3, strongest=False)
    elif candidates_all:
        top = candidates_all[0]
        top_id = top.id
        all_metrics = {**(top.legacy_metrics or {}), **(top.phase5_metrics or {})}
        top_strengths = _get_top_metrics(all_metrics, n=3, strongest=True)
        top_weaknesses = _get_top_metrics(all_metrics, n=3, strongest=False)

    summary["top_candidate_id"] = top_id
    summary["top_strengths"] = top_strengths
    summary["top_weaknesses"] = top_weaknesses

    # Sequence and folding info for UI
    seq_len = len(candidates_all[0].coding_sequence) if candidates_all else 0
    summary["sequence_length_nt"] = seq_len
    summary["folding_summary"] = _folding_summary(seq_len)

    # Rank movement
    rank_changes = [(c, c.rank_change) for c in candidates_all if c.rank_change is not None]
    moved_up = sum(1 for _, rc in rank_changes if rc < 0)
    moved_down = sum(1 for _, rc in rank_changes if rc > 0)

    biggest_gainer = None
    biggest_loser = None
    if rank_changes:
        gainer = min(rank_changes, key=lambda x: x[1])
        loser = max(rank_changes, key=lambda x: x[1])
        if gainer[1] < 0:
            biggest_gainer = {
                "id": gainer[0].id,
                "old_rank": gainer[0].rank_legacy,
                "new_rank": gainer[0].rank_phase5,
            }
        if loser[1] > 0:
            biggest_loser = {
                "id": loser[0].id,
                "old_rank": loser[0].rank_legacy,
                "new_rank": loser[0].rank_phase5,
            }

    rank_movement = {
        "moved_up": moved_up,
        "moved_down": moved_down,
        "biggest_gainer": biggest_gainer,
        "biggest_loser": biggest_loser,
    }

    return {
        "timestamp": datetime.now().isoformat(),
        "config": config,
        "summary": summary,
        "candidates_all": [_candidate_to_dict(c) for c in candidates_all],
        "candidates_diverse_topk": [_candidate_to_dict(c) for c in candidates_diverse],
        "rank_movement": rank_movement,
    }


def generate_explanation(candidate: ScoredCandidate) -> str:
    """
    Produce a detailed human-readable explanation for a scored candidate.

    Args:
        candidate: Scored candidate to explain.

    Returns:
        Multi-line explanation string.
    """
    parts = []

    # Rank info
    rank_phase5 = candidate.rank_phase5
    rank_legacy = candidate.rank_legacy
    rank_change = candidate.rank_change or 0
    if rank_legacy is not None and rank_phase5 is not None:
        direction = "up" if rank_change < 0 else "down"
        parts.append(f"Rank #{rank_phase5} (was #{rank_legacy}, moved {abs(rank_change)} positions {direction}).")
    elif rank_phase5 is not None:
        parts.append(f"Rank #{rank_phase5}.")

    # Combine metrics
    all_metrics = {**(candidate.legacy_metrics or {}), **(candidate.phase5_metrics or {})}
    filtered = {k: v for k, v in all_metrics.items() if not k.startswith("_") and isinstance(v, (int, float))}

    strong = [(k, v) for k, v in filtered.items() if v > 0.85]
    weak = [(k, v) for k, v in filtered.items() if v < 0.65]

    if strong:
        strong_str = ", ".join(f"{k} ({v:.2f})" for k, v in sorted(strong, key=lambda x: -x[1])[:3])
        parts.append(f"Strengths: {strong_str}.")
    if weak:
        weak_str = ", ".join(f"{k} ({v:.2f})" for k, v in sorted(weak, key=lambda x: x[1])[:3])
        parts.append(f"Weaknesses: {weak_str}.")
    if not strong and not weak:
        parts.append("Well-balanced candidate with no critical weaknesses.")

    if candidate.warnings:
        parts.append("Warnings: " + "; ".join(candidate.warnings))

    return " ".join(parts)


def generate_text_summary(result: Phase5Result) -> str:
    """
    Produce a multi-line human-readable text summary of the Phase 5 result.

    Args:
        result: Phase 5 rescoring result.

    Returns:
        Formatted summary string.
    """
    candidates_all = result.candidates_all
    candidates_diverse = result.candidates_diverse_topk
    n = len(candidates_all)
    m = len(candidates_diverse)

    lines = [
        "=== Phase 5 Rescoring Summary ===",
        f"Candidates rescored: {n}",
        f"Diverse top-K: {m}",
        "",
    ]

    if candidates_diverse:
        top = candidates_diverse[0]
        leg = top.composite_legacy
        ph5 = top.composite_phase5
        leg_str = f"{leg:.4f}" if leg is not None else "N/A"
        ph5_str = f"{ph5:.4f}" if ph5 is not None else "N/A"
        all_metrics = {**(top.legacy_metrics or {}), **(top.phase5_metrics or {})}
        filtered = {k: v for k, v in all_metrics.items() if not k.startswith("_") and isinstance(v, (int, float))}
        strong = [(k, v) for k, v in filtered.items() if v > 0.85]
        weak = [(k, v) for k, v in filtered.items() if v < 0.65]
        strengths_str = ", ".join(f"{k} ({v:.2f})" for k, v in sorted(strong, key=lambda x: -x[1])[:3]) if strong else "none"
        weaknesses_str = ", ".join(f"{k} ({v:.2f})" for k, v in sorted(weak, key=lambda x: x[1])[:3]) if weak else "none"
        lines.extend([
            f"Top candidate: #{top.id}",
            f"  Legacy composite: {leg_str}",
            f"  Phase5 composite: {ph5_str}",
            f"  Strengths: {strengths_str}",
            f"  Weaknesses: {weaknesses_str}",
            "",
        ])
    else:
        lines.append("Top candidate: (none)")
        lines.append("")

    # Rank movement
    rank_changes = [(c, c.rank_change) for c in candidates_all if c.rank_change is not None]
    moved_up = sum(1 for _, rc in rank_changes if rc < 0)
    moved_down = sum(1 for _, rc in rank_changes if rc > 0)

    lines.append("Rank movement:")
    lines.append(f"  Moved up: {moved_up} candidates")
    lines.append(f"  Moved down: {moved_down} candidates")

    if rank_changes:
        gainer = min(rank_changes, key=lambda x: x[1])
        loser = max(rank_changes, key=lambda x: x[1])
        if gainer[1] < 0:
            lines.append(f"  Biggest gain: #{gainer[0].id} (+{abs(gainer[1])} positions)")
        if loser[1] > 0:
            lines.append(f"  Biggest drop: #{loser[0].id} (-{loser[1]} positions)")

    return "\n".join(lines)
