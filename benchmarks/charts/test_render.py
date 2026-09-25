"""Checks for the chart renderer: python -m unittest benchmarks/charts/test_render.py (standard library only)."""
import copy
import json
import os
import re
import sys
import unittest
import xml.dom.minidom

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render  # noqa: E402


class Formatting(unittest.TestCase):
    def test_published_text_is_preserved(self):
        self.assertEqual(render.fmt_range(dict(low=13.7e6, high=14.6e6, published="13.7 M to 14.6 M")), "13.7 M to 14.6 M")
        self.assertEqual(render.fmt_range(dict(low=30.6e6, high=35.8e6, published="30.6 M to 35.8 M")), "30.6 M to 35.8 M")

    def test_three_significant_figures_without_published_text(self):
        self.assertEqual(render.fmt(1.38e6), "1.38 M")
        self.assertEqual(render.fmt(13.7e6), "13.7 M")
        self.assertEqual(render.fmt(625e3), "625 k")
        self.assertEqual(render.fmt(96.4e3), "96.4 k")
        self.assertEqual(render.fmt_range(dict(low=1.28e6, high=1.30e6)), "1.28 M to 1.3 M")
        self.assertEqual(render.fmt_range(dict(low=5e5, high=5e5)), "500 k")

    def test_axis_ticks(self):
        self.assertEqual(render.fmt_axis(10e6), "10 M")
        self.assertEqual(render.fmt_axis(200e3), "200 k")


class Scales(unittest.TestCase):
    def test_log_domain_hugs_the_data(self):
        s = render.Log(96e3, 14.6e6, 0, 1)
        self.assertEqual((s.lo, s.hi), (50e3, 20e6))
        self.assertAlmostEqual(s(50e3), 0)
        self.assertAlmostEqual(s(20e6), 1)

    def test_log2_positions(self):
        self.assertEqual(render.log2_positions([1, 2, 4, 8, 16], 0, 4), [0, 1, 2, 3, 4])
        self.assertEqual(render.log2_positions([4], 0, 4), [2])

    def test_rejects_bad_domains(self):
        with self.assertRaises(render.DataError):
            render.Log(0, 10, 0, 1)
        with self.assertRaises(render.DataError):
            render.Linear(5, 5, 0, 1)


class Validation(unittest.TestCase):
    def setUp(self):
        self.data = render.load()

    def bad(self, mutate):
        d = copy.deepcopy(self.data)
        mutate(d)
        with self.assertRaises(render.DataError):
            render.validate(d)

    def test_reversed_range(self):
        self.bad(lambda d: d["sets"]["compare"]["series"][0].update(low=2e6, high=1e6))

    def test_non_positive(self):
        self.bad(lambda d: d["sets"]["kestrel"]["series"][0].update(low=0))

    def test_unknown_family(self):
        self.bad(lambda d: d["sets"]["kestrel"]["series"][0].update(family="nope"))

    def test_duplicate_id(self):
        self.bad(lambda d: d["sets"]["kestrel"]["series"][1].update(id=d["sets"]["kestrel"]["series"][0]["id"]))

    def test_length_mismatch(self):
        self.bad(lambda d: d["sets"]["sync"]["series"][0]["points"].pop())

    def test_group_names_unknown_series(self):
        self.bad(lambda d: d["sets"]["compare"]["groups"][0]["series"].append("missing"))

    def test_sweep_run_mismatch(self):
        sweep = copy.deepcopy(self.data["sets"]["sweep"])
        runs = [dict(connections=[1, 2], secondsPerCell=2, pipeline=256, date="d", machine="m",
                     series=[dict(name=s["name"], rpcPerSec=[1, 2]) for s in sweep["series"]])]
        runs[0]["series"][0]["rpcPerSec"] = [1]
        with self.assertRaises(render.DataError):
            render.fold_sweep(sweep, runs, ["a"])


class Outputs(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = render.load()
        cls.outputs = render.render_all(cls.data)

    def test_every_svg_is_well_formed_and_carries_the_revision(self):
        for name, content in self.outputs.items():
            if name.endswith(".svg"):
                xml.dom.minidom.parseString(content)
                self.assertIn(f'data-revision="{self.data["revision"]}"', content, name)
                self.assertNotIn("&amp;amp;", content, name)

    def test_published_endpoints_survive_into_the_svgs(self):
        for key in ("kestrel", "compare", "inprocess"):
            for s in self.data["sets"][key]["series"]:
                self.assertIn(render.esc(s["published"]), self.outputs["compare-streamjsonrpc.svg"] + self.outputs["kestrel-transports.svg"] + self.outputs["inprocess-paths.svg"], s["id"])
        for p in self.data["sets"]["sync"]["series"][0]["points"]:
            self.assertIn(p["published"], self.outputs["sync-threads.svg"])
        for s in self.data["sets"]["headline"]["series"]:
            self.assertIn(render.esc(s["published"]), self.outputs["headline-1x-vs-2.svg"], s["id"])

    def test_headline_multiples_are_against_the_first_row(self):
        svg = self.outputs["headline-1x-vs-2.svg"]
        base = self.data["sets"]["headline"]["series"][0]["high"]
        for s in self.data["sets"]["headline"]["series"][1:]:
            self.assertIn(f"{s['high'] / base:.1f}\u00d7 the first row", svg, s["id"])
        self.assertEqual(svg.count("the first row"), len(self.data["sets"]["headline"]["series"]) - 1)

    def test_clip_ids_are_unique_across_themes(self):
        ids = re.findall(r'clipPath id="([^"]+)"', self.outputs["sync-threads.svg"] + self.outputs["sync-threads-dark.svg"])
        self.assertEqual(len(ids), len(set(ids)))

    def test_explorer_embeds_the_same_summaries(self):
        page = self.outputs["explorer.html"]
        self.assertNotIn("</script>", page.split('<script id="data"')[1].split("</script>")[0][10:], "embedded JSON must not close the script")
        m = re.search(r'<script id="data" type="application/json">(.*?)</script>', page, re.S)
        embedded = json.loads(m.group(1))
        self.assertEqual(embedded["revision"], self.data["revision"])
        self.assertEqual(embedded["sets"]["sweep"]["series"][0]["points"], self.data["sets"]["sweep"]["series"][0]["points"])
        self.assertEqual(embedded["sets"]["compare"]["series"][0]["published"], self.data["sets"]["compare"]["series"][0]["published"])

    def test_rendering_is_deterministic(self):
        self.assertEqual(render.render_all(render.load()), self.outputs)

    def test_readme_figures_match(self):
        self.assertEqual(render.check_readme(self.data), [])


if __name__ == "__main__":
    unittest.main()
