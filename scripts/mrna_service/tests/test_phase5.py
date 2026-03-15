"""
Phase 5 mRNA rescoring pipeline tests.

Uses unittest to test configuration, folding backend, motif rules,
GC/codon metrics, Phase 5 scorer, report generation, and integration.
"""

import random
import sys
import os
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from phase5_config import Phase5Config, get_preset, list_presets, get_default_config
from folding_backend import NussinovBackend, FoldingResult
from motif_rules import MotifRuleEngine, MotifRule, compute_aggregate_scores
from phase5_metrics import (
    score_gc_windows,
    score_gc_variance,
    score_codon_runs,
    score_codon_autocorrelation,
    compute_all_phase5_metrics,
    score_5prime_accessibility,
    score_global_folding,
)
from phase5_scoring import Phase5Scorer, ScoredCandidate
from phase5_report import generate_report, generate_text_summary


# ---------------------------------------------------------------------------
# TestPhase5Config
# ---------------------------------------------------------------------------


class TestPhase5Config(unittest.TestCase):
    def test_default_config(self):
        cfg = get_default_config()
        self.assertEqual(cfg.top_n, 50)
        self.assertGreaterEqual(cfg.gc_window_30_low, 0.0)
        self.assertLessEqual(cfg.gc_window_30_high, 1.0)
        self.assertGreater(cfg.gc_window_30_high, cfg.gc_window_30_low)
        self.assertGreater(cfg.max_homopolymer_run, 0)
        self.assertGreater(cfg.diversity_threshold, 0.0)
        self.assertGreater(cfg.saturation_threshold, 0.0)

    def test_list_presets(self):
        presets = list_presets()
        self.assertEqual(len(presets), 5)
        expected = {
            "phase5_balanced",
            "translation_heavy",
            "immune_stealth_heavy",
            "manufacturability_heavy",
            "structure_heavy",
        }
        self.assertEqual(set(presets), expected)

    def test_get_preset(self):
        preset = get_preset("phase5_balanced")
        self.assertIsInstance(preset, dict)
        self.assertIn("structure_global", preset)
        self.assertIn("gc_window_30", preset)
        self.assertIn("legacy_cai", preset)
        self.assertIn("homopolymer", preset)
        self.assertIn("codon_diversity", preset)
        for k, v in preset.items():
            self.assertIsInstance(v, (int, float))


# ---------------------------------------------------------------------------
# TestFoldingBackend
# ---------------------------------------------------------------------------


class TestFoldingBackend(unittest.TestCase):
    def setUp(self):
        self.backend = NussinovBackend()

    def test_nussinov_simple(self):
        result = self.backend.fold("GGGAAACCC")
        self.assertIsInstance(result, FoldingResult)
        self.assertEqual(len(result.structure), 9)
        self.assertEqual(result.structure.count("("), result.structure.count(")"))
        self.assertLess(result.mfe, 0)

    def test_nussinov_fold_region(self):
        seq = "GGGAAACCC" * 5
        result = self.backend.fold_region(seq, 0, 27)
        self.assertIsInstance(result, FoldingResult)
        self.assertEqual(len(result.structure), 27)

    def test_nussinov_is_approximate(self):
        self.assertTrue(self.backend.is_approximate)

    def test_nussinov_long_sequence(self):
        random.seed(42)
        bases = "AUGC"
        seq = "".join(random.choice(bases) for _ in range(600))
        result = self.backend.fold(seq)
        self.assertIsInstance(result, FoldingResult)
        self.assertEqual(len(result.structure), 600)


# ---------------------------------------------------------------------------
# TestMotifRuleEngine
# ---------------------------------------------------------------------------


