#!/usr/bin/env python3
"""Checks summarise.py on the sample files next to it. Run: python3 tools/perf/test_summarise.py"""
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import summarise  # noqa: E402


def sample(name):
    return os.path.join(HERE, "sample", name)


class SummariseTests(unittest.TestCase):
    def test_tour_file_gives_the_metrics_table(self):
        text, failed = summarise.summarise([sample("tour_sample.csv")])
        self.assertIn("| Metric | Must | S0 Home | S1 Glued 5x5 | S1 Glued 3x3x3 | S5 Impossible Rooms | S6 Slicer |", text)
        self.assertIn("| Draw calls, both eyes | ≤ 300 | 30 | 126 | 134 | 58 | 34 |", text)
        self.assertIn("16.68 / **20.40** / 25.90", text)
        self.assertIn("**FAIL** (p95, over17_2, gpu)", text)
        self.assertIn("- S2 Curved Walk is not in this build: not measured", text)
        self.assertTrue(failed)
        self.assertNotIn("the phone wrote", text, "the phone's verdicts agree with the numbers")

    def test_events_from_the_hub_give_the_same_table(self):
        from_file, _ = summarise.summarise([sample("tour_sample.csv")])
        from_events, failed = summarise.summarise([sample("events_sample.jsonl")])
        table = lambda text: [l for l in text.splitlines() if l.startswith("| ")]
        self.assertEqual(table(from_file), table(from_events))
        self.assertTrue(failed)

    def test_a_run_given_twice_counts_once(self):
        text, _ = summarise.summarise([sample("tour_sample.csv"), sample("events_sample.jsonl"), sample("perf_sample.csv")])
        self.assertEqual(text.count("## Per-space run"), 1)

    def test_soaks_give_the_timeline_and_the_depth_difference(self):
        text, failed = summarise.summarise([sample("tour_soak_sample.csv"), sample("tour_soak_nodepth_sample.csv")])
        self.assertFalse(failed)
        self.assertIn("## Soak, LiDAR depth on", text)
        self.assertIn("## Soak, LiDAR depth off", text)
        self.assertIn("| Time-to-Serious | not before 600 s | 731 s (12 min 11 s) |", text)
        self.assertIn("Thermal changes: Fair at 312 s, Serious at 731 s.", text)
        self.assertIn("S2 Curved Walk is not in this build", text)
        self.assertIn("| Time-to-Fair | 312 s (5 min 12 s) | 406 s (6 min 45 s) | +93 s (off minus on) |", text)

    def test_ladder_rows(self):
        text, failed = summarise.summarise([sample("tour_ladder_sample.csv")])
        self.assertFalse(failed)
        self.assertIn("| 3 Critical | ≤ 1000 ms | 16.6 | pass |", text)

    def test_probe_rows_alone_still_give_a_table(self):
        text, failed = summarise.summarise([sample("perf_sample.csv")])
        self.assertIn("| Metric | Must | S0 Home | S1 Glued 25 copies |", text)
        self.assertIn("| Draw calls, both eyes | ≤ 300 | 30 | 126 |", text)
        self.assertIn("made from the probe's rows", text)
        self.assertFalse(failed)

    def test_rules(self):
        good = summarise.typed({"frames": 1800, "p50": 16.67, "p95": 17, "p99": 18, "drawCalls": 100, "setPass": 10,
                                "tris": 1000, "cpuMainP95": 4, "cpuRenderP95": 3, "gpuP95": 5, "memMB": 600})
        self.assertEqual(summarise.judge_space(good, True), ([], []))
        hot = dict(good, gpuP95=12.0, thermalMax=1.0)
        self.assertEqual(summarise.judge_space(hot, True)[0], ["gpu", "notCold"])
        self.assertEqual(summarise.judge_space(hot, False)[0], [])
        editor = dict(good, cpuMainP95=0.0, cpuRenderP95=0.0, gpuP95=0.0)
        self.assertEqual(summarise.judge_space(editor, True), ([], ["cpuMain", "cpuRender", "gpu"]))
        on_limit = dict(good, p50=16.7, p95=20.0, p99=33.3, drawCalls=300.0, memMB=1536.0, gcAllocB=1024.0)
        self.assertEqual(summarise.judge_space(on_limit, True)[0], [])
        soak = dict(good, fpsMean=59.0, fpsMin=40.0, tSeriousS=500.0, tCriticalS=-1.0)
        self.assertEqual(summarise.judge_soak(soak, 900)[0], ["seriousEarly"])
        self.assertEqual(summarise.judge_soak(dict(soak, tSeriousS=-1.0), 120), ([], ["short"]))
        self.assertEqual(summarise.judge_tier({"effectMs": 1200.0})[0], ["slow"])

    def test_empty_table_has_every_space_of_the_plan(self):
        table = summarise.empty_table()
        self.assertIn("| S0 Home | S1 Glued 5x5 | S1 Glued 3x3x3 | S2 Curved Walk | S3 Aligned Cells | S4 Small Planet | "
                      "S5 Impossible Rooms | S6 Slicer |", table)
        self.assertEqual(len(table.strip().splitlines()), 14)

    def test_strict_exit_code_and_a_bad_file(self):
        self.assertEqual(summarise.main([sample("tour_ladder_sample.csv"), "--strict", "--out", os.devnull]), 0)
        self.assertEqual(summarise.main([sample("tour_sample.csv"), "--strict", "--out", os.devnull]), 1)
        self.assertEqual(summarise.main([os.path.abspath(__file__)]), 2)


if __name__ == "__main__":
    unittest.main()
