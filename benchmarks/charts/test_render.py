"""Checks for the chart renderer: python -m unittest benchmarks/charts/test_render.py (standard library only)."""
import copy
import glob
import json
import os
import re
import shutil
import sys
import tempfile
import unittest
import xml.dom.minidom

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ingest  # noqa: E402
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

    def test_sweep_cpu_arrays_and_older_runs(self):
        sweep = copy.deepcopy(self.data["sets"]["sweep"])
        def run(with_cpu):
            series = []
            for s in sweep["series"]:
                cell = dict(name=s["name"], rpcPerSec=[100, 200])
                if with_cpu:
                    cell.update(cpuSystem=[80.1, 90.2], cpuProcess=[70.0, 85.0],
                                cpuClients=[30.0, None] if "TCP" in s["name"] else [None, None])
                series.append(cell)
            return dict(connections=[1, 2], secondsPerCell=2, pipeline=256, date="d", machine="m",
                        **({"cores": 2} if with_cpu else {}), series=series)
        render.fold_sweep(sweep, [run(True), run(True)], ["a", "b"])
        self.assertEqual(sweep["cores"], 2)
        self.assertEqual(sweep["series"][0]["points"][0]["cpu"],
                         dict(system=80.1, process=70.0, clients=30.0))
        self.assertEqual(sweep["series"][0]["points"][1]["cpu"], dict(system=90.2, process=85.0))
        old = copy.deepcopy(self.data["sets"]["sweep"])
        render.fold_sweep(old, [run(False)], ["old"])
        self.assertNotIn("cores", old)
        self.assertNotIn("cpu", old["series"][0]["points"][0])

        bad = run(True)
        bad["series"][0]["cpuSystem"].pop()
        with self.assertRaises(render.DataError):
            render.fold_sweep(copy.deepcopy(self.data["sets"]["sweep"]), [bad], ["bad"])


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

    def test_headline_multiples_read_against(self):
        svg = self.outputs["headline-1x-vs-2.svg"]
        rows = self.data["sets"]["headline"]["series"][1:]
        for s in rows:
            self.assertRegex(s["against"], r"^\d+\.\d\u00d7$", s["id"])
            self.assertIn(f"{s['against']} the first row", svg, s["id"])
        self.assertEqual(svg.count("the first row"), len(rows))

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

    def test_committed_readme_blocks_and_alts_are_current(self):
        for rel, (old, new, changed) in render.readme_outputs(self.data).items():
            self.assertEqual(changed, [], rel)
            self.assertEqual(new, old, rel)


class Placeholders(unittest.TestCase):
    def test_set_and_series_values(self):
        data = dict(host="EPYC, 32 cores", sets={})
        st = dict(workers=32, cores=32, date="2026-09-27", run_count=2, x=dict(values=[1, 2, 32]))
        ctx = render.set_context(data, st)
        self.assertEqual(render.expand_text("{host}: {workers} workers, {runs_word} runs to {x_max}, {date}", ctx, {}, "t"),
                         "EPYC, 32 cores: 32 workers, two runs to 32, 2026-09-27")
        s = dict(batch=36000, bytes=1392, latency_us=[88.4, 130.2], n=3)
        sctx = render.series_context(ctx, s)
        self.assertEqual(render.expand_text("batch {batch}, {bytes} B, {us} us, {runs} runs", sctx, {}, "t"),
                         "batch 36,000, 1,392 B, 88 to 130 us, 3 runs")
        self.assertEqual(render.series_context(ctx, dict(latency_us=[10.2, 9.8]))["us"], "10")
        self.assertEqual(render.expand_text("{p:a} and {p:b}", {}, dict(a="1 M", b="2 M to 3 M"), "t"), "1 M and 2 M to 3 M")

    def test_unknown_or_unset_placeholder_fails(self):
        for text_ in ("{nope}", "{workers}", "{p:missing}", "{date:x}"):
            with self.assertRaises(render.DataError, msg=text_):
                render.expand_text(text_, dict(date="d"), {}, "t")

    def test_text_without_placeholders_is_unchanged(self):
        self.assertEqual(render.expand_text("8 to 10\u00d7 {not a placeholder}", {}, {}, "t"), "8 to 10\u00d7 {not a placeholder}")

    def test_load_expands_every_set(self):
        data = render.load()
        blob = json.dumps({k: v for k, v in data["sets"].items()}, ensure_ascii=False)
        self.assertIsNone(render.PLACEHOLDER.search(blob), "an unexpanded placeholder survived load()")
        self.assertEqual([r["value"] for r in data["summary"]][0], render.fmt_range(data["sets"]["sync"]["series"][0]["points"][-1]))