class TestMotifRuleEngine(unittest.TestCase):
    def test_homopolymer_detection(self):
        engine = MotifRuleEngine()
        result = engine.score_homopolymers("ACGUAAAAAACGU", max_run=5)
        self.assertLess(result.score, 1.0)
        self.assertGreater(result.hit_count, 0)

    def test_no_homopolymer(self):
        engine = MotifRuleEngine()
        result = engine.score_homopolymers("ACGU", max_run=5)
        self.assertEqual(result.score, 1.0)
        self.assertEqual(result.hit_count, 0)

    def test_dinucleotide_repeat(self):
        engine = MotifRuleEngine()
        result = engine.score_dinucleotide_repeats("AUAUAUAUAU")
        self.assertLess(result.score, 1.0)
        self.assertGreater(result.hit_count, 0)

    def test_au_rich_elements(self):
        engine = MotifRuleEngine()
        result = engine.score_au_rich_elements("ACGAUUUAUGC")
        self.assertLess(result.score, 1.0)
        self.assertGreater(result.hit_count, 0)

    def test_forbidden_motifs(self):
        engine = MotifRuleEngine(forbidden_motifs=["XXXYYY"])
        results = engine.evaluate("ACGUXXXYYYACGU")
        forbidden_results = [r for r in results if "forbidden" in r.rule_name.lower()]
        self.assertGreater(len(forbidden_results), 0)
        self.assertLess(forbidden_results[0].score, 1.0)

    def test_aggregate_scores(self):
        engine = MotifRuleEngine()
        results = engine.evaluate("ACGUACGUACGU")
        agg = compute_aggregate_scores(results)
        self.assertIn("homopolymer_score", agg)
        self.assertIn("motif_risk_score", agg)
        self.assertIn("forbidden_motif_score", agg)
        self.assertIn("local_sequence_quality_score", agg)
        for k, v in agg.items():
            self.assertGreaterEqual(v, 0.0)
            self.assertLessEqual(v, 1.0)


# ---------------------------------------------------------------------------
# TestGCWindowScoring
# ---------------------------------------------------------------------------


class TestGCWindowScoring(unittest.TestCase):
    def test_gc_in_band(self):
        seq = "ACGU" * 20
        self.assertEqual(len(seq), 80)
        gc_frac = (seq.count("G") + seq.count("C")) / len(seq)
        self.assertAlmostEqual(gc_frac, 0.5, places=1)
        score = score_gc_windows(seq, 30, 0.40, 0.65)
        self.assertGreater(score, 0.9)

    def test_gc_out_of_band(self):
        seq = "GGGGGGGGGG" * 10 + "CCCCCCCCCC" * 10
        score = score_gc_windows(seq, 30, 0.40, 0.65)
        self.assertLess(score, 0.5)

    def test_gc_variance_low(self):
        seq = "ACGU" * 50
        score = score_gc_variance(seq)
        self.assertGreater(score, 0.8)

    def test_gc_variance_high(self):
        seq = "GGGGGGGGGG" * 10 + "AAAAAAAAAA" * 10 + "CCCCCCCCCC" * 10
        score = score_gc_variance(seq)
        self.assertLess(score, 0.5)


# ---------------------------------------------------------------------------
# TestCodonDiversity
# ---------------------------------------------------------------------------


class TestCodonDiversity(unittest.TestCase):
    def test_codon_runs_no_repeat(self):
        indices = [0, 1, 2, 0, 1, 2, 0, 1, 2]
        score = score_codon_runs(indices)
        self.assertEqual(score, 1.0)

    def test_codon_runs_with_repeat(self):
        indices = [0, 0, 0, 0, 1, 2]
        score = score_codon_runs(indices)
        self.assertLess(score, 1.0)

    def test_codon_autocorrelation_random(self):
        random.seed(123)
        indices = [random.randint(0, 5) for _ in range(100)]
        score = score_codon_autocorrelation(indices)
        self.assertGreater(score, 0.5)

    def test_codon_autocorrelation_periodic(self):
        indices = [0, 1, 2, 0, 1, 2, 0, 1, 2] * 10
        score = score_codon_autocorrelation(indices)
        self.assertLess(score, 1.0)


