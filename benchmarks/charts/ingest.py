"""Folds a benchmark results directory into benchmarks.json and the sweep files.

    python benchmarks/charts/ingest.py <results dir> [--source <job id>] [--conditions "..."]

The results directory is what `benchmarks/hf/fetch.py` writes from a `run.sh <ref> publish` job (or what a local run
with JSONRPC_BENCH_RESULTS=<dir> leaves): one JSON file per mode and run (sync-1.json, async-w32-2.json, ...,
baseline-1.json, sweep-1.json ...). Every row carries an id; ROW_TARGETS below says which benchmarks.json series each
id feeds, and an id missing from the table stops the ingest. For every set but `wasm` this rewrites the measured
fields (x values; low, high, median, values, n, status, published, ns, cpu, bytes, latency_us, batch, against; the
set's date, source, machine, runtime, harness, policy, workers, cores, run_count and, when given, conditions), the
top-level machine, runtime and host. It copies the sweep files over sweep.json and sweep-2..k.json and keeps their fold
in the sweep set (x values; per cell low, high, median, values, CPU and published text; date, machine, cores, runtime).
Everything else in benchmarks.json (ids, labels, notes, rows, groups, titles, workload, subtitles, alt texts, families,
styles, the summary rows) is hand-maintained and left alone. The headline set is derived: 1.2.3 and the 2.0 string API at their best batch
size by median, the byte entry points at N threads and N workers, with the `Against 1.2.3` ratios of the medians.
Running it twice on the same directory changes nothing. Then run render.py. Plain Python, standard library only.
"""
import argparse
import glob
import json
import os
import re
import shutil
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REGISTRATIONS = ["sync-none", "task-flow", "task-none", "valuetask-flow", "valuetask-none", "yield-flow", "yield-none"]

# Every row id a results file may carry, and the series it feeds as "set/series id"; "headline" for the 1.2.3 rows,
# which feed the headline set through their best batch size; None for a row that is kept in the results file and
# published nowhere (ingest lists those).
ROW_TARGETS = {
    "ours-sync": "sync/ours-sync",
    **{f"async1-{r}": f"async1/async1-{r}" for r in REGISTRATIONS},
    **{f"asyncn-{r}": f"asyncn/asyncn-{r}" for r in REGISTRATIONS},
    "legacy-string": "legacy/legacy-string",
    "headline-123-string": "headline",
    "kestrel-inproc-1": None,
    "ours-inproc-n": "kestrel/ours-inproc-n",
    "ours-http-1": "kestrel/ours-http-1",
    "ours-http-100": "kestrel/ours-http-100",
    "ours-tcp": "kestrel/ours-tcp",
    "kestrel-async-inproc-1": None,
    "kestrel-async-inproc-n": None,
    "kestrel-async-http-1": None,
    "kestrel-async-http-100": None,
    "ours-tcp-async-inline": "kestrel/ours-tcp-async-inline",
    "ours-tcp-async-yield": "kestrel/ours-tcp-async-yield",
    "ours-tcp-jsonrpc": "compare/ours-tcp-jsonrpc",
    "sjr-newline-stj": "compare/sjr-newline-stj",
    "sjr-cl-stj": "compare/sjr-cl-stj",
    "sjr-cl-newtonsoft": "compare/sjr-cl-newtonsoft",
    "grpc-unary": "compare/grpc-unary",
    "grpc-stream": "compare/grpc-stream",
    "ours-direct-1": "inprocess/ours-direct-1",
    "sjr-pipe": "inprocess/sjr-pipe",
    "sjr-proxy": "inprocess/sjr-proxy",
    "scale-sync-none": None,
    "scale-task-none": None,
    "scale-valuetask-none": None,
    **{f"scale-diag-{s}-{r}-none": None for s in ("jsmn", "stj", "newtonsoft") for r in ("sync", "task", "valuetask", "yield")},
    **{sid: f"sweep/{sid}" for sid in ("sweep-ours-tcp", "sweep-ours-http-100", "sweep-ours-http-1", "sweep-sjr-newline-stj",
                                       "sweep-sjr-cl-stj", "sweep-sjr-cl-newtonsoft", "sweep-grpc-unary", "sweep-grpc-stream")},
}
MODES = {"sync", "async", "legacy", "kestrel", "kestrel-async", "compare", "scale", "sweep", "baseline"}
FILE = re.compile(r"^(?P<stem>[a-z0-9-]+?)-(?P<run>[1-9][0-9]*)\.json$")


