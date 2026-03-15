"""
Phase 5 mRNA rescoring pipeline orchestrator.

Takes top N candidates from an existing optimizer, rescores them with additional
metrics, and produces reranked results with diversity filtering.
"""

from dataclasses import dataclass
import math
from typing import Callable

from codon_data import CODON_TO_AA, SYNONYMOUS_CODONS
from folding_backend import FoldingBackend, get_best_backend
from phase5_config import Phase5Config, get_default_config, get_preset
from phase5_metrics import compute_all_phase5_metrics


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------


@dataclass
class ScoredCandidate:
    """
    A candidate sequence with legacy and Phase 5 metrics, composite scores,
    rankings, and explanatory text.
    """

    id: str
    coding_sequence: str
    legacy_metrics: dict[str, float]
    phase5_metrics: dict[str, float]
    composite_legacy: float
    composite_phase5: float
    rank_legacy: int
    rank_phase5: int
    rank_change: int
    warnings: list[str]
    explanation: str
    diversity_cluster_id: int | None = None


@dataclass
class Phase5Result:
    """
    Result of Phase 5 rescoring: all candidates sorted by Phase 5 composite,
    diversity-filtered top-K, summary statistics, and config used.
    """

    candidates_all: list[ScoredCandidate]
    candidates_diverse_topk: list[ScoredCandidate]
    summary: dict
    config_used: dict


# ---------------------------------------------------------------------------
# Main orchestrator
# ---------------------------------------------------------------------------