# ---------------------------------------------------------------------------
# TestPhase5Scorer
# ---------------------------------------------------------------------------


def _make_candidate(seq, cai=0.5, gc=0.5, cpg=0.5, uridine=0.5, rare=0.5, repeat=0.5, pair=0.5, idx=None):
    comp = (cai + gc + cpg + uridine + rare + repeat + pair) / 7.0
    d = {
        "coding_sequence": seq,
        "cai": cai,
        "gc_score": gc,
        "cpg_score": cpg,
        "uridine_score": uridine,
        "rare_codon_score": rare,
        "repeat_score": repeat,
        "codon_pair_score": pair,
        "composite": comp,
    }
    if idx is not None:
        d["id"] = f"candidate_{idx}"
    return d


class TestPhase5Scorer(unittest.TestCase):
    def setUp(self):
        self.backend = NussinovBackend()

    def test_rescore_basic(self):
        base = "AUG" + "GCC" * 30 + "UGA"
        candidates = [
            _make_candidate(base, cai=0.9, gc=0.9, idx=0),
            _make_candidate(base, cai=0.5, gc=0.5, idx=1),
            _make_candidate(base, cai=0.3, gc=0.3, idx=2),
        ]
        scorer = Phase5Scorer(backend=self.backend)
        result = scorer.rescore_candidates(candidates)
        self.assertEqual(len(result.candidates_all), 3)
        sorted_by_phase5 = result.candidates_all
        self.assertGreaterEqual(
            sorted_by_phase5[0].composite_phase5,
            sorted_by_phase5[-1].composite_phase5,
        )

    def test_saturation_dampening(self):
        base = "AUG" + "GCC" * 30 + "UGA"
        candidates = [
            _make_candidate(
                base,
                cai=0.99,
                gc=0.99,
                cpg=0.99,
                uridine=0.99,
                rare=0.99,
                repeat=0.99,
                pair=0.99,
                idx=0,
            ),
        ]
        scorer = Phase5Scorer(backend=self.backend)
        result = scorer.rescore_candidates(candidates)
        self.assertEqual(len(result.candidates_all), 1)
        self.assertLess(result.candidates_all[0].composite_phase5, 1.0)

    def test_diversity_filter(self):
        base = "AUG" + "GCC" * 30 + "UGA"
        c1 = _make_candidate(base, idx=0)
        c2 = _make_candidate(base, idx=1)
        c3 = _make_candidate(base, idx=2)
        diff = "AUG" + "GCA" * 30 + "UGA"
        c4 = _make_candidate(diff, idx=3)
        c5 = _make_candidate("AUG" + "GCC" * 15 + "GCA" * 15 + "UGA", idx=4)
        candidates = [c1, c2, c3, c4, c5]
        config = Phase5Config(diversity_top_k=3, diversity_threshold=0.05)
        scorer = Phase5Scorer(config=config, backend=self.backend)
        result = scorer.rescore_candidates(candidates)
        diverse = result.candidates_diverse_topk
        self.assertLessEqual(len(diverse), 3)
        seqs = [c.coding_sequence for c in diverse]
        self.assertEqual(len(seqs), len(set(seqs)))

    def test_rank_changes(self):
        good_legacy = "AUG" + "GCC" * 30 + "UGA"
        bad_legacy = "AUG" + "GCC" * 30 + "UGA"
        candidates = [
            _make_candidate(good_legacy, cai=0.95, gc=0.95, idx="A"),
            _make_candidate(bad_legacy, cai=0.4, gc=0.4, idx="B"),
        ]
        scorer = Phase5Scorer(backend=self.backend)
        result = scorer.rescore_candidates(candidates)
        for c in result.candidates_all:
            self.assertIsNotNone(c.rank_change)


# ---------------------------------------------------------------------------
# TestPhase5Report
# ---------------------------------------------------------------------------


