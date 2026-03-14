"""
GPU-accelerated mRNA scoring functions using PyTorch.
All scoring operates on batched tensors for massive parallelism on the RTX 3090.

Nucleotide encoding: A=0, U=1, G=2, C=3
Sequences are int8 tensors of shape (batch_size, seq_len).
"""

import torch
import numpy as np
from codon_data import RELATIVE_ADAPTIVENESS, CODON_TO_AA, SYNONYMOUS_CODONS, CFTR_PROTEIN

# Build codon index -> relative adaptiveness lookup (64 entries, indexed by 3-digit base-4 encoding)
def _build_codon_ra_tensor(device):
    """Build a lookup tensor: codon_int -> relative_adaptiveness."""
    ra = torch.zeros(64, device=device, dtype=torch.float32)
    nuc_map = {"A": 0, "U": 1, "G": 2, "C": 3}
    for codon, w in RELATIVE_ADAPTIVENESS.items():
        idx = nuc_map[codon[0]] * 16 + nuc_map[codon[1]] * 4 + nuc_map[codon[2]]
        ra[idx] = w
    return ra


class GpuScorer:
    """Batch-scores mRNA candidate populations on GPU."""

    def __init__(self, device=None):
        if device is None:
            self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        else:
            self.device = torch.device(device)

        self.ra_lookup = _build_codon_ra_tensor(self.device)
        self._build_decode_tables()
        print(f"[GpuScorer] Using device: {self.device}")
        if self.device.type == "cuda":
            print(f"[GpuScorer] GPU: {torch.cuda.get_device_name(0)}")
            print(f"[GpuScorer] VRAM: {torch.cuda.get_device_properties(0).total_memory / 1e9:.1f} GB")

    def _build_decode_tables(self):
        """
        Build numpy lookup tables for fully vectorized codon->nucleotide decode.
        nuc_table[position, codon_choice, 0..2] = nucleotide id (A=0, U=1, G=2, C=3)
        choice_counts[position] = number of synonymous codons at that position
        """
        nuc_map = {"A": 0, "U": 1, "G": 2, "C": 3}
        prot_len = len(CFTR_PROTEIN)
        syn_lists = [SYNONYMOUS_CODONS.get(aa, ["AUG"]) for aa in CFTR_PROTEIN]
        max_syn = max(len(s) for s in syn_lists)

        self.nuc_table = np.zeros((prot_len, max_syn, 3), dtype=np.int8)
        self.choice_counts = np.zeros(prot_len, dtype=np.int8)

        for j, codons in enumerate(syn_lists):
            self.choice_counts[j] = len(codons)
            for k, codon in enumerate(codons):
                self.nuc_table[j, k] = [nuc_map[codon[0]], nuc_map[codon[1]], nuc_map[codon[2]]]

    def decode_population_fast(self, codon_choices: np.ndarray) -> torch.Tensor:
        """
        Fully vectorized decode: codon indices [N, L] -> nucleotide seqs [N, L*3].
        Uses numpy advanced indexing — no Python loops over individuals or positions.
        """
        N, L = codon_choices.shape
        choices = codon_choices % self.choice_counts[np.newaxis, :]
        pos_idx = np.arange(L)[np.newaxis, :]  # [1, L]
        triplets = self.nuc_table[pos_idx, choices]  # [N, L, 3]
        nuc_array = np.ascontiguousarray(triplets.reshape(N, L * 3))
        return torch.from_numpy(nuc_array).to(self.device)

    @torch.no_grad()
    def score_batch(self, seqs: torch.Tensor) -> dict:
        """
        Score a batch of nucleotide sequences on GPU.
        seqs: (batch_size, seq_len) int8 tensor with A=0, U=1, G=2, C=3.
        Returns dict of score tensors, each shape (batch_size,).
        """
        batch_size, seq_len = seqs.shape
        seqs_float = seqs.float()

        cai = self._cai_batch(seqs)
        gc_score = self._gc_score_batch(seqs_float, seq_len)
        cpg_score = self._cpg_score_batch(seqs, seq_len)
        u_score = self._uridine_score_batch(seqs_float, seq_len)
        rare_score = self._rare_codon_score_batch(seqs)
        repeat_score = self._repeat_score_batch(seqs, seq_len)
        pair_score = self._codon_pair_score_batch(seqs, seq_len)

        return {
            "cai": cai.cpu().numpy(),
            "gc_score": gc_score.cpu().numpy(),
            "cpg_score": cpg_score.cpu().numpy(),
            "uridine_score": u_score.cpu().numpy(),
            "rare_codon_score": rare_score.cpu().numpy(),
            "repeat_score": repeat_score.cpu().numpy(),
            "codon_pair_score": pair_score.cpu().numpy(),
        }

    def _cai_batch(self, seqs: torch.Tensor) -> torch.Tensor:
        """CAI = exp(mean(ln(w_i))) for each sequence in the batch."""
        batch_size, seq_len = seqs.shape
        n_codons = seq_len // 3

        # Encode each codon as base-4 integer: n0*16 + n1*4 + n2
        codon_ints = (seqs[:, 0::3].long() * 16 +
                      seqs[:, 1::3].long() * 4 +
                      seqs[:, 2::3].long())  # (batch, n_codons)

        # Lookup relative adaptiveness
        ra_vals = self.ra_lookup[codon_ints]  # (batch, n_codons)
        ra_vals = ra_vals.clamp(min=0.01)

        # CAI = geometric mean = exp(mean(log(w)))
        log_ra = torch.log(ra_vals)
        cai = torch.exp(log_ra.mean(dim=1))  # (batch,)
        return cai

    def _gc_score_batch(self, seqs_float: torch.Tensor, seq_len: int) -> torch.Tensor:
        """GC content score. Optimal: 45-55%."""
        is_g = (seqs_float == 2)
        is_c = (seqs_float == 3)
        gc_count = (is_g | is_c).float().sum(dim=1)
        gc_frac = gc_count / seq_len

        # Score: 1.0 if in [0.45, 0.55], linearly decreasing outside
        score = torch.ones_like(gc_frac)
        below = gc_frac < 0.45
        above = gc_frac > 0.55
        score[below] = (1.0 - (0.45 - gc_frac[below]) * 5.0).clamp(min=0)
        score[above] = (1.0 - (gc_frac[above] - 0.55) * 5.0).clamp(min=0)
        return score

    def _cpg_score_batch(self, seqs: torch.Tensor, seq_len: int) -> torch.Tensor:
        """CpG depletion score. Low CpG = good (immune evasion)."""
        is_c = (seqs[:, :-1] == 3)  # C at position i
        is_g = (seqs[:, 1:] == 2)   # G at position i+1
        cpg_count = (is_c & is_g).float().sum(dim=1)

        c_count = (seqs == 3).float().sum(dim=1)
        g_count = (seqs == 2).float().sum(dim=1)
        expected = (c_count * g_count) / seq_len
        expected = expected.clamp(min=1.0)

        ratio = cpg_count / expected
        # Score: 1.0 if ratio <= 0.4, 0.0 if ratio >= 1.0
        score = ((1.0 - ratio) / 0.6).clamp(0, 1)
        score[ratio <= 0.4] = 1.0
        return score

    def _uridine_score_batch(self, seqs_float: torch.Tensor, seq_len: int) -> torch.Tensor:
        """Uridine fraction score. Less U = better."""
        u_frac = (seqs_float == 1).float().sum(dim=1) / seq_len
        # 1.0 at <=0.15, 0.0 at >=0.35
        score = ((0.35 - u_frac) / 0.20).clamp(0, 1)
        return score

    def _rare_codon_score_batch(self, seqs: torch.Tensor) -> torch.Tensor:
        """Rare codon cluster avoidance."""
        batch_size, seq_len = seqs.shape
        n_codons = seq_len // 3

        codon_ints = (seqs[:, 0::3].long() * 16 +
                      seqs[:, 1::3].long() * 4 +
                      seqs[:, 2::3].long())
        ra_vals = self.ra_lookup[codon_ints]

        is_rare = (ra_vals < 0.3).float()  # (batch, n_codons)

        # Detect runs of 3+ consecutive rare codons using convolution
        if n_codons >= 3:
            kernel = torch.ones(1, 1, 3, device=seqs.device)
            rare_1d = is_rare.unsqueeze(1)  # (batch, 1, n_codons)
            conv = torch.nn.functional.conv1d(rare_1d, kernel, padding=0)  # (batch, 1, n-2)
            cluster_count = (conv.squeeze(1) >= 3.0).float().sum(dim=1)
        else:
            cluster_count = torch.zeros(batch_size, device=seqs.device)

        score = (1.0 - cluster_count * 0.1).clamp(min=0)
        return score

    def _repeat_score_batch(self, seqs: torch.Tensor, seq_len: int) -> torch.Tensor:
        """Homopolymer run penalty."""
        # Detect runs of 6+ identical nucleotides
        same_as_prev = (seqs[:, 1:] == seqs[:, :-1]).float()  # (batch, seq_len-1)

        # Use conv1d to detect runs of 5 consecutive "same" (= run of 6)
        if seq_len >= 6:
            kernel = torch.ones(1, 1, 5, device=seqs.device)
            same_1d = same_as_prev.unsqueeze(1)
            conv = torch.nn.functional.conv1d(same_1d, kernel, padding=0)
            violations = (conv.squeeze(1) >= 5.0).float().sum(dim=1)
        else:
            violations = torch.zeros(seqs.shape[0], device=seqs.device)

        score = (1.0 - violations * 0.05).clamp(min=0)
        return score

    def _codon_pair_score_batch(self, seqs: torch.Tensor, seq_len: int) -> torch.Tensor:
        """Penalize CpG and UpA at codon junctions."""
        n_codons = seq_len // 3
        if n_codons < 2:
            return torch.ones(seqs.shape[0], device=seqs.device)

        # Junction: last nt of codon i (pos i*3+2) and first nt of codon i+1 (pos i*3+3)
        last_nts = seqs[:, 2::3][:, :-1]   # (batch, n_codons-1)
        first_nts = seqs[:, 3::3]           # (batch, n_codons-1)

        # CpG at junction: C(3) followed by G(2)
        cpg_junctions = ((last_nts == 3) & (first_nts == 2)).float().sum(dim=1)
        # UpA at junction: U(1) followed by A(0)
        upa_junctions = ((last_nts == 1) & (first_nts == 0)).float().sum(dim=1)

        bad_frac = (cpg_junctions + upa_junctions) / (n_codons - 1)
        score = (1.0 - bad_frac * 3.0).clamp(min=0)
        return score