class IngestError(ValueError):
    pass


# ---------------------------------------------------------------- output format

def _scalar(v):
    return json.dumps(v, ensure_ascii=False)


def _inline(v):
    """Scalars, lists of scalars and objects whose values are those (or objects of scalars) go on one line."""
    if isinstance(v, list):
        return all(not isinstance(x, (list, dict)) for x in v)
    if isinstance(v, dict):
        return all(not isinstance(x, (list, dict)) or (_inline(x) and not (isinstance(x, dict) and any(isinstance(y, (list, dict)) for y in x.values())))
                   for x in v.values())
    return True


def _one_line(v):
    if isinstance(v, dict):
        return "{ " + ", ".join(f"{_scalar(k)}: {_one_line(x)}" for k, x in v.items()) + " }" if v else "{}"
    if isinstance(v, list):
        return "[" + ", ".join(_one_line(x) for x in v) + "]"
    return _scalar(v)


def dumps(v, indent=0):
    """benchmarks.json's layout: two-space indent, and every object or list that holds no list of objects on one line."""
    if _inline(v):
        return _one_line(v)
    pad = " " * (indent + 2)
    if isinstance(v, dict):
        body = ",\n".join(f"{pad}{_scalar(k)}: {dumps(x, indent + 2)}" for k, x in v.items())
        return "{\n" + body + "\n" + " " * indent + "}"
    body = ",\n".join(pad + dumps(x, indent + 2) for x in v)
    return "[\n" + body + "\n" + " " * indent + "]"


# ---------------------------------------------------------------- reading

def read_results(directory):
    """{(mode, run): document} for every results file; sweep files keyed ("sweep", run)."""
    docs = {}
    for path in sorted(glob.glob(os.path.join(directory, "*.json"))):
        name = os.path.basename(path)
        m = FILE.match(name)
        if not m:
            raise IngestError(f"{name}: not a results file name (<stem>-<run>.json)")
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
        mode = doc.get("mode", "sweep" if m.group("stem") == "sweep" else None)
        if mode not in MODES:
            raise IngestError(f"{name}: unknown mode {mode!r}")
        doc["_file"] = name
        doc["_path"] = path
        docs[(m.group("stem"), int(m.group("run")))] = doc
    if not docs:
        raise IngestError(f"no results files in {directory}")
    return docs


def row_ids(doc):
    if doc.get("mode", "sweep") == "sweep":
        return [s.get("id") or sweep_id(s["name"]) for s in doc["series"]]
    return [r["id"] for r in doc.get("rows", [])] + [r["id"] for r in doc.get("diagnostics", [])]


def sweep_id(name):
    for sid, target in ROW_TARGETS.items():
        if target and target.startswith("sweep/") and SWEEP_NAMES.get(sid) == name:
            return sid
    raise IngestError(f"sweep series {name!r} has no id")


SWEEP_NAMES = {"sweep-ours-tcp": "JSON-RPC.Net, TCP", "sweep-ours-http-100": "JSON-RPC.Net, HTTP, batch of 100 per POST",
               "sweep-ours-http-1": "JSON-RPC.Net, HTTP, 1 request per POST",
               "sweep-sjr-newline-stj": "StreamJsonRpc, TCP, newline + System.Text.Json",
               "sweep-sjr-cl-stj": "StreamJsonRpc, TCP, Content-Length + System.Text.Json",
               "sweep-sjr-cl-newtonsoft": "StreamJsonRpc, TCP, Content-Length + Json.NET",
               "sweep-grpc-unary": "gRPC for .NET, unary", "sweep-grpc-stream": "gRPC for .NET, bidirectional stream"}


