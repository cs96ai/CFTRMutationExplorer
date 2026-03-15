"""
Phase 5 mRNA rescoring pipeline configuration.

Provides configurable parameters, weight presets, and factory functions
for the rescoring pipeline.
"""

from dataclasses import dataclass, field


def _default_forbidden_motifs() -> list[str]:
    """Default restriction enzyme sites in RNA alphabet (U not T)."""
    return [
        "GAAUUC",   # EcoRI
        "GGAUCC",   # BamHI
        "AAGCUU",   # HindIII
        "CUGCAG",   # PstI
        "GGUACC",   # KpnI
        "GAGCUC",   # SacI
        "UCUAGA",   # XbaI
        "GUCGAC",   # SalI
        "CGCG",     # BssHII / PvuI
        "GGCC",     # HaeIII
    ]


@dataclass
class Phase5Config:
    """Configuration for Phase 5 mRNA rescoring pipeline."""

    top_n: int = 50
    """Number of top candidates to rescore from initial ranking."""

    gc_window_30_low: float = 0.40
    """Lower bound for 30 nt sliding window GC content (fraction)."""
    gc_window_30_high: float = 0.65
    """Upper bound for 30 nt sliding window GC content (fraction)."""
    gc_window_50_low: float = 0.42
    """Lower bound for 50 nt sliding window GC content (fraction)."""
    gc_window_50_high: float = 0.62
    """Upper bound for 50 nt sliding window GC content (fraction)."""

    max_homopolymer_run: int = 5
    """Maximum allowed consecutive identical nucleotides before penalty."""

    forbidden_motifs: list[str] = field(default_factory=_default_forbidden_motifs)
    """Motifs to exclude (e.g. restriction enzyme sites in RNA alphabet)."""

    diversity_threshold: float = 0.05
    """Minimum diversity as fraction of sequence length between candidates."""

    diversity_top_k: int = 10
    """Number of top candidates to consider for diversity filtering."""

    folding_window_size: int = 120
    """Window size in nt for local folding analysis."""
    folding_window_step: int = 60
    """Step size in nt between folding windows."""

    saturation_threshold: float = 0.95
    """Score fraction above which dampening is applied."""
    saturation_dampen_factor: float = 0.5
    """Factor by which scores above threshold are dampened."""


# Weight presets: name -> dict of metric -> weight
WEIGHT_PRESETS: dict[str, dict[str, float]] = {
    "phase5_balanced": {
        "structure_global": 1.2,
        "structure_5prime": 1.2,
        "gc_window_30": 0.8,
        "gc_window_50": 0.8,
        "gc_variance": 0.6,
        "homopolymer": 0.8,
        "motif_risk": 0.8,
        "forbidden_motif": 1.0,
        "codon_diversity": 1.0,
        "codon_run_penalty": 0.8,
        "codon_pattern": 0.8,
        "legacy_cai": 1.0,
        "legacy_gc": 0.6,
        "legacy_cpg": 0.6,
        "legacy_uridine": 0.6,
        "legacy_rare_codon": 0.8,
        "legacy_repeat": 0.6,
        "legacy_codon_pair": 0.8,
    },
    "translation_heavy": {
        "structure_global": 0.8,
        "structure_5prime": 1.5,
        "gc_window_30": 0.5,
        "gc_window_50": 0.5,
        "gc_variance": 0.4,
        "homopolymer": 0.5,
        "motif_risk": 0.5,
        "forbidden_motif": 0.8,
        "codon_diversity": 1.5,
        "codon_run_penalty": 1.0,
        "codon_pattern": 1.2,
        "legacy_cai": 1.8,
        "legacy_gc": 0.4,
        "legacy_cpg": 0.4,
        "legacy_uridine": 0.4,
        "legacy_rare_codon": 1.2,
        "legacy_repeat": 0.4,
        "legacy_codon_pair": 1.2,
    },
    "immune_stealth_heavy": {
        "structure_global": 0.6,
        "structure_5prime": 0.6,
        "gc_window_30": 0.5,
        "gc_window_50": 0.5,
        "gc_variance": 0.4,
        "homopolymer": 0.6,
        "motif_risk": 0.8,
        "forbidden_motif": 1.8,
        "codon_diversity": 0.6,
        "codon_run_penalty": 0.5,
        "codon_pattern": 0.5,
        "legacy_cai": 0.5,
        "legacy_gc": 0.5,
        "legacy_cpg": 1.5,
        "legacy_uridine": 1.5,
        "legacy_rare_codon": 0.5,
        "legacy_repeat": 0.6,
        "legacy_codon_pair": 0.5,
    },
    "manufacturability_heavy": {
        "structure_global": 0.5,
        "structure_5prime": 0.5,
        "gc_window_30": 1.2,
        "gc_window_50": 1.2,
        "gc_variance": 1.2,
        "homopolymer": 1.5,
        "motif_risk": 1.5,
        "forbidden_motif": 1.2,
        "codon_diversity": 0.5,
        "codon_run_penalty": 0.8,
        "codon_pattern": 0.6,
        "legacy_cai": 0.5,
        "legacy_gc": 0.8,
        "legacy_cpg": 0.5,
        "legacy_uridine": 0.5,
        "legacy_rare_codon": 0.5,
        "legacy_repeat": 0.8,
        "legacy_codon_pair": 0.6,
    },
    "structure_heavy": {
        "structure_global": 1.8,
        "structure_5prime": 1.8,
        "gc_window_30": 1.2,
        "gc_window_50": 1.2,
        "gc_variance": 0.8,
        "homopolymer": 0.6,
        "motif_risk": 0.6,
        "forbidden_motif": 0.8,
        "codon_diversity": 0.6,
        "codon_run_penalty": 0.6,
        "codon_pattern": 0.6,
        "legacy_cai": 0.6,
        "legacy_gc": 0.6,
        "legacy_cpg": 0.5,
        "legacy_uridine": 0.5,
        "legacy_rare_codon": 0.5,
        "legacy_repeat": 0.5,
        "legacy_codon_pair": 0.5,
    },
}


def get_preset(name: str) -> dict[str, float]:
    """Return weight preset by name. Raises KeyError if preset not found."""
    return WEIGHT_PRESETS[name].copy()


def list_presets() -> list[str]:
    """Return list of available preset names."""
    return list(WEIGHT_PRESETS.keys())


def get_default_config() -> Phase5Config:
    """Return a Phase5Config instance with default values."""
    return Phase5Config()