class ReadmeBlocks(unittest.TestCase):
    BLOCKS = {"one": "| A |\n| --- |\n| 1 |\n\n", "two": "| B |\n| --- |\n| 2 |\n\n"}

    def body(self, nl, one="| A |\n| --- |\n| 0 |\n\n"):
        text_ = ("intro\n<!-- benchmarks:one -->\n" + one + "<!-- /benchmarks:one -->\n\nmiddle\n"
                 '<img alt="old alt" src="benchmarks/charts/c.svg">\n'
                 "<!-- benchmarks:two -->\n" + self.BLOCKS["two"] + "<!-- /benchmarks:two -->\nend\n")
        return text_.replace("\n", nl)

    def test_rewrites_between_markers_and_keeps_crlf(self):
        new, changed = render.rewrite_readme(self.body("\r\n"), self.BLOCKS, ["one", "two"], {"c": "new & alt"}, "t")
        self.assertEqual(changed, ["block one", "alt text of c"])
        self.assertNotIn("\n", new.replace("\r\n", ""), "a bare LF crept in")
        self.assertIn("| 1 |\r\n\r\n<!-- /benchmarks:one -->", new)
        self.assertIn('<img alt="new &amp; alt" src="benchmarks/charts/c.svg">', new)
        self.assertTrue(new.startswith("intro\r\n") and new.endswith("end\r\n"))
        again, changed = render.rewrite_readme(new, self.BLOCKS, ["one", "two"], {"c": "new & alt"}, "t")
        self.assertEqual((again, changed), (new, []))

    def test_lf_file_stays_lf(self):
        new, changed = render.rewrite_readme(self.body("\n", one=self.BLOCKS["one"]), self.BLOCKS, ["one", "two"], {}, "t")
        self.assertEqual(changed, [])
        self.assertNotIn("\r", new)

    def test_bad_markers_fail(self):
        good = self.body("\n")
        cases = [
            good.replace("<!-- /benchmarks:one -->\n", ""),                       # never closed
            good.replace("<!-- benchmarks:two -->\n", ""),                        # closes no open block
            good.replace("<!-- /benchmarks:one -->", "<!-- /benchmarks:two -->"),  # mismatched close
            good.replace("<!-- benchmarks:one -->\n", "<!-- benchmarks:one --> x\n"),  # no line break after the marker
            good.replace("<!-- benchmarks:two -->", "<!-- benchmarks:one -->").replace("<!-- /benchmarks:two -->", "<!-- /benchmarks:one -->"),
        ]
        for i, body in enumerate(cases):
            with self.assertRaises(render.DataError, msg=str(i)):
                render.rewrite_readme(body, self.BLOCKS, ["one", "two"], {}, "t")
        with self.assertRaises(render.DataError):
            render.rewrite_readme(good, self.BLOCKS, ["one", "two", "three"], {}, "t")  # a required block is missing
        with self.assertRaises(render.DataError):
            render.rewrite_readme(good, self.BLOCKS, ["one"], {}, "t")  # an unknown block is present
        with self.assertRaises(render.DataError):
            render.rewrite_readme(good, self.BLOCKS, ["one", "two"], {"missing": "x"}, "t")  # no such image

    def test_table_ends_with_a_blank_line_and_writes_empty_cells(self):
        self.assertEqual(render.table(["A", "B"], ["---", "---:"], [("x", "")]), "| A | B |\n| --- | ---: |\n| x | |\n\n")