def one(values, what):
    values = sorted({v for v in values if v is not None})
    if len(values) > 1:
        raise IngestError(f"the results disagree on {what}: {values}")
    return values[0] if values else None


# ---------------------------------------------------------------- summaries

def published(lo, hi):
    a, b = render.fmt(lo), render.fmt(hi)
    return a if a == b else f"{a} to {b}"


def num(v):
    """Whole numbers as ints, so the file does not grow ".0" tails."""
    return int(v) if float(v).is_integer() else v


def summary(rows):
    """low, high, median, every value (in run order), n, status, published and the median CPU of the rows."""
    values = [num(r["rpcPerSec"]) for r in rows]
    lo, hi = min(values), max(values)
    out = dict(low=lo, high=hi, median=num(statistics.median(values)), values=values, n=len(values),
               status="range" if lo != hi else "single", published=published(lo, hi))
    cpu = {}
    for key in ("system", "process", "clients"):
        measured = [r["cpu"][key] for r in rows if key in r.get("cpu", {})]
        if measured:
            cpu[key] = num(round(statistics.median(measured), 1))
    if cpu:
        out["cpu"] = cpu
    return out


MEASURED = ("low", "high", "median", "values", "n", "status", "published", "cpu", "ns", "bytes", "latency_us", "batch", "against")


def apply(target, measured):
    """Replaces a series' (or point's) measured fields, keeping its hand fields and their order."""
    for key in MEASURED:
        if key in target and key not in measured:
            del target[key]
    target.update(measured)


def by_x(rows, key):
    xs = {}
    for r in rows:
        xs.setdefault(r[key], []).append(r)
    return xs


# ---------------------------------------------------------------- ingest

