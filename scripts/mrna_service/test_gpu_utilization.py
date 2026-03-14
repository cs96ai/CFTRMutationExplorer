"""
GPU utilization tests for the mRNA scoring engine.

Verifies that:
  1. CUDA is available and the RTX 3090 is detected
  2. All tensors and operations execute on GPU (not CPU)
  3. GPU memory is actually allocated during scoring
  4. Batch parallelism works — scoring 1000 seqs is NOT 100x slower than 10
  5. All 7 scoring functions return valid results on GPU
  6. Multi-stream concurrency is exercised via large batches
  7. The full CFTR protein (1480 aa = 4440 nt) is handled at scale

Run:  python test_gpu_utilization.py
  or:  pytest test_gpu_utilization.py -v
"""

import sys
import os
import time
import unittest
import numpy as np

# Ensure local imports work
sys.path.insert(0, os.path.dirname(__file__))

import torch
from scoring_gpu import GpuScorer, _build_codon_ra_tensor
from codon_data import CFTR_PROTEIN, SYNONYMOUS_CODONS, HUMAN_FREQ


def _random_codon_choices(batch_size, prot_len):
    """Generate random codon choice indices for the CFTR protein."""
    choices = np.zeros((batch_size, prot_len), dtype=np.int8)
    for j, aa in enumerate(CFTR_PROTEIN[:prot_len]):
        n = len(SYNONYMOUS_CODONS.get(aa, ["AUG"]))
        choices[:, j] = np.random.randint(0, max(n, 1), size=batch_size)
    return choices


class TestGpuDetection(unittest.TestCase):
    """Verify CUDA device detection and GPU properties."""

    def test_cuda_is_available(self):
        self.assertTrue(torch.cuda.is_available(), "CUDA must be available")

    def test_gpu_is_rtx_3090(self):
        name = torch.cuda.get_device_name(0)
        self.assertIn("3090", name, f"Expected RTX 3090, got: {name}")

    def test_gpu_has_sufficient_vram(self):
        props = torch.cuda.get_device_properties(0)
        vram_gb = props.total_memory / 1e9
        self.assertGreater(vram_gb, 20.0, f"Expected >20 GB VRAM, got {vram_gb:.1f} GB")

    def test_gpu_compute_capability(self):
        major, minor = torch.cuda.get_device_capability(0)
        # RTX 3090 is Ampere = compute capability 8.6
        self.assertGreaterEqual(major, 8, f"Expected compute capability >= 8.x, got {major}.{minor}")

    def test_cuda_stream_creation(self):
        """Verify we can create multiple CUDA streams for concurrent execution."""
        streams = [torch.cuda.Stream() for _ in range(4)]
        self.assertEqual(len(streams), 4)
        for s in streams:
            self.assertIsNotNone(s)

    def test_sm_count(self):
        """RTX 3090 has 82 streaming multiprocessors."""
        props = torch.cuda.get_device_properties(0)
        sm_count = props.multi_processor_count
        print(f"  GPU SMs (streaming multiprocessors): {sm_count}")
        # RTX 3090 = 82 SMs, each with 128 CUDA cores = 10496 total
        self.assertGreaterEqual(sm_count, 80, f"Expected >= 80 SMs, got {sm_count}")


class TestGpuTensorPlacement(unittest.TestCase):
    """Verify all tensors are created and operate on GPU, not CPU."""

    @classmethod
    def setUpClass(cls):
        cls.scorer = GpuScorer(device="cuda")
        cls.prot_len = len(CFTR_PROTEIN)
        cls.choices = _random_codon_choices(50, cls.prot_len)
        cls.seqs = cls.scorer.decode_population_fast(cls.choices)

    def test_scorer_device_is_cuda(self):
        self.assertEqual(self.scorer.device.type, "cuda")

    def test_ra_lookup_on_gpu(self):
        self.assertEqual(self.scorer.ra_lookup.device.type, "cuda",
                         "Relative adaptiveness lookup must be on GPU")

    def test_decoded_sequences_on_gpu(self):
        self.assertEqual(self.seqs.device.type, "cuda",
                         "Decoded sequences must be on GPU")

    def test_decoded_shape(self):
        self.assertEqual(self.seqs.shape, (50, self.prot_len * 3))

    def test_intermediate_tensors_on_gpu(self):
        """Verify internal scoring tensors stay on GPU during computation."""
        seqs = self.seqs
        seqs_float = seqs.float()
        self.assertEqual(seqs_float.device.type, "cuda")

        # CAI internals
        codon_ints = (seqs[:, 0::3].long() * 16 +
                      seqs[:, 1::3].long() * 4 +
                      seqs[:, 2::3].long())
        self.assertEqual(codon_ints.device.type, "cuda")

        ra_vals = self.scorer.ra_lookup[codon_ints]
        self.assertEqual(ra_vals.device.type, "cuda")

    def test_score_results_are_numpy_on_cpu(self):
        """Final results should be numpy arrays (transferred from GPU to CPU)."""
        scores = self.scorer.score_batch(self.seqs)
        for key, arr in scores.items():
            self.assertIsInstance(arr, np.ndarray, f"{key} should be numpy array")
            self.assertEqual(arr.shape, (50,), f"{key} should have shape (50,)")