class Phase5Scorer:
    """
    Main orchestrator for the Phase 5 mRNA rescoring pipeline.

    Ingests candidates from an optimizer, computes Phase 5 metrics, applies
    saturation dampening, reranks by composite score, and produces
    diversity-filtered results.
    """

    def __init__(
        self,
        config: Phase5Config | None = None,
        weights: dict[str, float] | None = None,
        preset: str = "phase5_balanced",
        backend: FoldingBackend | None = None,
    ) -> None:
        """
        Initialize the Phase 5 scorer.

        Args:
            config: Pipeline configuration. If None, uses get_default_config().
            weights: Metric weights for composite. If None, uses get_preset(preset).
            preset: Preset name when weights is None.
            backend: Folding backend for structure metrics. If None, uses get_best_backend().
        """
        self.config = config or get_default_config()
        self.weights = weights or get_preset(preset)
        self.preset = preset
        self.backend = backend or get_best_backend()

    def rescore_candidates(
        self,
        candidates: list[dict],
        legacy_weights: list[float] | None = None,
        progress_callback: Callable[[int, int, str], None] | None = None,
        log_callback: Callable[[str], None] | None = None,
    ) -> Phase5Result:
        """
        Run the full Phase 5 rescoring pipeline on candidate sequences.

        Args:
            candidates: List of dicts with coding_sequence, cai, gc_score,
                cpg_score, uridine_score, rare_codon_score, repeat_score,
                codon_pair_score, composite.
            legacy_weights: Optional list of 7 weights for recomputing legacy
                composite. If None, uses candidate's composite field.

        Returns:
            Phase5Result with all candidates, diverse top-K, summary, and config.
        """
        # 1. Ingest
        ingested = self._ingest(candidates)
        if log_callback:
            log_callback(f"Ingested {len(ingested)} candidates from optimizer")

        # 2. Validate
        _, validation_warnings = self._validate(ingested)
        for i, w in enumerate(validation_warnings):
            if w and i < len(ingested):
                ingested[i].setdefault("_warnings", []).extend(w)
        valid_count = sum(1 for c in ingested if c.get("_valid", True))
        invalid_count = len(ingested) - valid_count
        if log_callback:
            log_callback(f"Validation: {valid_count} valid, {invalid_count} invalid/skipped")
            if invalid_count > 0:
                invalid_ids = [c.get("id", f"candidate_{i}") for i, c in enumerate(ingested) if not c.get("_valid", True)]
                log_callback(f"  Skipped: {', '.join(invalid_ids[:5])}{'...' if len(invalid_ids) > 5 else ''}")

        # 3. Compute Phase 5 metrics
        scored_list: list[ScoredCandidate] = []
        if log_callback:
            log_callback(f"Computing Phase 5 metrics for {valid_count} candidates (folding + GC windows + motifs)...")

        for i, c in enumerate(ingested):
            if not c.get("_valid", True):
                continue
            codon_indices, syn_lookup = self._reconstruct_codon_indices(
                c["coding_sequence"]
            )
            phase5 = compute_all_phase5_metrics(
                c["coding_sequence"],
                codon_indices=codon_indices if codon_indices else None,
                syn_lookup=syn_lookup if syn_lookup else None,
                config=self.config,
                backend=self.backend,
            )
            # Remove sentinel keys from phase5
            phase5_clean = {k: v for k, v in phase5.items() if not k.startswith("_")}

            legacy = {
                "cai": c.get("cai", 0.5),
                "gc_score": c.get("gc_score", 0.5),
                "cpg_score": c.get("cpg_score", 0.5),
                "uridine_score": c.get("uridine_score", 0.5),
                "rare_codon_score": c.get("rare_codon_score", 0.5),
                "repeat_score": c.get("repeat_score", 0.5),
                "codon_pair_score": c.get("codon_pair_score", 0.5),
            }

            composite_legacy = c.get("composite")
            if composite_legacy is None and legacy_weights is not None:
                composite_legacy = sum(
                    legacy.get(k, 0.5) * w
                    for k, w in zip(
                        [
                            "cai",
                            "gc_score",
                            "cpg_score",
                            "uridine_score",
                            "rare_codon_score",
                            "repeat_score",
                            "codon_pair_score",
                        ],
                        legacy_weights[:7],
                    )
                ) / sum(legacy_weights[:7]) if legacy_weights else 0.5
            if composite_legacy is None:
                composite_legacy = c.get("composite", 0.5)

            # 4. Saturation dampening and 5. Compute Phase 5 composite
            composite_phase5 = self._compute_composite_with_dampening(
                legacy, phase5_clean
            )

            cand_id = c.get("id", f"candidate_{i}")
            warnings = c.get("_warnings", [])

            scored_list.append(
                ScoredCandidate(
                    id=cand_id,
                    coding_sequence=c["coding_sequence"],
                    legacy_metrics=legacy,
                    phase5_metrics=phase5_clean,
                    composite_legacy=float(composite_legacy),
                    composite_phase5=composite_phase5,
                    rank_legacy=0,  # assigned in step 6
                    rank_phase5=0,
                    rank_change=0,
                    warnings=warnings,
                    explanation="",
                )
            )
            if progress_callback:
                progress_callback(len(scored_list), len(ingested), cand_id)
            # Log every 10th candidate or first/last
            if log_callback and (len(scored_list) <= 3 or len(scored_list) % 10 == 0 or len(scored_list) == valid_count):
                log_callback(f"  Processed {len(scored_list)}/{valid_count}: {cand_id} (Phase5 composite: {composite_phase5:.4f})")

        # 6. Rank
        sorted_legacy = sorted(
            scored_list, key=lambda x: x.composite_legacy, reverse=True
        )
        for r, sc in enumerate(sorted_legacy, start=1):
            sc.rank_legacy = r

        sorted_phase5 = sorted(
            scored_list, key=lambda x: x.composite_phase5, reverse=True
        )
        for r, sc in enumerate(sorted_phase5, start=1):
            sc.rank_phase5 = r

        # 7. Compute rank_change
        for sc in scored_list:
            sc.rank_change = sc.rank_legacy - sc.rank_phase5

        moved_up = sum(1 for sc in scored_list if sc.rank_change > 0)
        moved_down = sum(1 for sc in scored_list if sc.rank_change < 0)
        unchanged = sum(1 for sc in scored_list if sc.rank_change == 0)
        if log_callback:
            log_callback(f"Rank changes: {moved_up} moved up, {moved_down} moved down, {unchanged} unchanged")
            top3 = sorted_phase5[:3]
            for i, sc in enumerate(top3, 1):
                log_callback(f"  Top #{i}: {sc.id} (Phase5: {sc.composite_phase5:.4f}, rank change: {sc.rank_change:+d})")

        # 8. Generate explanation
        for sc in scored_list:
            sc.explanation = self._generate_explanation(sc)

        # 9. Diversity filter
        if log_callback:
            log_callback(f"Ranking complete. Applying diversity filter (top_k={self.config.diversity_top_k})...")
        candidates_diverse_topk = self._diversity_filter(
            sorted_phase5,
            top_k=self.config.diversity_top_k,
            threshold=self.config.diversity_threshold,
        )

        # 10. Build summary
        if log_callback:
            log_callback(f"Diversity filter: {len(candidates_diverse_topk)} diverse candidates selected")
        summary = self._build_summary(scored_list)

        config_used = {
            "preset": self.preset,
            "top_n": self.config.top_n,
            "diversity_top_k": self.config.diversity_top_k,
            "diversity_threshold": self.config.diversity_threshold,
            "saturation_threshold": self.config.saturation_threshold,
            "saturation_dampen_factor": self.config.saturation_dampen_factor,
        }

        return Phase5Result(
            candidates_all=sorted_phase5,
            candidates_diverse_topk=candidates_diverse_topk,
            summary=summary,
            config_used=config_used,
        )

    def _ingest(self, candidates: list[dict]) -> list[dict]:
        """Parse and normalize candidate dicts."""
        result = []
        for i, c in enumerate(candidates):
            entry = dict(c)
            entry.setdefault("id", f"candidate_{i}")
            result.append(entry)
        return result

    def _validate(self, ingested: list[dict]) -> tuple[list[dict], list[list[str]]]:
        """Check sequences are valid RNA (AUGC only) and same length."""
        warnings: list[list[str]] = [[] for _ in ingested]
        valid_chars = set("AUGC")
        ref_len = None

        for i, c in enumerate(ingested):
            c["_valid"] = True
            seq = c.get("coding_sequence", "")
            seq_upper = seq.upper().replace("T", "U")
            invalid = [b for b in seq_upper if b not in valid_chars]
            if invalid:
                warnings[i].append(
                    f"Invalid bases: {set(invalid)}. Sequence must be AUGC only."
                )
                c["_valid"] = False
            if ref_len is None:
                ref_len = len(seq)
            elif len(seq) != ref_len:
                warnings[i].append(
                    f"Length {len(seq)} differs from reference {ref_len}."
                )
                c["_valid"] = False
        return ingested, warnings

    def _reconstruct_codon_indices(
        self, sequence: str
    ) -> tuple[list[int], list[list[str]]]:
        """
        Reconstruct codon_indices and syn_lookup from coding sequence.

        Splits into codons, looks up each in CODON_TO_AA, builds syn_lookup
        from SYNONYMOUS_CODONS, and returns (codon_indices, syn_lookup).
        """
        codon_indices: list[int] = []
        syn_lookup: list[list[str]] = []
        seq_upper = sequence.upper().replace("T", "U")

        for i in range(0, len(seq_upper), 3):
            codon = seq_upper[i : i + 3]
            if len(codon) != 3:
                break
            aa = CODON_TO_AA.get(codon, "?")
            if aa == "*":
                syns = [codon]
                idx = 0
            else:
                syns = list(SYNONYMOUS_CODONS.get(aa, [codon]))
                if codon not in syns:
                    syns = [codon]
                idx = syns.index(codon)
            codon_indices.append(idx)
            syn_lookup.append(syns)

        return codon_indices, syn_lookup

    def _dampen_weight(self, metric_value: float, base_weight: float) -> float:
        """
        Apply saturation dampening: reduce effective weight when metric
        exceeds saturation_threshold.
        """
        if metric_value > self.config.saturation_threshold:
            return base_weight * self.config.saturation_dampen_factor
        return base_weight

    def _compute_composite_with_dampening(
        self, legacy: dict[str, float], phase5: dict[str, float]
    ) -> float:
        """
        Compute weighted average of all metrics with saturation dampening.
        """
        legacy_to_weight = {
            "cai": "legacy_cai",
            "gc_score": "legacy_gc",
            "cpg_score": "legacy_cpg",
            "uridine_score": "legacy_uridine",
            "rare_codon_score": "legacy_rare_codon",
            "repeat_score": "legacy_repeat",
            "codon_pair_score": "legacy_codon_pair",
        }
        total_weight = 0.0
        weighted_sum = 0.0

        for leg_key, weight_key in legacy_to_weight.items():
            w = self.weights.get(weight_key, 0.0)
            if w == 0:
                continue
            val = legacy.get(leg_key, 0.5)
            eff_w = self._dampen_weight(val, w)
            total_weight += eff_w
            weighted_sum += val * eff_w

        for metric_key, val in phase5.items():
            w = self.weights.get(metric_key, 0.0)
            if w == 0:
                continue
            eff_w = self._dampen_weight(val, w)
            total_weight += eff_w
            weighted_sum += val * eff_w

        if total_weight == 0:
            return 0.5
        return weighted_sum / total_weight

    def _diversity_filter(
        self,
        candidates: list[ScoredCandidate],
        top_k: int,
        threshold: float,
    ) -> list[ScoredCandidate]:
        """
        Greedy Hamming-distance-based selection: keep #1, then only admit
        candidates with Hamming distance > threshold * seq_length from all
        already-selected.
        """
        if not candidates:
            return []
        selected: list[ScoredCandidate] = []
        seq_len = len(candidates[0].coding_sequence)
        min_hamming = int(threshold * seq_len)

        for c in candidates:
            if len(selected) >= top_k:
                break
            if not selected:
                selected.append(c)
                c.diversity_cluster_id = 0
                continue
            seq = c.coding_sequence.upper().replace("T", "U")
            min_dist = min(
                sum(
                    1
                    for a, b in zip(
                        seq, s.coding_sequence.upper().replace("T", "U")
                    )
                    if a != b
                )
                for s in selected
            )
            if min_dist > min_hamming:
                selected.append(c)
                c.diversity_cluster_id = len(selected) - 1

        return selected

    def _generate_explanation(self, c: ScoredCandidate) -> str:
        """
        Produce a 1-2 sentence human-readable summary of strengths and
        weaknesses based on metric values.
        """
        all_metrics = {**c.legacy_metrics, **c.phase5_metrics}
        # Map to display names
        display_names = {
            "cai": "CAI",
            "gc_score": "GC balance",
            "cpg_score": "CpG",
            "uridine_score": "uridine",
            "rare_codon_score": "rare codon",
            "repeat_score": "repeat",
            "codon_pair_score": "codon pair",
            "structure_global": "global folding",
            "structure_5prime": "5' accessibility",
            "gc_window_30": "30nt GC windows",
            "gc_window_50": "50nt GC windows",
            "gc_variance": "GC variance",
            "homopolymer": "homopolymer",
            "motif_risk": "motif risk",
            "forbidden_motif": "forbidden motif",
            "codon_diversity": "codon diversity",
            "codon_run_penalty": "codon runs",
            "codon_pattern": "codon pattern",
        }

        strengths = [
            (display_names.get(k, k), v)
            for k, v in sorted(all_metrics.items(), key=lambda x: -x[1])
            if v > 0.9
        ][:3]
        weaknesses = [
            (display_names.get(k, k), v)
            for k, v in sorted(all_metrics.items(), key=lambda x: x[1])
            if v < 0.7
        ][:3]

        parts = []
        if strengths:
            strength_str = " and ".join(f"{n} ({v:.2f})" for n, v in strengths)
            parts.append(f"Strong {strength_str}")
        if weaknesses:
            weak_str = " and ".join(f"{n} ({v:.2f})" for n, v in weaknesses)
            parts.append(f"but downgraded due to poor {weak_str}")
        if not parts:
            return f"Balanced profile with composite Phase 5 score {c.composite_phase5:.2f}."
        return ", ".join(parts) + "."

    def _build_summary(self, candidates: list[ScoredCandidate]) -> dict:
        """Build summary with rank change counts and composite statistics."""
        moved_up = sum(1 for c in candidates if c.rank_change > 0)
        moved_down = sum(1 for c in candidates if c.rank_change < 0)
        unchanged = sum(1 for c in candidates if c.rank_change == 0)

        rank_changes = [c.rank_change for c in candidates]
        avg_rank_change = (
            sum(rank_changes) / len(rank_changes) if rank_changes else 0.0
        )

        composites_legacy = [c.composite_legacy for c in candidates]
        composites_phase5 = [c.composite_phase5 for c in candidates]
        avg_legacy = sum(composites_legacy) / len(composites_legacy) if composites_legacy else 0.0
        avg_phase5 = sum(composites_phase5) / len(composites_phase5) if composites_phase5 else 0.0
        n = len(composites_phase5)
        std_phase5 = (
            math.sqrt(
                sum((x - avg_phase5) ** 2 for x in composites_phase5) / n
            )
            if n > 0
            else 0.0
        )

        return {
            "moved_up_count": moved_up,
            "moved_down_count": moved_down,
            "unchanged_count": unchanged,
            "avg_rank_change": avg_rank_change,
            "avg_composite_legacy": avg_legacy,
            "avg_composite_phase5": avg_phase5,
            "std_composite_phase5": std_phase5,
        }
