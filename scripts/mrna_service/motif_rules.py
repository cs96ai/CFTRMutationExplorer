"""
Rule-based motif scoring engine for mRNA sequence quality.
Scores sequences for problematic motifs: homopolymers, dinucleotide repeats,
AU-rich elements, and custom forbidden motifs.
"""

import re
from dataclasses import dataclass


@dataclass
class MotifResult:
    """Result of applying a single motif rule to a sequence."""

    rule_name: str
    score: float  # 0-1, 1.0 = no problems found
    hit_count: int
    positions: list[int]
    explanation: str


@dataclass
class MotifRule:
    """Definition of a motif rule with regex pattern and penalty parameters."""

    name: str
    pattern: str  # regex pattern
    max_allowed: int  # hits above this count incur penalty
    penalty_per_hit: float  # score reduction per hit above max_allowed
    description: str


def get_default_rules() -> list[MotifRule]:
    """
    Return the default set of motif rules for mRNA sequence quality assessment.
    Includes homopolymer runs, dinucleotide repeats, AU-rich elements, and U-rich stretches.
    """
    return [
        # Homopolymer runs (5+ consecutive same nucleotide)
        MotifRule(
            name="Homopolymer A runs",
            pattern=r"A{5,}",
            max_allowed=0,
            penalty_per_hit=0.2,
            description="Runs of 5 or more consecutive adenines",
        ),
        MotifRule(
            name="Homopolymer U runs",
            pattern=r"U{5,}",
            max_allowed=0,
            penalty_per_hit=0.2,
            description="Runs of 5 or more consecutive uracils",
        ),
        MotifRule(
            name="Homopolymer G runs",
            pattern=r"G{5,}",
            max_allowed=0,
            penalty_per_hit=0.2,
            description="Runs of 5 or more consecutive guanines",
        ),
        MotifRule(
            name="Homopolymer C runs",
            pattern=r"C{5,}",
            max_allowed=0,
            penalty_per_hit=0.2,
            description="Runs of 5 or more consecutive cytosines",
        ),
        # Dinucleotide repeats
        MotifRule(
            name="Dinucleotide AU repeat",
            pattern=r"(AU){4,}",
            max_allowed=0,
            penalty_per_hit=0.15,
            description="Repeated AU dinucleotides (e.g. AUAUAUAU)",
        ),
        MotifRule(
            name="Dinucleotide GC repeat",
            pattern=r"(GC){4,}",
            max_allowed=0,
            penalty_per_hit=0.15,
            description="Repeated GC dinucleotides",
        ),
        MotifRule(
            name="Dinucleotide AG repeat",
            pattern=r"(AG){4,}",
            max_allowed=0,
            penalty_per_hit=0.15,
            description="Repeated AG dinucleotides",
        ),
        MotifRule(
            name="Dinucleotide UC repeat",
            pattern=r"(UC){4,}",
            max_allowed=0,
            penalty_per_hit=0.15,
            description="Repeated UC dinucleotides",
        ),
        # AU-rich elements (ARE, immunostimulatory)
        MotifRule(
            name="AU-rich element",
            pattern=r"AUUUA",
            max_allowed=0,
            penalty_per_hit=0.25,
            description="AUUUA pentamer (ARE elements, immunostimulatory)",
        ),
        # U-rich stretches
        MotifRule(
            name="U-rich stretch",
            pattern=r"U{4,}[AUGC]?U{4,}",
            max_allowed=0,
            penalty_per_hit=0.2,
            description="U-rich stretches with optional single nucleotide interruption",
        ),
    ]


def _compute_score(hit_count: int, max_allowed: int, penalty_per_hit: float) -> float:
    """Compute score from hit count using penalty formula, clamped to [0, 1]."""
    excess = max(0, hit_count - max_allowed)
    raw = 1.0 - penalty_per_hit * excess
    return max(0.0, min(1.0, raw))


