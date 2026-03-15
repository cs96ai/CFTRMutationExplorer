"""
RNA folding abstraction for Phase 5 mRNA rescoring pipeline.

Provides pluggable backends (ViennaRNACuda, ViennaRNA, Nussinov) for secondary
structure prediction. All energy values are in kcal/mol; MFE is negative
(more negative = more stable). Input sequences use RNA alphabet: A, U, G, C.

ViennaRNACuda: Uses RNAfold_simple.exe from C:\\code\\ViennaRNACuda (subprocess).
Configure via VIENNARNA_EXE env var.
"""

import os
import subprocess
from abc import ABC, abstractmethod
from dataclasses import dataclass
from typing import Set, Tuple

# Default path for ViennaRNACuda RNAfold_simple.exe (Windows build)
DEFAULT_VIENNARNA_EXE = r"C:\code\ViennaRNACuda\build\Release\RNAfold_simple.exe"

# Energy per base pair in simplified Nussinov model (kcal/mol)
ENERGY_PER_PAIR = -1.5

# Valid base pairs in RNA (canonical + wobble)
VALID_PAIRS: Set[Tuple[str, str]] = {
    ("A", "U"),
    ("U", "A"),
    ("G", "C"),
    ("C", "G"),
    ("G", "U"),
    ("U", "G"),
}

# Maximum sequence length for direct Nussinov folding (beyond this, use sliding window)
NUSSINOV_WINDOW_SIZE = 500


@dataclass
class FoldingResult:
    """
    Result of an RNA folding computation.

    Attributes:
        structure: Secondary structure in dot-bracket notation ('.', '(', ')').
        mfe: Minimum free energy in kcal/mol (negative = stable).
        is_approximate: True if a fallback or simplified model was used.
    """

    structure: str
    mfe: float
    is_approximate: bool


class FoldingBackend(ABC):
    """
    Abstract base class for RNA folding backends.

    Implementations provide secondary structure prediction and MFE estimation
    for RNA sequences.
    """

    @property
    @abstractmethod
    def name(self) -> str:
        """Human-readable backend name."""
        pass

    @property
    @abstractmethod
    def is_approximate(self) -> bool:
        """Whether this backend uses an approximate/simplified energy model."""
        pass

    @abstractmethod
    def fold(self, sequence: str) -> FoldingResult:
        """Fold the full sequence and return structure and MFE."""
        pass

    @abstractmethod
    def fold_region(self, sequence: str, start: int, end: int) -> FoldingResult:
        """Fold the subsequence [start, end) and return structure and MFE."""
        pass


class NussinovBackend(FoldingBackend):
    """
    Nussinov-style dynamic programming backend for base pair maximization.

    Uses a simplified energy model: MFE approximated as -1.5 kcal/mol per
    predicted base pair. Valid pairs: AU, UA, GC, CG, GU, UG. For sequences
    longer than 500 nt, uses sliding windows and aggregates results.
    """

    def __init__(self) -> None:
        pass

    @property
    def name(self) -> str:
        return "Nussinov"

    @property
    def is_approximate(self) -> bool:
        return True

    def _can_pair(self, a: str, b: str) -> bool:
        """Check if two bases can form a valid base pair."""
        return (a, b) in VALID_PAIRS

    def _nussinov_fold(self, sequence: str) -> Tuple[str, float]:
        """
        Run Nussinov DP on a sequence. Returns (structure, mfe).
        Assumes sequence length <= NUSSINOV_WINDOW_SIZE for efficiency.
        """
        n = len(sequence)
        if n == 0:
            return "", 0.0

        # dp[i][j] = max base pairs in sequence[i:j+1]
        dp = [[0] * n for _ in range(n)]
        trace = [[-1] * n for _ in range(n)]  # -1: unset, 0: i unpaired, 1: j unpaired, 2: pair, 3: bifurc at k

        for length in range(1, n):
            for i in range(n - length):
                j = i + length
                best = 0
                best_trace = -1
                best_k = -1

                # i unpaired
                if dp[i + 1][j] > best:
                    best = dp[i + 1][j]
                    best_trace = 0

                # j unpaired
                if dp[i][j - 1] > best:
                    best = dp[i][j - 1]
                    best_trace = 1

                # i,j pair
                if self._can_pair(sequence[i], sequence[j]) and (j - i > 1):
                    cand = 1 + dp[i + 1][j - 1]
                    if cand > best:
                        best = cand
                        best_trace = 2

                # Bifurcation
                for k in range(i, j):
                    cand = dp[i][k] + dp[k + 1][j]
                    if cand > best:
                        best = cand
                        best_trace = 3
                        best_k = k

                dp[i][j] = best
                if best_trace == 0:
                    trace[i][j] = 0
                elif best_trace == 1:
                    trace[i][j] = 1
                elif best_trace == 2:
                    trace[i][j] = 2
                else:
                    trace[i][j] = (3 << 16) | best_k

        # Traceback to build dot-bracket structure
        structure = ["."] * n

        def traceback(lo: int, hi: int) -> None:
            if lo >= hi:
                return
            t = trace[lo][hi]
            if t == -1:
                return
            if t == 0:
                traceback(lo + 1, hi)
            elif t == 1:
                traceback(lo, hi - 1)
            elif t == 2:
                structure[lo] = "("
                structure[hi] = ")"
                traceback(lo + 1, hi - 1)
            else:
                k = t & 0xFFFF
                traceback(lo, k)
                traceback(k + 1, hi)

        traceback(0, n - 1)
        num_pairs = dp[0][n - 1]
        mfe = num_pairs * ENERGY_PER_PAIR
        return "".join(structure), mfe

    def fold(self, sequence: str) -> FoldingResult:
        """Fold the full sequence. For long sequences (>500 nt), use sliding windows."""
        seq = sequence.upper().replace("T", "U")
        n = len(seq)

        if n <= NUSSINOV_WINDOW_SIZE:
            structure, mfe = self._nussinov_fold(seq)
            return FoldingResult(structure=structure, mfe=mfe, is_approximate=True)

        # Sliding window: non-overlapping 500 nt chunks
        structures = []
        total_mfe = 0.0
        pos = 0
        while pos < n:
            end = min(pos + NUSSINOV_WINDOW_SIZE, n)
            subseq = seq[pos:end]
            struct, mfe = self._nussinov_fold(subseq)
            structures.append(struct)
            total_mfe += mfe
            pos = end

        return FoldingResult(
            structure="".join(structures),
            mfe=total_mfe,
            is_approximate=True,
        )

    def fold_region(self, sequence: str, start: int, end: int) -> FoldingResult:
        """Extract subsequence [start, end) and fold it."""
        subseq = sequence[start:end]
        return self.fold(subseq)