def ingest(results_dir, data_path=None, conditions=None, source=None, log=print):
    """Folds the results into benchmarks.json (and the sweep files beside it); returns True when anything changed."""
    data_path = data_path or os.path.join(HERE, "benchmarks.json")
    charts = os.path.dirname(os.path.abspath(data_path))
    with open(data_path, encoding="utf-8") as f:
        original = f.read()
    data = json.loads(original)
    docs = read_results(results_dir)

    # Every row id must be in the table; unpublished ones are listed, table ids no file carries are warned about.
    seen = set()
    for doc in docs.values():
        for rid in row_ids(doc):
            if rid not in ROW_TARGETS:
                raise IngestError(f"{doc['_file']}: row id {rid!r} is not in ROW_TARGETS; add it with its target series")
            seen.add(rid)
    unpublished = sorted(r for r in seen if ROW_TARGETS[r] is None)
    if unpublished:
        log("kept in the results files, not published: " + ", ".join(unpublished))
    missing = sorted(r for r, target in ROW_TARGETS.items() if target and r not in seen)
    if missing:
        log("warning: no results carry " + ", ".join(missing) + "; their series keep the values they had")

    everything = list(docs.values())
    cores = one([d.get("cores") for d in everything], "cores")
    runtime = one([d.get("runtime") for d in everything], "runtime")
    commit = one([d.get("commit") for d in everything], "commit")
    machine = one([d.get("machine") for d in everything if d.get("mode") not in ("baseline", None) and "machine" in d], "machine")
    serializer = one([d.get("serializer") for d in everything if d.get("mode") not in ("baseline", "sweep")], "serializer")
    sets = data["sets"]

    def of(mode, pred=lambda d: True):
        return [d for (stem, run), d in sorted(docs.items(), key=lambda kv: (kv[0][0], kv[0][1])) if d.get("mode") == mode and pred(d)]

    def rows_for(doc_list, rid):
        return [r for d in doc_list for r in d.get("rows", []) if r["id"] == rid]

    def series(set_key, sid):
        for s in sets[set_key]["series"]:
            if s["id"] == sid:
                return s
        raise IngestError(f"benchmarks.json has no series {set_key}/{sid}")

    def meta(set_key, doc_list, harness, policy, workers=None):
        if not doc_list:
            return
        st = sets[set_key]
        st["date"] = ", ".join(sorted({d["date"] for d in doc_list}))
        where = f"commit {commit[:7]}" if commit else "a local run"
        st["source"] = f"Hugging Face job {source}, {where}" if source else where
        st["machine"] = machine
        st["runtime"] = runtime
        st["harness"] = harness
        st["policy"] = policy
        if workers is not None:
            st["workers"] = workers
        st["cores"] = cores
        st["run_count"] = max(len(doc_list), 1)
        if conditions is not None:
            st["conditions"] = conditions

    def runs_text(k):
        return f"{k} run" + ("" if k == 1 else "s")

    # sync: one point per thread count; ns is the median of the runs' reported figures.
    sync_docs = of("sync")
    if sync_docs:
        rows = rows_for(sync_docs, "ours-sync")
        groups = by_x(rows, "threads")
        xs = sorted(groups)
        for d in sync_docs:
            if sorted(r["threads"] for r in d["rows"] if r["id"] == "ours-sync") != xs:
                raise IngestError(f"{d['_file']}: its thread counts differ from the other sync runs")
        s = series("sync", "ours-sync")
        s["points"] = [dict(summary(groups[x]), ns=num(round(statistics.median(r["nsPerRequest"] for r in groups[x])))) for x in xs]
        sets["sync"]["x"]["values"] = xs
        seconds = one([d["seconds"] for d in sync_docs], "sync seconds")
        meta("sync", sync_docs, f"TestServer_Console --sync {seconds:g} (1, 2, 4, ... threads up to {cores})",
             f"low, high and median over {runs_text(len(sync_docs))}; ns per request per thread is the median of the runs' reported figures",
             workers=max(xs))

    # async: one series per registration, at one worker and at N.
    for key, pred in (("async1", lambda d: d["workers"] == 1), ("asyncn", lambda d: d["workers"] > 1)):
        docs_ = of("async", pred)
        if not docs_:
            continue
        workers = one([d["workers"] for d in docs_], f"{key} workers")
        seconds = one([d["seconds"] for d in docs_], f"{key} seconds")
        for r in REGISTRATIONS:
            rows = rows_for(docs_, f"{key}-{r}")
            if rows:
                apply(series(key, f"{key}-{r}"), dict(summary(rows), bytes=num(round(statistics.median(x["bytesPerRpc"] for x in rows)))))
        meta(key, docs_, f"TestServer_Console --async {seconds:g} {workers}", f"low, high and median over {runs_text(len(docs_))} of each row", workers=workers)

    # legacy: one point per batch size.
    legacy_docs = of("legacy")
    if legacy_docs:
        groups = by_x(rows_for(legacy_docs, "legacy-string"), "batch")
        xs = sorted(groups)
        sets["legacy"]["series"][0]["points"] = [summary(groups[x]) for x in xs]
        sets["legacy"]["x"]["values"] = xs
        meta("legacy", legacy_docs, "TestServer_Console, menu entry t with stdout redirected (BenchmarkRunner.Benchmark)",
             f"low, high and median over {runs_text(len(legacy_docs))}")

    # kestrel: the default rows from --kestrel, the two TCP rows under EnableAsyncMethods = true from --kestrel async.
    k_docs, ka_docs = of("kestrel"), of("kestrel-async")
    for rid, target in ROW_TARGETS.items():
        if target and target.startswith("kestrel/"):
            rows = rows_for(k_docs + ka_docs, rid)
            if rows:
                m = summary(rows)
                us = [r["usPerRequest"] for r in rows if "usPerRequest" in r]
                if us:
                    m["latency_us"] = [num(round(min(us))), num(round(max(us)))]
                apply(series("kestrel", rid), m)
    if k_docs or ka_docs:
        seconds = one([d["seconds"] for d in k_docs + ka_docs], "kestrel seconds")
        clients = one([d["clients"] for d in k_docs + ka_docs], "kestrel clients")
        meta("kestrel", k_docs + ka_docs, f"TestServer_Console --kestrel {seconds:g} and --kestrel {seconds:g} async ({clients} clients)",
             f"low, high and median over the runs: {runs_text(len(k_docs))} of --kestrel for the EnableAsyncMethods = false rows, "
             f"{runs_text(len(ka_docs))} of --kestrel async for the EnableAsyncMethods = true rows", workers=clients)

    # compare and the in-process paths: both from --compare.
    c_docs = of("compare")
    if c_docs:
        seconds = one([d["seconds"] for d in c_docs], "compare seconds")
        clients = one([d["clients"] for d in c_docs], "compare clients")
        for rid, target in ROW_TARGETS.items():
            if target and target.split("/")[0] in ("compare", "inprocess"):
                rows = rows_for(c_docs, rid)
                if rows:
                    m = summary(rows)
                    us = [r["usPerRequest"] for r in rows if "usPerRequest" in r]
                    if us:
                        m["latency_us"] = [num(round(min(us))), num(round(max(us)))]
                    apply(series(target.split("/")[0], rid), m)
        versions = one([json.dumps(d.get("versions"), sort_keys=True) for d in c_docs], "compare versions")
        if versions:
            v = json.loads(versions)
            sets["compare"]["versions"] = f"StreamJsonRpc {v.get('streamJsonRpc')}, gRPC for .NET {v.get('grpc')}"
        for key in ("compare", "inprocess"):
            meta(key, c_docs, f"TestServer_Console --compare {seconds:g} ({clients} connections)",
                 f"low, high and median over {runs_text(len(c_docs))}; a single figure is one value that every run rounded to", workers=clients)

    # headline: derived from baseline, legacy, sync at N and async at N.
    base_docs = of("baseline")
    head = sets["headline"]
    head_docs = []
    if base_docs:
        groups = by_x(rows_for(base_docs, "headline-123-string"), "batch")
        xs = sorted(groups)
        best = max(xs, key=lambda x: statistics.median(r["rpcPerSec"] for r in groups[x]))
        apply(series("headline", "headline-123-string"), dict(summary(groups[best]), batch=best))
        head_docs += base_docs
    if legacy_docs:
        points, xs = sets["legacy"]["series"][0]["points"], sets["legacy"]["x"]["values"]
        i = render.best_index(points)
        apply(series("headline", "headline-20-string"), dict({k: v for k, v in points[i].items() if k in MEASURED}, batch=xs[i]))
        head_docs += legacy_docs
    if sync_docs:
        last = sets["sync"]["series"][0]["points"][-1]
        apply(series("headline", "headline-20-sync"), {k: v for k, v in last.items() if k in MEASURED and k != "ns"})
        head_docs += sync_docs
        head["workers"] = sets["sync"]["workers"]
    an_docs = of("async", lambda d: d["workers"] > 1)
    if sync_docs and an_docs and sets["asyncn"]["workers"] != sets["sync"]["workers"]:
        log(f"warning: --sync ran up to {sets['sync']['workers']} threads and --async at {sets['asyncn']['workers']} workers; "
            "the headline rows name one count, so run both at the core count")
    if an_docs:
        src = series("asyncn", "asyncn-valuetask-none")
        apply(series("headline", "headline-20-async"), {k: v for k, v in src.items() if k in MEASURED})
        head_docs += an_docs
    base = series("headline", "headline-123-string")
    for s in head["series"][1:]:
        if base_docs and "median" in s and "median" in base:
            s["against"] = f"{s['median'] / base['median']:.1f}×"
    if head_docs:
        st = sets["sync"]
        meta("headline", head_docs,
             f"benchmarks/Baseline (1.2.3 from NuGet), TestServer_Console menu entry t, --sync {one([d['seconds'] for d in sync_docs], 's') or 3:g} "
             f"and --async {one([d['seconds'] for d in an_docs], 's') or 3:g} {st.get('workers', cores)}",
             f"{runs_text(len(base_docs))} of benchmarks/Baseline and {runs_text(len(legacy_docs))} of the t entry, each at its best batch size by median; "
             f"{runs_text(len(sync_docs))} of --sync and {runs_text(len(an_docs))} of --async at {st.get('workers', cores)}; low to high over the runs",
             workers=head.get("workers"))

    # sweep: the run files are copied over sweep.json, sweep-2.json, ...; render.py folds them.
    sweep_docs = sorted(((run, d) for (stem, run), d in docs.items() if d.get("mode", "sweep") == "sweep"), key=lambda p: p[0])
    changed_files = []
    if sweep_docs:
        wanted = {}
        for k, (_, d) in enumerate(sweep_docs, start=1):
            wanted["sweep.json" if k == 1 else f"sweep-{k}.json"] = d["_path"]
        # The fold render.py does at load time, kept in the data so the file carries the measured sweep like every other
        # set; a run that does not fit the sweep series fails here, before any sweep file is replaced.
        sweep = sets["sweep"]
        render.fold_sweep(sweep, [d for _, d in sweep_docs], list(wanted))
        for s in sweep["series"]:
            for point in s["points"]:
                for key in ("low", "high", "median"):
                    point[key] = num(point[key])
                if "cpu" in point:
                    point["cpu"] = {k: num(v) for k, v in point["cpu"].items()}
                point["published"] = published(point["low"], point["high"])
        sweep["runtime"] = runtime
        sweep["run_count"] = len(sweep_docs)
        sweep["policy"] = f"low, high and median over {runs_text(len(sweep_docs))}; each run contributes one observation per cell"
        for name, src in wanted.items():
            dst = os.path.join(charts, name)
            with open(src, "rb") as f:
                content = f.read().replace(b"\r\n", b"\n")
            old = None
            if os.path.exists(dst):
                with open(dst, "rb") as f:
                    old = f.read().replace(b"\r\n", b"\n")
            if old != content:
                with open(dst, "wb") as f:
                    f.write(content)
                changed_files.append(name)
        for path in glob.glob(os.path.join(charts, "sweep*.json")):
            if os.path.basename(path) not in wanted:
                os.remove(path)
                changed_files.append(os.path.basename(path) + " (removed)")
        where = f"commit {commit[:7]}" if commit else "a local run"
        sets["sweep"]["source"] = f"Hugging Face job {source}, {where}" if source else where
        sets["sweep"]["harness"] = f"TestServer_Console --sweep {one([d.get('secondsPerCell') for _, d in sweep_docs], 'sweep seconds'):g}; one file per run, matched by the pattern in runs"
        if conditions is not None:
            sets["sweep"]["conditions"] = conditions

    # Top level: the machine the job ran on.
    if machine:
        data["machine"] = machine
        model = re.sub(r"\s+Processor$", "", machine.split(", ")[0])
        data["host"] = f"{model}, {cores} cores"
    if runtime:
        data["runtime"] = runtime + (", built-in serializer" if serializer == "jsmn" else f", {serializer} serializer" if serializer else "")

    text = dumps(data) + "\n"
    if text.replace("\r\n", "\n") != original.replace("\r\n", "\n"):
        with open(data_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        changed_files.insert(0, os.path.basename(data_path))
    render.load(data_path)  # validates the result, sweep files included
    if changed_files:
        log("wrote " + ", ".join(changed_files))
    else:
        log(f"{os.path.basename(data_path)} and the sweep files unchanged")
    return bool(changed_files)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("results", help="directory of results files (fetch.py's output)")
    ap.add_argument("--source", help="the Hugging Face job id, recorded in each set's source")
    ap.add_argument("--conditions", help="the measurement conditions, recorded in each set but wasm")
    ap.add_argument("--data", help="benchmarks.json to update (default: the one beside this script)")
    args = ap.parse_args(argv)
    try:
        ingest(args.results, args.data, args.conditions, args.source)
    except (IngestError, render.DataError, KeyError, ValueError, OSError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