class MotifRuleEngine:
    """
    Rule-based motif scoring engine for mRNA sequences.
    Evaluates sequences against configurable rules and optional forbidden motifs.
    """

    def __init__(
        self,
        rules: list[MotifRule] | None = None,
        forbidden_motifs: list[str] | None = None,
    ) -> None:
        """
        Initialize the engine with rules and optional forbidden motifs.
        If rules is None, uses get_default_rules().
        If forbidden_motifs is provided, adds a rule for each motif (max_allowed=0).
        """
        self._rules: list[MotifRule] = rules if rules is not None else get_default_rules()
        if forbidden_motifs:
            for motif in forbidden_motifs:
                escaped = re.escape(motif)
                self._rules.append(
                    MotifRule(
                        name=f"Forbidden: {motif}",
                        pattern=escaped,
                        max_allowed=0,
                        penalty_per_hit=1.0,
                        description=f"Forbidden motif: {motif}",
                    )
                )

    def evaluate(self, sequence: str) -> list[MotifResult]:
        """
        Run all rules against the sequence and return a MotifResult for each rule.
        """
        results: list[MotifResult] = []
        seq_upper = sequence.upper()
        for rule in self._rules:
            matches = list(re.finditer(rule.pattern, seq_upper, re.IGNORECASE))
            hit_count = len(matches)
            positions = [m.start() for m in matches]
            score = _compute_score(hit_count, rule.max_allowed, rule.penalty_per_hit)
            if hit_count > rule.max_allowed:
                excess = hit_count - rule.max_allowed
                explanation = (
                    f"Found {hit_count} hit(s) (max allowed {rule.max_allowed}); "
                    f"{excess} excess incur penalty"
                )
            else:
                explanation = f"Within limit: {hit_count} hit(s), max allowed {rule.max_allowed}"
            results.append(
                MotifResult(
                    rule_name=rule.name,
                    score=score,
                    hit_count=hit_count,
                    positions=positions,
                    explanation=explanation,
                )
            )
        return results

    def score_homopolymers(self, sequence: str, max_run: int = 5) -> MotifResult:
        """
        Detect runs of the same nucleotide of length >= max_run.
        Returns a single MotifResult aggregating all homopolymer violations.
        """
        seq_upper = sequence.upper()
        pattern = rf"[AUGC]{{{max_run},}}"
        matches = list(re.finditer(pattern, seq_upper))
        hit_count = len(matches)
        positions = [m.start() for m in matches]
        # Use default penalty: 0.2 per hit, max_allowed=0
        score = _compute_score(hit_count, 0, 0.2)
        explanation = (
            f"Found {hit_count} homopolymer run(s) of length >= {max_run}"
            if hit_count > 0
            else f"No homopolymer runs of length >= {max_run}"
        )
        return MotifResult(
            rule_name="Homopolymer runs",
            score=score,
            hit_count=hit_count,
            positions=positions,
            explanation=explanation,
        )

    def score_dinucleotide_repeats(self, sequence: str) -> MotifResult:
        """
        Detect repeated dinucleotides (e.g. AUAUAUAU).
        Returns a single MotifResult aggregating dinucleotide repeat violations.
        """
        seq_upper = sequence.upper()
        dinucleotides = ["AU", "UA", "GC", "CG", "AG", "GA", "UC", "CU", "AC", "CA", "UG", "GU"]
        all_positions: list[int] = []
        for dinuc in dinucleotides:
            pattern = rf"({re.escape(dinuc)}){{4,}}"
            for m in re.finditer(pattern, seq_upper):
                all_positions.append(m.start())
        all_positions.sort()
        hit_count = len(all_positions)
        score = _compute_score(hit_count, 0, 0.15)
        explanation = (
            f"Found {hit_count} dinucleotide repeat(s)"
            if hit_count > 0
            else "No dinucleotide repeats"
        )
        return MotifResult(
            rule_name="Dinucleotide repeats",
            score=score,
            hit_count=hit_count,
            positions=all_positions,
            explanation=explanation,
        )

    def score_au_rich_elements(self, sequence: str) -> MotifResult:
        """
        Detect AUUUA pentamer motifs (ARE elements, immunostimulatory).
        """
        seq_upper = sequence.upper()
        pattern = r"AUUUA"
        matches = list(re.finditer(pattern, seq_upper))
        hit_count = len(matches)
        positions = [m.start() for m in matches]
        score = _compute_score(hit_count, 0, 0.25)
        explanation = (
            f"Found {hit_count} AU-rich element(s) (AUUUA)"
            if hit_count > 0
            else "No AU-rich elements"
        )
        return MotifResult(
            rule_name="AU-rich elements",
            score=score,
            hit_count=hit_count,
            positions=positions,
            explanation=explanation,
        )


def compute_aggregate_scores(results: list[MotifResult]) -> dict[str, float]:
    """
    Aggregate individual MotifResults into summary scores.
    Returns a dict with:
    - homopolymer_score: average of homopolymer rule scores
    - motif_risk_score: average of dinucleotide and pattern rule scores
    - forbidden_motif_score: average of forbidden motif rule scores (1.0 if none)
    - local_sequence_quality_score: overall average of all scores
    """
    homopolymer_results = [r for r in results if "homopolymer" in r.rule_name.lower()]
    forbidden_results = [r for r in results if "forbidden" in r.rule_name.lower()]
    motif_risk_results = [
        r
        for r in results
        if "homopolymer" not in r.rule_name.lower() and "forbidden" not in r.rule_name.lower()
    ]

    def _avg(scores: list[float]) -> float:
        return sum(scores) / len(scores) if scores else 1.0

    homopolymer_score = _avg([r.score for r in homopolymer_results])
    motif_risk_score = _avg([r.score for r in motif_risk_results])
    forbidden_motif_score = _avg([r.score for r in forbidden_results])
    local_sequence_quality_score = _avg([r.score for r in results])

    return {
        "homopolymer_score": homopolymer_score,
        "motif_risk_score": motif_risk_score,
        "forbidden_motif_score": forbidden_motif_score,
        "local_sequence_quality_score": local_sequence_quality_score,
    }