class Ingest(unittest.TestCase):
    """ingest.py on testdata/ingest (one small file per mode) against a temporary copy of the data and sweep files."""
    FIXTURES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "testdata", "ingest")

    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.tmp)
        for path in [os.path.join(render.HERE, "benchmarks.json")] + glob.glob(os.path.join(render.HERE, "sweep*.json")):
            shutil.copy(path, self.tmp)
        self.path = os.path.join(self.tmp, "benchmarks.json")
        with open(self.path, encoding="utf-8") as f:
            self.before = json.load(f)
        self.log = []
        ingest.ingest(self.FIXTURES, self.path, conditions="test conditions", source="job-1", log=self.log.append)
        with open(self.path, encoding="utf-8") as f:
            self.after = json.load(f)
        self.sets = self.after["sets"]

    def series(self, key, sid):
        return next(s for s in self.sets[key]["series"] if s["id"] == sid)

    def test_sync_points_published_and_ns(self):
        self.assertEqual(self.sets["sync"]["x"]["values"], [1, 2, 4])
        points = self.series("sync", "ours-sync")["points"]
        self.assertEqual([p["published"] for p in points], ["3 M to 3.3 M", "6 M to 6.6 M", "11 M to 12.1 M"])
        self.assertEqual([p["ns"] for p in points], [310, 305, 340])
        self.assertEqual(points[0]["values"], [3000000, 3300000])
        self.assertEqual(points[0]["cpu"], dict(system=20, process=18))

    def test_async_bytes_and_workers(self):
        s = self.series("asyncn", "asyncn-valuetask-none")
        self.assertEqual((s["published"], s["bytes"], s["n"], s["status"]), ("9.4 M", 38, 1, "single"))
        self.assertEqual(self.sets["asyncn"]["workers"], 4)
        self.assertEqual(self.series("async1", "async1-sync-none")["published"], "2 M")

    def test_headline_best_batch_and_against(self):
        base = self.series("headline", "headline-123-string")
        self.assertEqual((base["published"], base["batch"]), ("1.1 M", 100))
        string = self.series("headline", "headline-20-string")
        self.assertEqual((string["published"], string["batch"], string["against"]), ("5.2 M", 100, "4.7\u00d7"))
        self.assertEqual(self.series("headline", "headline-20-sync")["against"], "10.5\u00d7")
        self.assertEqual(self.series("headline", "headline-20-async")["against"], "8.5\u00d7")
        self.assertNotIn("against", base)

    def test_latency_and_transports(self):
        self.assertEqual(self.series("kestrel", "ours-http-1")["latency_us"], [100, 100])
        self.assertEqual(self.series("inprocess", "sjr-proxy")["latency_us"], [11, 11])
        self.assertEqual(self.series("kestrel", "ours-tcp-async-yield")["published"], "1.5 M")
        self.assertEqual(self.series("compare", "grpc-stream")["published"], "400 k")
        self.assertEqual(self.sets["compare"]["versions"], "StreamJsonRpc 2.22.11, gRPC for .NET 2.71.0")

    def test_metadata(self):
        self.assertEqual(self.after["host"], "AMD EPYC 7R13, 4 cores")
        self.assertTrue(self.after["machine"].startswith("AMD EPYC 7R13 Processor, 4 cores"))
        self.assertEqual(self.after["runtime"], ".NET 10.0.0, Release, Server GC, built-in serializer")
        for key in ("headline", "sync", "async1", "asyncn", "legacy", "kestrel", "compare", "inprocess", "sweep"):
            st = self.sets[key]
            self.assertEqual(st["conditions"], "test conditions", key)
            self.assertEqual(st["source"], "Hugging Face job job-1, commit 0123456", key)
        self.assertEqual(self.sets["sync"]["run_count"], 2)
        self.assertEqual(self.sets["wasm"], self.before["sets"]["wasm"])

    def test_hand_fields_are_kept(self):
        self.assertEqual(self.after["summary"], self.before["summary"])
        for key, st in self.before["sets"].items():
            for field in ("title", "workload", "groups", "subtitle", "alt"):
                self.assertEqual(self.sets[key].get(field), st.get(field), f"{key}.{field}")
            for old, new in zip(st["series"], self.sets[key]["series"]):
                for field in ("id", "label", "family", "note", "row", "row_note", "rpc_note"):
                    self.assertEqual(new.get(field), old.get(field), f"{old['id']}.{field}")

    def test_sweep_copied_and_stale_runs_removed(self):
        self.assertEqual(sorted(os.path.basename(p) for p in glob.glob(os.path.join(self.tmp, "sweep*.json"))), ["sweep.json"])
        with open(os.path.join(self.FIXTURES, "sweep-1.json"), encoding="utf-8") as a, open(os.path.join(self.tmp, "sweep.json"), encoding="utf-8") as b:
            self.assertEqual(json.load(a), json.load(b))
        data = render.load(self.path)
        self.assertEqual(data["sets"]["sweep"]["x"]["values"], [1, 2, 4])
        self.assertIn("kept in the results files, not published: kestrel-async-http-1", self.log[0])

    def test_sweep_fold_is_kept_in_the_data(self):
        sweep = self.sets["sweep"]
        self.assertEqual(sweep["x"]["values"], [1, 2, 4])
        self.assertEqual((sweep["cores"], sweep["run_count"], sweep["date"], sweep["runtime"]), (4, 1, "2026-09-27", ".NET 10.0.0, Release, Server GC"))
        self.assertTrue(sweep["machine"].startswith("AMD EPYC 7R13"))
        self.assertEqual(sweep["run_files"], ["sweep.json"])
        self.assertEqual(sweep["policy"], "low, high and median over 1 run; each run contributes one observation per cell")
        tcp = self.series("sweep", "sweep-ours-tcp")
        self.assertEqual([p["published"] for p in tcp["points"]], ["1 k", "1.8 k", "3 k"])
        self.assertEqual(tcp["points"][0]["cpu"], dict(system=30, process=25, clients=5))
        self.assertNotIn("clients", self.series("sweep", "sweep-grpc-unary")["points"][0]["cpu"])

    def test_persisted_sweep_matches_the_fold_from_the_files(self):
        loaded = render.load(self.path)["sets"]["sweep"]
        kept = self.sets["sweep"]
        for key in ("date", "machine", "cores", "run_files", "seconds_per_cell", "pipeline", "run_count", "x"):
            self.assertEqual(kept[key], loaded[key], key)
        for mine, theirs in zip(kept["series"], loaded["series"]):
            self.assertEqual(len(mine["points"]), len(theirs["points"]))
            for a, b in zip(mine["points"], theirs["points"]):
                self.assertEqual({k: v for k, v in a.items() if k != "published"}, b, mine["id"])
                self.assertEqual(a["published"], render.fmt_range(b))

    def test_result_renders_and_second_run_is_identical(self):
        data = render.load(self.path)
        render.render_all(data)
        render.readme_blocks(data)
        with open(self.path, "rb") as f:
            first = f.read()
        self.assertFalse(ingest.ingest(self.FIXTURES, self.path, conditions="test conditions", source="job-1", log=self.log.append))
        with open(self.path, "rb") as f:
            self.assertEqual(f.read(), first)

    def test_unknown_row_id_fails(self):
        results = os.path.join(self.tmp, "results")
        os.mkdir(results)
        with open(os.path.join(self.FIXTURES, "compare-1.json"), encoding="utf-8") as f:
            doc = json.load(f)
        doc["rows"][0]["id"] = "not-a-row"
        with open(os.path.join(results, "compare-1.json"), "w", encoding="utf-8") as f:
            json.dump(doc, f)
        with self.assertRaises(ingest.IngestError):
            ingest.ingest(results, self.path, log=self.log.append)

    def test_disagreeing_files_fail(self):
        results = os.path.join(self.tmp, "results")
        shutil.copytree(self.FIXTURES, results)
        with open(os.path.join(results, "sync-2.json"), encoding="utf-8") as f:
            doc = json.load(f)
        doc["cores"] = 8
        with open(os.path.join(results, "sync-2.json"), "w", encoding="utf-8") as f:
            json.dump(doc, f)
        with self.assertRaises(ingest.IngestError):
            ingest.ingest(results, self.path, log=self.log.append)

    def test_formatter_matches_the_committed_layout(self):
        with open(os.path.join(render.HERE, "benchmarks.json"), encoding="utf-8", newline="") as f:
            committed = f.read().replace("\r\n", "\n")
        self.assertEqual(ingest.dumps(json.loads(committed)) + "\n", committed)


if __name__ == "__main__":
    unittest.main()