class TestGpuMemoryAllocation(unittest.TestCase):
    """Verify GPU memory is actually used during scoring."""

    def test_memory_allocated_during_scoring(self):
        torch.cuda.reset_peak_memory_stats()
        torch.cuda.empty_cache()

        mem_before = torch.cuda.memory_allocated()

        scorer = GpuScorer(device="cuda")
        choices = _random_codon_choices(500, len(CFTR_PROTEIN))
        seqs = scorer.decode_population_fast(choices)
        _ = scorer.score_batch(seqs)

        mem_peak = torch.cuda.max_memory_allocated()
        mem_used = mem_peak - mem_before

        print(f"  GPU memory allocated: {mem_used / 1e6:.1f} MB")
        print(f"  GPU peak memory: {mem_peak / 1e6:.1f} MB")

        # Scoring 500 sequences of 4440 nt should use meaningful GPU memory
        self.assertGreater(mem_used, 1e6,
                           f"Expected >1 MB GPU allocation, got {mem_used / 1e6:.2f} MB — "
                           "scoring may not be running on GPU")

    def test_no_cpu_fallback(self):
        """Ensure the scorer doesn't silently fall back to CPU."""
        scorer = GpuScorer(device="cuda")
        self.assertNotEqual(scorer.device.type, "cpu",
                            "Scorer must not fall back to CPU")