class ViennaRNACudaBackend(FoldingBackend):
    """
    ViennaRNACuda backend: calls RNAfold_simple.exe via subprocess.

    Uses the custom Windows build at C:\\code\\ViennaRNACuda. Path configurable
    via VIENNARNA_EXE environment variable.
    """

    def __init__(self, exe_path: str | None = None) -> None:
        self._exe = exe_path or os.environ.get("VIENNARNA_EXE", DEFAULT_VIENNARNA_EXE)
        self._cwd = os.path.dirname(self._exe) or "."
        if not os.path.isfile(self._exe):
            raise FileNotFoundError(
                f"ViennaRNACuda RNAfold_simple.exe not found: {self._exe}. "
                "Build with: cd C:\\code\\ViennaRNACuda && .\\build.ps1"
            )

    @property
    def name(self) -> str:
        return "ViennaRNACuda"

    @property
    def is_approximate(self) -> bool:
        return False

    def _rnafold_subprocess(self, sequence: str) -> Tuple[str, float]:
        """Call RNAfold_simple.exe. Returns (structure, mfe)."""
        result = subprocess.run(
            [self._exe],
            input=sequence.encode(),
            capture_output=True,
            text=False,
            cwd=self._cwd,
            timeout=60,
        )
        result.check_returncode()
        lines = result.stdout.decode().strip().split("\n")
        if len(lines) < 2:
            raise ValueError(f"Unexpected RNAfold output: {result.stdout.decode()}")
        struct_line = lines[1]
        struct, energy_str = struct_line.rsplit(" ", 1)
        energy = float(energy_str.strip("()"))
        return struct.strip(), energy

    def fold(self, sequence: str) -> FoldingResult:
        """Fold via RNAfold_simple.exe subprocess."""
        seq = sequence.upper().replace("T", "U")
        structure, mfe = self._rnafold_subprocess(seq)
        return FoldingResult(
            structure=structure,
            mfe=float(mfe),
            is_approximate=False,
        )

    def fold_region(self, sequence: str, start: int, end: int) -> FoldingResult:
        """Extract subsequence [start, end) and fold it."""
        subseq = sequence[start:end]
        return self.fold(subseq)


class ViennaRNABackend(FoldingBackend):
    """
    ViennaRNA pip package backend for RNA folding.

    Uses RNA.fold() for structure and MFE. Raises ImportError in __init__
    if ViennaRNA is not installed.
    """

    def __init__(self) -> None:
        try:
            import RNA as _RNA
            self._RNA = _RNA
        except ImportError as e:
            raise ImportError(
                "ViennaRNA package is required for ViennaRNABackend. "
                "Install with: pip install ViennaRNA"
            ) from e

    @property
    def name(self) -> str:
        return "ViennaRNA"

    @property
    def is_approximate(self) -> bool:
        return False

    def fold(self, sequence: str) -> FoldingResult:
        """Call RNA.fold(sequence) to get structure and MFE. Uses ViennaRNA C library."""
        seq = sequence.upper().replace("T", "U")
        structure, mfe = self._RNA.fold(seq)
        # ViennaRNA may include trailing newline in structure
        structure = (structure or "").strip()
        return FoldingResult(
            structure=structure,
            mfe=float(mfe),
            is_approximate=False,
        )

    def fold_region(self, sequence: str, start: int, end: int) -> FoldingResult:
        """Extract subsequence [start, end) and fold it."""
        subseq = sequence[start:end]
        return self.fold(subseq)


def get_best_backend() -> FoldingBackend:
    """
    Return the best available folding backend.

    Tries ViennaRNACuda (RNAfold_simple.exe) first, then ViennaRNA pip package,
    then NussinovBackend as fallback. Prints which backend is being used.
    """
    # 1. ViennaRNACuda (custom Windows build via subprocess)
    try:
        backend = ViennaRNACudaBackend()
        print(f"Using folding backend: {backend.name}")
        return backend
    except FileNotFoundError:
        pass
    # 2. ViennaRNA pip package
    try:
        backend = ViennaRNABackend()
        print(f"Using folding backend: {backend.name}")
        return backend
    except ImportError:
        pass
    # 3. Nussinov fallback
    backend = NussinovBackend()
    print(f"Using folding backend: {backend.name} (fallback, approximate)")
    return backend