class TestPhase5Report(unittest.TestCase):
    def setUp(self):
        self.backend = NussinovBackend()

    def test_generate_report_structure(self):
        base = "AUG" + "GCC" * 30 + "UGA"
        candidates = [_make_candidate(base, idx=0)]
        scorer = Phase5Scorer(backend=self.backend)
        result = scorer.rescore_candidates(candidates)
        report = generate_report(result)
        self.assertIn("timestamp", report)
        self.assertIn("config", report)
        self.assertIn("summary", report)
        self.assertIn("candidates_all", report)
        self.assertIn("candidates_diverse_topk", report)
        self.assertIn("rank_movement", report)

    def test_generate_text_summary(self):
        base = "AUG" + "GCC" * 30 + "UGA"
        candidates = [_make_candidate(base, idx=0)]
        scorer = Phase5Scorer(backend=self.backend)
        result = scorer.rescore_candidates(candidates)
        text = generate_text_summary(result)
        self.assertIsInstance(text, str)
        self.assertGreater(len(text), 0)
        self.assertIn("Phase 5", text)
        self.assertIn("Candidates rescored", text)
        self.assertIn("Rank movement", text)


# ---------------------------------------------------------------------------
# TestIntegration
# ---------------------------------------------------------------------------


class TestIntegration(unittest.TestCase):
    def setUp(self):
        self.backend = NussinovBackend()

    def test_end_to_end(self):
        ref_len = 126
        n_codons = ref_len // 3
        base_seq = "AUG" + "GCC" * (n_codons - 2) + "UGA"

        seq_a = "AUG" + "GGGGGG" * 10 + "CCCCCC" * 10 + "UGA"
        seq_a = seq_a[:ref_len]
        if len(seq_a) % 3 != 0:
            seq_a = seq_a[: (len(seq_a) // 3) * 3]

        seq_b = "AUG" + "ACGU" * 30 + "UGA"
        seq_b = seq_b[: (len(seq_b) // 3) * 3]
        seq_b = seq_b[: len(seq_a)]

        candidates = []
        for i in range(10):
            if i == 0:
                c = _make_candidate(
                    seq_a,
                    cai=0.9,
                    gc=0.9,
                    cpg=0.9,
                    uridine=0.9,
                    rare=0.9,
                    repeat=0.9,
                    pair=0.9,
                    idx="A",
                )
            elif i == 1:
                c = _make_candidate(
                    seq_b,
                    cai=0.8,
                    gc=0.8,
                    cpg=0.8,
                    uridine=0.8,
                    rare=0.8,
                    repeat=0.8,
                    pair=0.8,
                    idx="B",
                )
            else:
                s = base_seq[: len(seq_a)]
                c = _make_candidate(
                    s,
                    cai=0.9 - i * 0.05,
                    gc=0.9 - i * 0.05,
                    cpg=0.8,
                    uridine=0.8,
                    rare=0.8,
                    repeat=0.8,
                    pair=0.8,
                    idx=i,
                )
            candidates.append(c)

        scorer = Phase5Scorer(backend=self.backend)
        result = scorer.rescore_candidates(candidates)

        cand_a = next(c for c in result.candidates_all if c.id == "candidate_A")
        cand_b = next(c for c in result.candidates_all if c.id == "candidate_B")

        self.assertGreater(cand_b.rank_phase5, 0)
        self.assertGreater(cand_a.rank_phase5, 0)

        phase5_ranks = {c.id: c.rank_phase5 for c in result.candidates_all}
        rank_b = phase5_ranks["candidate_B"]
        rank_a = phase5_ranks["candidate_A"]
        self.assertLess(
            rank_b,
            rank_a,
            "B (good local) should rank higher than A (poor local) in Phase 5",
        )

        self.assertGreater(
            cand_b.rank_change,
            0,
            "B should have positive rank_change (moved up)",
        )

        report = generate_report(result)
        self.assertIn("summary", report)
        self.assertIn("candidates_all", report)


if __name__ == "__main__":
    unittest.main()