class TestGpuParallelism(unittest.TestCase):
    """
    Core parallelism test: prove the GPU is doing batch-parallel work.
    If scoring is truly parallel, scoring 1000 sequences should take
    roughly the same time as scoring 10 (GPU launches are batch-wide).
    """

    @classmethod
    def setUpClass(cls):
        cls.scorer = GpuScorer(device="cuda")
        cls.prot_len = len(CFTR_PROTEIN)
        # Warm up GPU (first call includes JIT overhead)
        warmup = _random_codon_choices(10, cls.prot_len)
        seqs = cls.scorer.decode_population_fast(warmup)
        _ = cls.scorer.score_batch(seqs)
        torch.cuda.synchronize()

    def _time_scoring(self, batch_size, repeats=5):
        """Time the scoring of a batch, returning median seconds."""
        choices = _random_codon_choices(batch_size, self.prot_len)
        seqs = self.scorer.decode_population_fast(choices)

        times = []
        for _ in range(repeats):
            torch.cuda.synchronize()
            t0 = time.perf_counter()
            _ = self.scorer.score_batch(seqs)
            torch.cuda.synchronize()
            t1 = time.perf_counter()
            times.append(t1 - t0)

        return sorted(times)[len(times) // 2]  # median

    def test_batch_parallelism_10_vs_1000(self):
        """
        Scoring 1000 sequences should NOT be 100x slower than 10.
        On a truly parallel GPU, the ratio should be well under 10x
        because CUDA kernels process entire batches in parallel.
        """
        t_10 = self._time_scoring(10)
        t_1000 = self._time_scoring(1000)
        ratio = t_1000 / max(t_10, 1e-9)

        print(f"  Batch  10: {t_10*1000:.1f} ms")
        print(f"  Batch 1000: {t_1000*1000:.1f} ms")
        print(f"  Ratio (1000/10): {ratio:.1f}x  (ideal: ~1-3x for GPU parallelism)")

        # If this were sequential CPU, ratio would be ~100x.
        # GPU parallelism should keep it well under 20x.
        self.assertLess(ratio, 20.0,
                        f"Batch 1000 was {ratio:.1f}x slower than batch 10 — "
                        "scoring may not be using GPU parallelism")

    def test_batch_parallelism_100_vs_2000(self):
        """Larger scale parallelism test."""
        t_100 = self._time_scoring(100)
        t_2000 = self._time_scoring(2000)
        ratio = t_2000 / max(t_100, 1e-9)

        print(f"  Batch  100: {t_100*1000:.1f} ms")
        print(f"  Batch 2000: {t_2000*1000:.1f} ms")
        print(f"  Ratio (2000/100): {ratio:.1f}x  (ideal: ~1-5x for GPU parallelism)")

        self.assertLess(ratio, 30.0,
                        f"Batch 2000 was {ratio:.1f}x slower than batch 100")

    def test_throughput_at_scale(self):
        """Verify we achieve reasonable throughput with production batch size."""
        batch = 500
        choices = _random_codon_choices(batch, self.prot_len)
        seqs = self.scorer.decode_population_fast(choices)

        torch.cuda.synchronize()
        t0 = time.perf_counter()
        for _ in range(10):
            _ = self.scorer.score_batch(seqs)
        torch.cuda.synchronize()
        elapsed = time.perf_counter() - t0

        seqs_per_sec = (batch * 10) / elapsed
        print(f"  Throughput: {seqs_per_sec:.0f} sequences/sec (500-batch x 10 reps)")

        # Should sustain at least 100 seq/s on a 3090
        self.assertGreater(seqs_per_sec, 100,
                           f"Throughput {seqs_per_sec:.0f} seq/s is too low for GPU scoring")


class TestAllScoringFunctions(unittest.TestCase):
    """Verify all 7 scoring functions return valid [0,1] results on GPU."""

    @classmethod
    def setUpClass(cls):
        cls.scorer = GpuScorer(device="cuda")
        cls.choices = _random_codon_choices(200, len(CFTR_PROTEIN))
        cls.seqs = cls.scorer.decode_population_fast(cls.choices)
        cls.scores = cls.scorer.score_batch(cls.seqs)

    def test_all_score_keys_present(self):
        expected = {"cai", "gc_score", "cpg_score", "uridine_score",
                    "rare_codon_score", "repeat_score", "codon_pair_score"}
        self.assertEqual(set(self.scores.keys()), expected)

    def test_cai_valid_range(self):
        vals = self.scores["cai"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"CAI out of range: min={vals.min():.4f}, max={vals.max():.4f}")
        self.assertTrue(np.all(np.isfinite(vals)), "CAI contains NaN/Inf")

    def test_gc_score_valid_range(self):
        vals = self.scores["gc_score"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"GC score out of range: min={vals.min():.4f}, max={vals.max():.4f}")

    def test_cpg_score_valid_range(self):
        vals = self.scores["cpg_score"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"CpG score out of range: min={vals.min():.4f}, max={vals.max():.4f}")

    def test_uridine_score_valid_range(self):
        vals = self.scores["uridine_score"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"Uridine score out of range: min={vals.min():.4f}, max={vals.max():.4f}")

    def test_rare_codon_score_valid_range(self):
        vals = self.scores["rare_codon_score"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"Rare codon score out of range: min={vals.min():.4f}, max={vals.max():.4f}")

    def test_repeat_score_valid_range(self):
        vals = self.scores["repeat_score"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"Repeat score out of range: min={vals.min():.4f}, max={vals.max():.4f}")

    def test_codon_pair_score_valid_range(self):
        vals = self.scores["codon_pair_score"]
        self.assertTrue(np.all(vals >= 0) and np.all(vals <= 1),
                        f"Codon pair score out of range: min={vals.min():.4f}, max={vals.max():.4f}")

    def test_scores_have_variance(self):
        """Random sequences should produce varying scores (not all identical)."""
        for key, vals in self.scores.items():
            std = np.std(vals)
            self.assertGreater(std, 1e-6,
                               f"{key} has zero variance — scoring may be broken")

    def test_optimal_codons_have_high_cai(self):
        """A sequence using only the highest-frequency codons should have CAI near 1.0."""
        optimal = np.zeros((1, len(CFTR_PROTEIN)), dtype=np.int8)  # index 0 = most frequent
        seqs = self.scorer.decode_population_fast(optimal)
        scores = self.scorer.score_batch(seqs)
        cai = scores["cai"][0]
        print(f"  Optimal-codon CAI: {cai:.4f}")
        self.assertGreater(cai, 0.9, f"Optimal CAI should be near 1.0, got {cai:.4f}")


class TestGpuConcurrentKernels(unittest.TestCase):
    """Test that the GPU can handle concurrent operations via CUDA streams."""

    def test_multi_stream_scoring(self):
        """Score multiple batches on different CUDA streams concurrently."""
        if not torch.cuda.is_available():
            self.skipTest("CUDA not available")

        scorer = GpuScorer(device="cuda")
        prot_len = len(CFTR_PROTEIN)

        streams = [torch.cuda.Stream() for _ in range(4)]
        results = [None] * 4

        choices_list = [_random_codon_choices(100, prot_len) for _ in range(4)]

        torch.cuda.synchronize()
        t0 = time.perf_counter()

        for i, stream in enumerate(streams):
            with torch.cuda.stream(stream):
                seqs = scorer.decode_population_fast(choices_list[i])
                results[i] = scorer.score_batch(seqs)

        torch.cuda.synchronize()
        elapsed = time.perf_counter() - t0

        print(f"  4-stream concurrent scoring: {elapsed*1000:.1f} ms")

        for i, r in enumerate(results):
            self.assertIsNotNone(r, f"Stream {i} produced no results")
            self.assertEqual(len(r["cai"]), 100)

    def test_large_batch_exercises_all_sms(self):
        """
        A batch of 5000 sequences of 4440 nt each should fully saturate
        all 82 SMs on the RTX 3090. We check that it completes and
        GPU memory usage is substantial.
        """
        torch.cuda.reset_peak_memory_stats()
        scorer = GpuScorer(device="cuda")
        choices = _random_codon_choices(5000, len(CFTR_PROTEIN))
        seqs = scorer.decode_population_fast(choices)

        torch.cuda.synchronize()
        t0 = time.perf_counter()
        scores = scorer.score_batch(seqs)
        torch.cuda.synchronize()
        elapsed = time.perf_counter() - t0

        peak_mb = torch.cuda.max_memory_allocated() / 1e6
        print(f"  Batch 5000 scoring: {elapsed*1000:.1f} ms, peak GPU mem: {peak_mb:.0f} MB")

        self.assertEqual(len(scores["cai"]), 5000)
        self.assertGreater(peak_mb, 50,
                           "Large batch should use significant GPU memory")


class TestCpuVsGpuConsistency(unittest.TestCase):
    """Verify GPU and CPU produce the same results (no device-specific bugs)."""

    def test_gpu_cpu_scores_match(self):
        """Score the same sequences on GPU and CPU; results should match."""
        prot_len = len(CFTR_PROTEIN)
        choices = _random_codon_choices(20, prot_len)

        scorer_gpu = GpuScorer(device="cuda")
        scorer_cpu = GpuScorer(device="cpu")

        seqs_gpu = scorer_gpu.decode_population_fast(choices)
        seqs_cpu = scorer_cpu.decode_population_fast(choices)

        scores_gpu = scorer_gpu.score_batch(seqs_gpu)
        scores_cpu = scorer_cpu.score_batch(seqs_cpu)

        for key in scores_gpu:
            np.testing.assert_allclose(
                scores_gpu[key], scores_cpu[key], atol=1e-5,
                err_msg=f"{key} differs between GPU and CPU")
            print(f"  {key}: GPU/CPU match (max diff = {np.max(np.abs(scores_gpu[key] - scores_cpu[key])):.2e})")


if __name__ == "__main__":
    print("=" * 70)
    print("RTX 3090 GPU UTILIZATION TEST SUITE")
    print("=" * 70)
    if torch.cuda.is_available():
        print(f"GPU: {torch.cuda.get_device_name(0)}")
        props = torch.cuda.get_device_properties(0)
        print(f"SMs: {props.multi_processor_count}, VRAM: {props.total_memory / 1e9:.1f} GB")
        print(f"Compute capability: {props.major}.{props.minor}")
    else:
        print("WARNING: CUDA not available — GPU tests will fail")
    print("=" * 70)

    unittest.main(verbosity=2)
