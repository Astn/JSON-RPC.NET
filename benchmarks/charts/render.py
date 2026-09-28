"""Renders the benchmark charts (SVG, light and dark), the explorer page and the README tables from benchmarks.json.

    python benchmarks/charts/render.py            # write every output next to this file and the README tables
    python benchmarks/charts/render.py --check    # render in memory and fail if a committed output differs

benchmarks.json is the canonical data: the measured fields that ingest.py folds in from a results directory, the
hand-maintained labels, notes and captions, plus an index of the connection-sweep runs (sweep*.json, one file per
`TestServer_Console --sweep 2 <file>`), which are summarised to low, high and median per cell here. Hand fields may
carry placeholders ({workers}, {batch}, {us}, {p:<series id>}, ...) that load() fills from the measured fields. Each
README table that mirrors a set sits between `<!-- benchmarks:<name> -->` and `<!-- /benchmarks:<name> -->` markers
and is generated here, as are the chart alt texts; `--check` fails when a committed table, alt text, chart or the
explorer differs from the data, and every published string must appear in the README, so a number cannot change in
one place only. Every range drawn is the observed low and high over the stated runs, as a capped interval; a lone value
is a marker. GitHub serves README images as plain <img>, so the SVGs carry everything a reader needs and the explorer
page (inline SVG, vanilla JavaScript, the same summaries embedded) supplies the toggles and exact values. Plain
Python, standard library only.
Design notes: docs/reviews/2026-09-23_codex-astra-charts.md.
"""
import argparse
import glob
import hashlib
import json
import math
import os
import re
import statistics
import sys
import xml.dom.minidom

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
FONT = "-apple-system, 'Segoe UI', Helvetica, Arial, sans-serif"

THEMES = {
    "light": dict(suffix="", bg="#ffffff", edge="#d1d5db", ink="#1f2328", muted="#57606a", grid="#e5e7eb"),
    "dark": dict(suffix="-dark", bg="#0d1117", edge="#30363d", ink="#f0f6fc", muted="#9da7b3", grid="#21262d"),
}
# Okabe-Ito family colours: distinguishable under the common colour-vision deficiencies, 3:1 or better on both themes.
FAMILY = {"ours": "#009e73", "theirs": "#0072b2", "grpc": "#d55e00", "scale": "#767676"}
FAMILY_NAME = {"ours": "JSON-RPC.Net", "theirs": "StreamJsonRpc", "grpc": "gRPC for .NET", "scale": "reference"}
# Setting styles within a family, by the series' declared style index: dash and marker, so identity never rests on colour.
STYLES = [("", "circle"), ("9 5", "square"), ("2 4", "triangle"), ("12 4 2 4", "diamond")]


class DataError(ValueError):
    pass


# ---------------------------------------------------------------- data

def load(path=None):
    """Reads benchmarks.json, folds the sweep runs in, validates, and returns the model with a revision id."""
    path = path or os.path.join(HERE, "benchmarks.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    # The revision hashes the parsed data, not the bytes, so line endings (CRLF checkouts) and whitespace do not change it.
    digest = hashlib.sha256(canonical(data))
    sweep = data["sets"].get("sweep")
    if sweep:
        files = sorted(glob.glob(os.path.join(os.path.dirname(path), sweep["runs"])), key=lambda n: (len(n), n))  # sweep, sweep-2, ..., sweep-10
        if not files:
            raise DataError(f"no sweep runs match {sweep['runs']}; run TestServer_Console --sweep 2 benchmarks/charts/sweep.json")
        runs = []
        for name in files:
            with open(name, encoding="utf-8") as f:
                runs.append(json.load(f))
            digest.update(canonical(runs[-1]))
        fold_sweep(sweep, runs, [os.path.basename(n) for n in files])
        n = len(files)
        sweep["run_count"] = n
        sweep["runs_text"] = f"{n} runs of {sweep['seconds_per_cell']:g} s per point; whiskers span the runs" if n > 1 else "one run per point; no range yet"
    data["revision"] = digest.hexdigest()[:12]
    validate(data)
    expand(data)
    return data


def canonical(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":")).encode("utf-8")


def fold_sweep(sweep, runs, names):
    """Attaches per-cell summaries (low, high, median, n, values) to each sweep series from the raw run files."""
    first = runs[0]
    xs = first["connections"]
    for r in runs:
        if r["connections"] != xs:
            raise DataError("sweep runs disagree on the connection counts")
    by_name = {s["name"]: [] for s in sweep["series"]}
    if len(by_name) != len(sweep["series"]):
        raise DataError("sweep series names must be unique")
    cpu_by_name = {s["name"]: [] for s in sweep["series"]}
    for r in runs:
        seen = set()
        for s in r["series"]:
            if s["name"] not in by_name:
                raise DataError(f"sweep run has an unknown series {s['name']!r}; add it to benchmarks.json")
            if len(s["rpcPerSec"]) != len(xs):
                raise DataError(f"sweep series {s['name']!r} has {len(s['rpcPerSec'])} values for {len(xs)} connection counts")
            for field in ("cpuSystem", "cpuProcess", "cpuClients"):
                if field in s and len(s[field]) != len(xs):
                    raise DataError(f"sweep series {s['name']!r} has {len(s[field])} {field} values for {len(xs)} connection counts")
            by_name[s["name"]].append(s["rpcPerSec"])
            cpu_by_name[s["name"]].append(s)
            seen.add(s["name"])
        missing = set(by_name) - seen
        if missing:
            raise DataError(f"sweep run lacks {sorted(missing)}")
    sweep["x"]["values"] = xs
    sweep["run_files"] = names
    sweep["seconds_per_cell"] = first["secondsPerCell"]
    sweep["pipeline"] = first["pipeline"]
    sweep["date"] = ", ".join(sorted({r["date"] for r in runs}))
    sweep["machine"] = first["machine"]
    cores = {r["cores"] for r in runs if "cores" in r}
    if len(cores) > 1:
        raise DataError("sweep runs disagree on available cores")
    if cores:
        sweep["cores"] = cores.pop()
    else:
        sweep.pop("cores", None)  # older run files carry no core count; do not keep one from an earlier fold
    for s in sweep["series"]:
        cols = list(zip(*by_name[s["name"]]))
        s["points"] = [dict(low=min(c), high=max(c), median=statistics.median(c), n=len(c), values=list(c),
                            status="range" if len(c) > 1 else "single") for c in cols]
        for i, point in enumerate(s["points"]):
            cpu = {}
            for field, label in (("cpuSystem", "system"), ("cpuProcess", "process"), ("cpuClients", "clients")):
                values = [run[field][i] for run in cpu_by_name[s["name"]] if field in run and run[field][i] is not None]
                if values:
                    cpu[label] = statistics.median(values)
            if cpu:
                point["cpu"] = cpu


def validate(data):
    ids = set()
    for key, st in data["sets"].items():
        for s in st["series"]:
            if s["id"] in ids:
                raise DataError(f"duplicate series id {s['id']}")
            ids.add(s["id"])
            if s["family"] not in FAMILY:
                raise DataError(f"{s['id']}: unknown family {s['family']!r}")
            if "points" in s:
                points = s["points"]
                if "x" in st and len(points) != len(st["x"]["values"]):
                    raise DataError(f"{s['id']}: {len(points)} points for {len(st['x']['values'])} x values")
            elif isinstance(s.get("values"), dict):  # the wasm rows: one figure per mode (a list of values is the runs)
                points = [dict(low=v, high=v) for v in s["values"].values()]
            else:
                points = [s]
            for p in points:
                lo, hi = p["low"], p["high"]
                if not (isinstance(lo, (int, float)) and isinstance(hi, (int, float)) and math.isfinite(lo) and math.isfinite(hi)):
                    raise DataError(f"{s['id']}: non-finite value")
                if lo <= 0 or hi <= 0:
                    raise DataError(f"{s['id']}: values must be positive (log axes)")
                if lo > hi:
                    raise DataError(f"{s['id']}: low {lo} above high {hi}")
        for g in st.get("groups", []):
            for sid in g["series"]:
                if sid not in ids:
                    raise DataError(f"group {g['heading']!r} names unknown series {sid}")
    return data


# ---------------------------------------------------------------- placeholders

PLACEHOLDER = re.compile(r"\{([a-z_]+)(?::([A-Za-z0-9_.-]+))?\}")
WORDS = {1: "one", 2: "two", 3: "three", 4: "four", 5: "five", 6: "six", 7: "seven", 8: "eight", 9: "nine", 10: "ten"}


def fmt_int(v):
    """A count as the README writes it: thousands separators, no decimals for whole numbers."""
    return f"{int(v):,}" if float(v).is_integer() else f"{v:g}"


def fmt_us(v):
    """A [low, high] microsecond pair: "10", or "88 to 130" when the whole microseconds differ."""
    lo, hi = round(v[0]), round(v[1])
    return f"{lo}" if lo == hi else f"{lo} to {hi}"


def set_context(data, st):
    """The placeholder values a set's own text may use: its parallelism, cores, date, run count and sweep fields."""
    ctx = {}
    if isinstance(data.get("host"), str):
        ctx["host"] = data["host"]
    for key in ("workers", "cores", "pipeline"):
        if isinstance(st.get(key), (int, float)):
            ctx[key] = fmt_int(st[key])
    for key in ("date", "runs_text"):
        if isinstance(st.get(key), str):
            ctx[key] = st[key]
    if isinstance(st.get("run_count"), int):
        ctx["runs"] = str(st["run_count"])
        ctx["runs_word"] = WORDS.get(st["run_count"], str(st["run_count"]))
    if st.get("x", {}).get("values"):
        ctx["x_max"] = fmt_int(st["x"]["values"][-1])
    return ctx


def series_context(ctx, s):
    """A series adds its measured batch size, bytes per request, round-trip time and observation count."""
    ctx = dict(ctx)
    for key in ("batch", "bytes"):
        if isinstance(s.get(key), (int, float)):
            ctx[key] = fmt_int(s[key])
    if isinstance(s.get("latency_us"), list) and len(s["latency_us"]) == 2:
        ctx["us"] = fmt_us(s["latency_us"])
    if isinstance(s.get("n"), int):
        ctx["runs"] = str(s["n"])
        ctx["runs_word"] = WORDS.get(s["n"], str(s["n"]))
    return ctx


def expand_text(value, ctx, published, where):
    def sub(m):
        name, arg = m.group(1), m.group(2)
        if name == "p":
            if arg not in published:
                raise DataError(f"{where}: {{p:{arg}}} names no series with a published figure")
            return published[arg]
        if arg is not None or name not in ctx:
            raise DataError(f"{where}: placeholder {m.group(0)} has no value here")
        return ctx[name]
    return PLACEHOLDER.sub(sub, value)


def expand_value(value, ctx, published, where):
    if isinstance(value, str):
        return expand_text(value, ctx, published, where)
    if isinstance(value, list):
        return [expand_value(v, ctx, published, where) for v in value]
    if isinstance(value, dict):
        return {k: expand_value(v, ctx, published, where) for k, v in value.items()}
    return value


def expand(data):
    """Fills the placeholders in every set's and series' text in place, then resolves the README summary rows."""
    published = {s["id"]: s["published"] for st in data["sets"].values() for s in st["series"] if isinstance(s.get("published"), str)}
    for key, st in data["sets"].items():
        ctx = set_context(data, st)
        for field in list(st):
            if field == "series":
                st["series"] = [expand_value(s, series_context(ctx, s), published, f"{key}/{s['id']}") for s in st["series"]]
            else:
                st[field] = expand_value(st[field], ctx, published, key)
    data["summary"] = [summary_row(data, row, published) for row in data.get("summary", [])]
    return data


def best_index(points):
    """The point with the highest median (the midpoint of low and high when a point has no median)."""
    return max(range(len(points)), key=lambda i: points[i].get("median", (points[i]["low"] + points[i]["high"]) / 2))


def summary_row(data, row, published):
    """A summary row's RPC/s from its source ("set:id", "set:id:last", "set:id:best" or "set:id,id,...:range")."""
    parts = row["source"].split(":")
    if len(parts) not in (2, 3) or parts[0] not in data["sets"]:
        raise DataError(f"summary: bad source {row['source']!r}")
    st = data["sets"][parts[0]]
    by_id = {s["id"]: s for s in st["series"]}
    ids = parts[1].split(",")
    for sid in ids:
        if sid not in by_id:
            raise DataError(f"summary: {row['source']!r} names unknown series {sid}")
    mode = parts[2] if len(parts) == 3 else "value"
    ctx = set_context(data, st)
    if mode == "last":
        value = fmt_range(by_id[ids[0]]["points"][-1])
    elif mode == "best":
        points = by_id[ids[0]]["points"]
        i = best_index(points)
        value = fmt_range(points[i])
        ctx["batch"] = fmt_int(st["x"]["values"][i])
    elif mode == "range":
        lo, hi = fmt(min(by_id[i]["low"] for i in ids)), fmt(max(by_id[i]["high"] for i in ids))
        value = lo if lo == hi else f"{lo} to {hi}"
    elif mode == "value":
        value = fmt_range(by_id[ids[0]])
    else:
        raise DataError(f"summary: unknown selector {mode!r}")
    where = f"summary/{row['source']}"
    return dict(what=expand_text(row["what"], ctx, published, where), api=expand_text(row["api"], ctx, published, where),
                value=value, details=expand_text(row.get("details", ""), ctx, published, where))


# ---------------------------------------------------------------- text and geometry helpers

def esc(s):
    return str(s).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def fmt_axis(v):
    if v >= 1e6:
        return f"{v / 1e6:g} M"
    if v >= 1e3:
        return f"{v / 1e3:g} k"
    return f"{v:g}"


def fmt(v):
    """Three significant figures with a unit, for a value that has no published text."""
    if v >= 1e6:
        return f"{float(f'{v / 1e6:.3g}'):g} M"
    if v >= 1e3:
        return f"{float(f'{v / 1e3:.3g}'):g} k"
    return f"{float(f'{v:.3g}'):g}"


def fmt_range(p):
    """The published text when the data carries one; otherwise the endpoints at three significant figures."""
    if p.get("published"):
        return p["published"]
    if p["low"] == p["high"]:
        return fmt(p["low"])
    return f"{fmt(p['low'])} to {fmt(p['high'])}"


def text(x, y, s, t, size=13, fill=None, anchor="start", weight=None, extra=""):
    w = f' font-weight="{weight}"' if weight else ""
    return f'<text x="{x:.1f}" y="{y:.1f}" text-anchor="{anchor}" font-size="{size}" fill="{fill or t["ink"]}"{w}{extra}>{esc(s)}</text>'


def marker(x, y, shape, color, t, r=4.5):
    if shape == "circle":
        return f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{t["bg"]}" stroke="{color}" stroke-width="2"/>'
    if shape == "square":
        return f'<rect x="{x - r:.1f}" y="{y - r:.1f}" width="{2 * r}" height="{2 * r}" fill="{t["bg"]}" stroke="{color}" stroke-width="2"/>'
    if shape == "diamond":
        d = r * 1.3
        return (f'<polygon points="{x:.1f},{y - d:.1f} {x + d:.1f},{y:.1f} {x:.1f},{y + d:.1f} {x - d:.1f},{y:.1f}" '
                f'fill="{color}" stroke="{color}" stroke-width="1.5" stroke-linejoin="round"/>')
    s = r * 1.25
    return (f'<polygon points="{x:.1f},{y - s:.1f} {x + s:.1f},{y + s * 0.8:.1f} {x - s:.1f},{y + s * 0.8:.1f}" '
            f'fill="{t["bg"]}" stroke="{color}" stroke-width="2" stroke-linejoin="round"/>')


def capped(out, x0, y0, x1, y1, color, cap, vertical):
    """A range interval from (x0, y0) to (x1, y1) with opaque end caps."""
    out.append(f'<line x1="{x0:.1f}" y1="{y0:.1f}" x2="{x1:.1f}" y2="{y1:.1f}" stroke="{color}" stroke-width="3"/>')
    for x, y in ((x0, y0), (x1, y1)):
        if vertical:
            out.append(f'<line x1="{x - cap:.1f}" y1="{y:.1f}" x2="{x + cap:.1f}" y2="{y:.1f}" stroke="{color}" stroke-width="3"/>')
        else:
            out.append(f'<line x1="{x:.1f}" y1="{y - cap:.1f}" x2="{x:.1f}" y2="{y + cap:.1f}" stroke="{color}" stroke-width="3"/>')


def card(width, height, title, subtitle, t, revision):
    lines = subtitle if isinstance(subtitle, (list, tuple)) else [subtitle]
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" '
           f'font-family="{FONT}" font-size="13" data-revision="{revision}">',
           f'<title>{esc(title)}</title><desc>{esc(" ".join(lines))} Data revision {revision}.</desc>',
           f'<rect width="{width}" height="{height}" rx="10" fill="{t["bg"]}" stroke="{t["edge"]}"/>',
           text(24, 32, title, t, size=18, weight=600)]
    y = 54
    for line in lines:
        out.append(text(24, y, line, t, size=12, fill=t["muted"]))
        y += 16
    return out


class Linear:
    def __init__(self, lo, hi, p0, p1):
        if hi <= lo:
            raise DataError("empty linear domain")
        self.lo, self.hi, self.p0, self.p1 = lo, hi, p0, p1

    def __call__(self, v):
        return self.p0 + (self.p1 - self.p0) * (v - self.lo) / (self.hi - self.lo)

    def ticks(self, max_ticks=6):
        span = self.hi - self.lo
        step = 10 ** math.floor(math.log10(span))
        if span / step < 3:
            step /= 2
        while span / step > max_ticks:
            step *= 2
        out, v = [], math.ceil(self.lo / step) * step
        while v <= self.hi + 1e-9:
            out.append((v, True))
            v += step
        return out


class Log:
    """Domain from the last 1-2-5 step at or below the smallest value to the first one with 15 % headroom above the largest."""

    def __init__(self, lo, hi, p0, p1):
        if lo <= 0 or hi < lo:
            raise DataError("log domain needs positive values")
        d = 10 ** math.floor(math.log10(lo))
        self.lo = max(d * m for m in (1, 2, 5) if d * m <= lo)
        d = 10 ** math.floor(math.log10(hi * 1.15))
        self.hi = next(d * m for m in (1, 2, 5, 10) if d * m >= hi * 1.15)
        if self.hi <= self.lo:
            self.hi = self.lo * 10
        self.p0, self.p1 = p0, p1

    def __call__(self, v):
        return self.p0 + (self.p1 - self.p0) * (math.log10(v) - math.log10(self.lo)) / (math.log10(self.hi) - math.log10(self.lo))

    def ticks(self):
        out, d = [], 10 ** math.floor(math.log10(self.lo))
        while d <= self.hi:
            for m in (1, 2, 5):
                if self.lo <= d * m <= self.hi:
                    out.append((d * m, m == 1))
            d *= 10
        return out


def log2_positions(values, x0, x1):
    """x positions for doubling counts: log2 spacing from the numeric counts, centred when there is one value."""
    if len(values) == 1:
        return [(x0 + x1) / 2]
    a, b = math.log2(values[0]), math.log2(values[-1])
    return [x0 + (x1 - x0) * (math.log2(v) - a) / (b - a) for v in values]


def nice_max(v):
    step = 10 ** math.floor(math.log10(v))
    if v / step < 2:
        step /= 4
    elif v / step < 5:
        step /= 2
    return math.ceil(v / step) * step


def style(series):
    return STYLES[series.get("style", 0) % len(STYLES)]


def groups_of(st):
    by_id = {s["id"]: s for s in st["series"]}
    return [(g["heading"], [by_id[i] for i in g["series"]], g["rule"]) for g in st["groups"]]


# ---------------------------------------------------------------- charts: each returns SVG text for one theme

def interval_chart(st, subtitle, t, revision, name, width=940):
    """Horizontal capped intervals on a log axis, grouped; a group with rule=True sits under a separator."""
    groups = groups_of(st)
    left, num_w, row_h, group_h = 330, 160, 40, 30
    top = 70 + 16 * len(subtitle)
    x0, x1 = left, width - num_w - 30
    n_rows = sum(len(rows) for _, rows, _ in groups)
    height = top + row_h * n_rows + sum(group_h if h else 6 for h, _, _ in groups) + 12 * sum(r for _, _, r in groups) + 52
    xs = Log(min(s["low"] for s in st["series"]), max(s["high"] for s in st["series"]), x0, x1)
    out = card(width, height, st["title"], subtitle, t, revision)
    y_axis = height - 42
    for v, major in xs.ticks():
        x = xs(v)
        out.append(f'<line x1="{x:.1f}" y1="{top - 6}" x2="{x:.1f}" y2="{y_axis}" stroke="{t["grid"]}" stroke-width="{1 if major else 0.6}"/>')
        out.append(text(x, y_axis + 16, fmt_axis(v), t, size=11 if major else 10, fill=t["muted"], anchor="middle"))
    out.append(text(x1, height - 10, "requests per second, log scale", t, size=11, fill=t["muted"], anchor="end"))
    y = top
    for heading, rows, rule in groups:
        if rule:
            y += 6
            out.append(f'<line x1="24" y1="{y}" x2="{width - 24}" y2="{y}" stroke="{t["edge"]}"/>')
            y += 6
        if heading:
            out.append(text(24, y + 14, heading, t, size=12, weight=600))
            y += group_h
        else:
            y += 6
        for s in rows:
            color = FAMILY[s["family"]]
            note = s.get("note")
            cy = y + row_h / 2 - (6 if note else 0)
            out.append(text(left - 14, cy + 4, s["label"], t, anchor="end"))
            if note:
                out.append(text(left - 14, cy + 18, note, t, size=10, fill=t["muted"], anchor="end"))
            if s["low"] == s["high"]:
                out.append(marker(xs(s["low"]), cy, "circle", color, t))
                out.append(f'<circle cx="{xs(s["low"]):.1f}" cy="{cy:.1f}" r="2" fill="{color}"/>')
            else:
                capped(out, xs(s["low"]), cy, xs(s["high"]), cy, color, 6, vertical=False)
            out.append(text(x1 + 18, cy + 4, fmt_range(s), t, weight=600))
            y += row_h
    out.append("</svg>")
    return "\n".join(out) + "\n"


def headline_chart(st, subtitle, t, revision, name, width=940):
    """Horizontal bars on a linear axis, grouped; every bar carries its figure and its multiple of the first series,
    which is the reference row. Linear, unlike the other charts, because the point is the size of the difference."""
    groups = groups_of(st)
    left, num_w, row_h, group_h = 330, 170, 44, 30
    top = 70 + 16 * len(subtitle)
    x0, x1 = left, width - num_w - 30
    n_rows = sum(len(rows) for _, rows, _ in groups)
    height = top + row_h * n_rows + sum(group_h if h else 6 for h, _, _ in groups) + 12 * sum(r for _, _, r in groups) + 52
    xs = Linear(0, nice_max(max(s["high"] for s in st["series"])), x0, x1)
    base = st["series"][0]["high"]
    out = card(width, height, st["title"], subtitle, t, revision)
    y_axis = height - 42
    for v, _ in xs.ticks():
        x = xs(v)
        out.append(f'<line x1="{x:.1f}" y1="{top - 6}" x2="{x:.1f}" y2="{y_axis}" stroke="{t["grid"]}"/>')
        out.append(text(x, y_axis + 16, fmt_axis(v), t, size=11, fill=t["muted"], anchor="middle"))
    out.append(text(x1, height - 10, "requests per second, linear scale", t, size=11, fill=t["muted"], anchor="end"))
    y = top
    for heading, rows, rule in groups:
        if rule:
            y += 6
            out.append(f'<line x1="24" y1="{y}" x2="{width - 24}" y2="{y}" stroke="{t["edge"]}"/>')
            y += 6
        if heading:
            out.append(text(24, y + 14, heading, t, size=12, weight=600))
            y += group_h
        else:
            y += 6
        for s in rows:
            color = FAMILY[s["family"]]
            note = s.get("note")
            cy = y + row_h / 2 - (6 if note else 0)
            out.append(text(left - 14, cy + 4, s["label"], t, anchor="end"))
            if note:
                out.append(text(left - 14, cy + 18, note, t, size=10, fill=t["muted"], anchor="end"))
            out.append(f'<rect x="{x0}" y="{cy - 9:.1f}" width="{xs(s["low"]) - x0:.1f}" height="18" rx="3" fill="{color}"/>')
            if s["high"] > s["low"]:
                out.append(f'<rect x="{xs(s["low"]):.1f}" y="{cy - 9:.1f}" width="{xs(s["high"]) - xs(s["low"]):.1f}" height="18" rx="3" fill="{color}" fill-opacity="0.35"/>')
            out.append(text(x1 + 18, cy + 4, fmt_range(s), t, weight=600))
            if s is not st["series"][0]:
                multiple = s.get("against") or f"{s['high'] / base:.1f}\u00d7"
                out.append(text(x1 + 18, cy + 18, f"{multiple} the first row", t, size=10, fill=t["muted"]))
            y += row_h
    out.append("</svg>")
    return "\n".join(out) + "\n"


def scaling_chart(st, subtitle, t, revision, name, width=940):
    """Two aligned panels: aggregate RPC/s as an envelope (opaque low and high lines, capped intervals, no centre
    line) and the reported ns per request per worker as single points. x is log2-spaced: each step doubles."""
    s = st["series"][0]
    threads, pts = st["x"]["values"], s["points"]
    left, right = 86, 40
    top = 70 + 16 * len(subtitle)
    p1_y0, p1_y1 = top + 250, top + 16
    p2_y0, p2_y1 = p1_y0 + 180, p1_y0 + 56
    height = p2_y0 + 60
    x0, x1 = left, width - right - 40
    xpos = log2_positions(threads, x0, x1)
    ys = Linear(0, nice_max(max(p["high"] for p in pts)), p1_y0, p1_y1)
    ns = Linear(0, nice_max(max(p["ns"] for p in pts) * 1.25), p2_y0, p2_y1)
    color = FAMILY[s["family"]]
    out = card(width, height, st["title"], subtitle, t, revision)
    out.append(text(x0, p1_y1 - 4, "Aggregate requests per second: low to high over the day's runs", t, size=11, fill=t["muted"]))
    for v, _ in ys.ticks():
        y = ys(v)
        out.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{t["grid"]}"/>')
        out.append(text(x0 - 8, y + 4, fmt_axis(v), t, size=11, fill=t["muted"], anchor="end"))
    for x in xpos:
        out.append(f'<line x1="{x:.1f}" y1="{p1_y1}" x2="{x:.1f}" y2="{p2_y0}" stroke="{t["grid"]}" stroke-width="0.6"/>')
    clip = f"clip-{name}-{t['suffix'].strip('-') or 'light'}"
    out.append(f'<clipPath id="{clip}"><rect x="{x0}" y="{p1_y1}" width="{x1 - x0}" height="{p1_y0 - p1_y1}"/></clipPath>')
    base = pts[0]["low"]
    ref = " ".join(f"{x:.1f},{ys(base * n):.1f}" for x, n in zip(xpos, threads))
    out.append(f'<polyline points="{ref}" fill="none" stroke="{t["muted"]}" stroke-width="1.5" stroke-dasharray="6 5" clip-path="url(#{clip})"/>')
    lx, ly = max(((x, ys(base * n)) for x, n in zip(xpos, threads) if base * n <= ys.hi), key=lambda p: p[0])
    out.append(text(lx - 10, ly - 10, "linear scaling from the 1-thread low", t, size=11, fill=t["muted"], anchor="end"))
    upper = " ".join(f"{x:.1f},{ys(p['high']):.1f}" for x, p in zip(xpos, pts))
    lower_pts = [f"{x:.1f},{ys(p['low']):.1f}" for x, p in zip(xpos, pts)]
    lower_rev = " ".join(reversed(lower_pts))
    out.append(f'<polygon points="{upper} {lower_rev}" fill="{color}" fill-opacity="0.15"/>')
    for poly in (upper, " ".join(lower_pts)):
        out.append(f'<polyline points="{poly}" fill="none" stroke="{color}" stroke-width="2"/>')
    last = len(xpos) - 1
    for i, (x, p) in enumerate(zip(xpos, pts)):
        capped(out, x, ys(p["low"]), x, ys(p["high"]), color, 7, vertical=True)
        anchor = "start" if i == 0 else ("end" if i == last else "middle")
        dx = 12 if i == 0 else (-12 if i == last else 0)
        out.append(text(x + dx, ys(p["high"]) - 12, fmt_range(p), t, size=11, weight=600, anchor=anchor))
    out.append(text(x0, p2_y1 - 6, "Reported ns per request per worker thread: one value per row, no range", t, size=11, fill=t["muted"]))
    for v, _ in ns.ticks(max_ticks=4):
        y = ns(v)
        out.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{t["grid"]}"/>')
        out.append(text(x0 - 8, y + 4, f"{v:.0f}", t, size=11, fill=t["muted"], anchor="end"))
    ns_pts = " ".join(f"{x:.1f},{ns(p['ns']):.1f}" for x, p in zip(xpos, pts))
    out.append(f'<polyline points="{ns_pts}" fill="none" stroke="{color}" stroke-width="1.5" stroke-dasharray="2 4"/>')
    for i, (x, p) in enumerate(zip(xpos, pts)):
        out.append(marker(x, ns(p["ns"]), "circle", color, t))
        anchor = "start" if i == 0 else ("end" if i == last else "middle")
        dx = 10 if i == 0 else (-10 if i == last else 0)
        out.append(text(x + dx, ns(p["ns"]) - 11, f"{p['ns']} ns", t, size=11, weight=600, anchor=anchor))
    out.append(f'<line x1="{x0}" y1="{p2_y0}" x2="{x1}" y2="{p2_y0}" stroke="{t["muted"]}"/>')
    for x, n in zip(xpos, threads):
        out.append(text(x, p2_y0 + 18, str(n), t, size=12, anchor="middle"))
    out.append(text((x0 + x1) / 2, height - 12, st["x"].get("caption", "worker threads (each step doubles)"), t, size=12, fill=t["muted"], anchor="middle"))
    out.append(text(0, 0, "requests per second", t, size=12, fill=t["muted"], anchor="middle", extra=f' transform="translate(20 {(p1_y0 + p1_y1) / 2:.1f}) rotate(-90)"'))
    out.append(text(0, 0, "ns per request", t, size=12, fill=t["muted"], anchor="middle", extra=f' transform="translate(20 {(p2_y0 + p2_y1) / 2:.1f}) rotate(-90)"'))
    out.append("</svg>")
    return "\n".join(out) + "\n"


def sweep_chart(st, subtitle, t, revision, name, width=940):
    """One panel per library on a shared log y axis; markers at the median, capped whiskers low to high."""
    conns = st["x"]["values"]
    families = []
    for s in st["series"]:
        if not families or families[-1][0] != s["family"]:
            families.append((s["family"], []))
        families[-1][1].append(s)
    left, legend_w, panel_h, gap = 86, 300, 175, 26
    top = 70 + 16 * len(subtitle)
    x0, x1 = left, width - legend_w - 30
    height = top + len(families) * (panel_h + gap) + 30
    xpos = log2_positions(conns, x0, x1)
    lo_all = min(p["low"] for s in st["series"] for p in s["points"])
    hi_all = max(p["high"] for s in st["series"] for p in s["points"])
    out = card(width, height, st["title"], subtitle, t, revision)
    py = top
    for family, series in families:
        color = FAMILY[family]
        y0, y1 = py + panel_h, py + 14
        ys = Log(lo_all, hi_all, y0, y1)
        out.append(text(x0, y1 - 2, FAMILY_NAME[family], t, size=12, weight=600))
        for v, major in ys.ticks():
            y = ys(v)
            out.append(f'<line x1="{x0}" y1="{y:.1f}" x2="{x1}" y2="{y:.1f}" stroke="{t["grid"]}" stroke-width="{1 if major else 0.6}"/>')
            if major:
                out.append(text(x0 - 8, y + 4, fmt_axis(v), t, size=11, fill=t["muted"], anchor="end"))
        for x in xpos:
            out.append(f'<line x1="{x:.1f}" y1="{y1}" x2="{x:.1f}" y2="{y0}" stroke="{t["grid"]}" stroke-width="0.6"/>')
        out.append(f'<line x1="{x0}" y1="{y0}" x2="{x1}" y2="{y0}" stroke="{t["muted"]}"/>')
        for x, c in zip(xpos, conns):
            out.append(text(x, y0 + 16, str(c), t, size=11, anchor="middle"))
        ly = y1 + 6
        for s in series:
            dash, shape = style(s)
            d = f' stroke-dasharray="{dash}"' if dash else ""
            pts = s["points"]
            mid = " ".join(f"{x:.1f},{ys(p['median']):.1f}" for x, p in zip(xpos, pts))
            out.append(f'<polyline points="{mid}" fill="none" stroke="{color}" stroke-width="2"{d}/>')
            for x, p in zip(xpos, pts):
                if p["low"] != p["high"]:
                    capped(out, x, ys(p["low"]), x, ys(p["high"]), color, 5, vertical=True)
            for x, p in zip(xpos, pts):
                out.append(marker(x, ys(p["median"]), shape, color, t))
            lx = x1 + 26
            out.append(f'<line x1="{lx}" y1="{ly + 5}" x2="{lx + 30}" y2="{ly + 5}" stroke="{color}" stroke-width="2"{d}/>')
            out.append(marker(lx + 15, ly + 5, shape, color, t, r=4))
            out.append(text(lx + 38, ly + 9, s["label"], t, size=11.5))
            out.append(text(lx + 38, ly + 22, f"at {conns[-1]}: {fmt_range(pts[-1])}", t, size=10.5, fill=t["muted"]))
            ly += 32
        py += panel_h + gap
    out.append(text((x0 + x1) / 2, height - 10, f"{st['x']['name']}; each step doubles", t, size=12, fill=t["muted"], anchor="middle"))
    out.append(text(0, 0, "requests per second, log scale, shared by the panels; marker at the median, whisker low to high", t, size=12, fill=t["muted"], anchor="middle",
                    extra=f' transform="translate(20 {(top + height - 30) / 2:.1f}) rotate(-90)"'))
    out.append("</svg>")
    return "\n".join(out) + "\n"


def paired_chart(st, subtitle, t, revision, name, width=940):
    """Two modes per row (interpreter and AOT) as distinct markers joined by a thin line, on a log axis."""
    groups = groups_of(st)
    modes = st["modes"]
    left, num_w, row_h, group_h = 300, 190, 30, 30
    top = 70 + 16 * len(subtitle)
    x0, x1 = left, width - num_w - 30
    n_rows = sum(len(rows) for _, rows, _ in groups)
    height = top + row_h * n_rows + sum(group_h if h else 6 for h, _, _ in groups) + 12 * sum(r for _, _, r in groups) + 80
    vals = [v for s in st["series"] for v in s["values"].values()]
    xs = Log(min(vals), max(vals), x0, x1)
    out = card(width, height, st["title"], subtitle, t, revision)
    y_axis = height - 66
    for v, major in xs.ticks():
        x = xs(v)
        out.append(f'<line x1="{x:.1f}" y1="{top - 6}" x2="{x:.1f}" y2="{y_axis}" stroke="{t["grid"]}" stroke-width="{1 if major else 0.6}"/>')
        if major:
            out.append(text(x, y_axis + 16, fmt_axis(v), t, size=11, fill=t["muted"], anchor="middle"))
    out.append(text(x1, y_axis + 34, "add(1, 2) operations per second, log scale", t, size=11, fill=t["muted"], anchor="end"))
    lx = 24
    for m in modes:
        out.append(marker(lx + 6, y_axis + 30, m["marker"], FAMILY["ours"], t, r=4))
        out.append(text(lx + 18, y_axis + 34, f"{m['label']} ({m['policy']})", t, size=11, fill=t["muted"]))
        lx += 200
    for i, m in enumerate(modes):
        out.append(text(x1 + 18 + 90 * i, top - 8, m["label"], t, size=11, fill=t["muted"], weight=600))
    y = top
    for heading, rows, rule in groups:
        if rule:
            y += 6
            out.append(f'<line x1="24" y1="{y}" x2="{width - 24}" y2="{y}" stroke="{t["edge"]}"/>')
            y += 6
        if heading:
            out.append(text(24, y + 14, heading, t, size=12, weight=600))
            y += group_h
        else:
            y += 6
        for s in rows:
            color = FAMILY[s["family"]]
            cy = y + row_h / 2
            out.append(text(left - 14, cy + 4, s["label"], t, size=12, anchor="end"))
            xa, xb = xs(s["values"][modes[0]["id"]]), xs(s["values"][modes[1]["id"]])
            out.append(f'<line x1="{xa:.1f}" y1="{cy:.1f}" x2="{xb:.1f}" y2="{cy:.1f}" stroke="{color}" stroke-width="1.5" stroke-opacity="0.6"/>')
            for i, m in enumerate(modes):
                out.append(marker(xs(s["values"][m["id"]]), cy, m["marker"], color, t))
                out.append(text(x1 + 18 + 90 * i, cy + 4, s["published"][m["id"]], t, size=12, weight=600))
            y += row_h
    out.append("</svg>")
    return "\n".join(out) + "\n"


# ---------------------------------------------------------------- outputs

CHARTS = [("headline-1x-vs-2", headline_chart, "headline"), ("sync-threads", scaling_chart, "sync"),
          ("kestrel-transports", interval_chart, "kestrel"), ("compare-streamjsonrpc", interval_chart, "compare"),
          ("inprocess-paths", interval_chart, "inprocess"), ("compare-connections", sweep_chart, "sweep"),
          ("wasm-interop", paired_chart, "wasm")]


def chart_specs(data):
    """(file stem, chart function, measurement set, subtitle lines) for every static chart; the subtitles are the sets'
    `subtitle` lines with their placeholders filled."""
    sets = data["sets"]
    return [(stem, fn, sets[key], sets[key]["subtitle"]) for stem, fn, key in CHARTS]


def render_all(data):
    """Every output as {filename: text}: two SVGs per chart and the explorer page."""
    outputs = {}
    for stem, fn, st, subtitle in chart_specs(data):
        for t in THEMES.values():
            outputs[f"{stem}{t['suffix']}.svg"] = fn(st, subtitle, t, data["revision"], stem)
    outputs["explorer.html"] = explorer(data)
    return outputs


def explorer(data):
    """The interactive page: the template with the summarised data embedded, so the saved file needs no server."""
    with open(os.path.join(HERE, "explorer_template.html"), encoding="utf-8") as f:
        template = f.read()
    payload = json.dumps(dict(revision=data["revision"], machine=data["machine"], runtime=data["runtime"],
                              families=FAMILY, familyNames=FAMILY_NAME, styles=STYLES, sets=data["sets"]),
                         separators=(",", ":"), sort_keys=True).replace("<", "\\u003c")
    return template.replace("__DATA__", payload).replace("__REVISION__", data["revision"])


# ---------------------------------------------------------------- README tables

# The generated blocks each README carries, in no particular order; every one must be present exactly once.
README_BLOCKS = {"README.md": ["summary", "headline", "sync", "async", "legacy", "kestrel", "compare", "inprocess"],
                 "Json-Rpc/README.md": ["headline"]}
MARKER = re.compile(r"<!-- (/?)benchmarks:([a-z0-9-]+) -->")


def cells(*values):
    """One Markdown table row; an empty cell is written as "| |", as the README tables have them."""
    return "|" + "".join(f" {v} |" if v != "" else " |" for v in values)


def table(header, align, rows):
    """The table and a blank line, so the closing marker never reads as a table row in any Markdown parser."""
    return "\n".join([cells(*header), "| " + " | ".join(align) + " |"] + [cells(*r) for r in rows]) + "\n\n"


def row_text(s):
    return s.get("row", s["label"])


def readme_blocks(data):
    """The Markdown of every generated README table, by block name, from the (expanded) data."""
    S = data["sets"]
    sync, a1, an = S["sync"], S["async1"], S["asyncn"]
    by_reg = {s["id"].split("-", 1)[1]: s for s in an["series"]}
    s0 = sync["series"][0]
    legacy = S["legacy"]
    return {
        "summary": table(["What", "API and mode", "RPC/s", "Details"], ["---", "---", "---:", "---"],
                         [(r["what"], r["api"], r["value"], r["details"]) for r in data["summary"]]),
        "headline": table(["Path", "RPC/s", "Against 1.2.3"], ["---", "---:", "---:"],
                          [(row_text(s), fmt_range(s), s.get("against", "")) for s in S["headline"]["series"]]),
        "sync": table(["Threads", "RPC/s", "ns per request per thread", "Allocations per request"], ["---:", "---:", "---:", "---"],
                      [(fmt_int(x), fmt_range(p), fmt_int(p["ns"]), s0.get("note", "") if i == 0 else "")
                       for i, (x, p) in enumerate(zip(sync["x"]["values"], s0["points"]))]),
        "async": table(["Registration", "1 worker", f"{fmt_int(an['workers'])} workers", "B per request"], ["---", "---:", "---:", "---:"],
                       [(row_text(s), fmt_range(s), fmt_range(by_reg[s["id"].split("-", 1)[1]]), fmt_int(s["bytes"])) for s in a1["series"]]),
        "legacy": table(["Batch size", "RPC/s"], ["---:", "---:"],
                        [(fmt_int(x), fmt_range(p)) for x, p in zip(legacy["x"]["values"], legacy["series"][0]["points"])]),
        "kestrel": table(["Transport", "RPC/s", "Note"], ["---", "---:", "---"],
                         [(row_text(s), fmt_range(s), s.get("row_note", "")) for s in S["kestrel"]["series"]]),
        "compare": table(["Library and path", "RPC/s"], ["---", "---:"], [(row_text(s), fmt_range(s)) for s in S["compare"]["series"]]),
        "inprocess": table(["Path", "RPC/s"], ["---", "---:"],
                           [(row_text(s), fmt_range(s) + (f" ({s['rpc_note']})" if s.get("rpc_note") else "")) for s in S["inprocess"]["series"]]),
    }


def readme_alts(data):
    """The alt text of each chart image in README.md, by chart stem, from the sets' `alt` templates."""
    return {stem: data["sets"][key]["alt"] for stem, _, key in CHARTS if "alt" in data["sets"][key]}


def split_blocks(body, where):
    """(name, start, end) of each generated block's body; markers must pair up, open on their own line, and not nest."""
    found, open_ = [], None
    for m in MARKER.finditer(body):
        closing, name = m.group(1) == "/", m.group(2)
        if not closing:
            if open_:
                raise DataError(f"{where}: benchmarks:{name} opens inside benchmarks:{open_[0]}")
            end = m.end()
            if body.startswith("\r\n", end):
                end += 2
            elif body.startswith("\n", end):
                end += 1
            else:
                raise DataError(f"{where}: benchmarks:{name} must be followed by a line break")
            open_ = (name, end)
        else:
            if not open_ or open_[0] != name:
                raise DataError(f"{where}: /benchmarks:{name} closes no open block")
            found.append((name, open_[1], m.start()))
            open_ = None
    if open_:
        raise DataError(f"{where}: benchmarks:{open_[0]} is never closed")
    return found


def alt_pattern(stem):
    return re.compile(r'(<img alt=")([^"]*)(" src="benchmarks/charts/' + re.escape(stem) + r'\.svg">)')


def rewrite_readme(body, blocks, names, alts, where):
    """The README text with each named block and alt text regenerated; line endings follow the file. Returns the new
    text and the names of the blocks and alt texts that changed."""
    nl = "\r\n" if "\r\n" in body else "\n"
    spans = split_blocks(body, where)
    present = [name for name, _, _ in spans]
    for name in present:
        if name not in names:
            raise DataError(f"{where}: unknown block benchmarks:{name}")
        if present.count(name) > 1:
            raise DataError(f"{where}: block benchmarks:{name} appears more than once")
    for name in names:
        if name not in present:
            raise DataError(f"{where}: no <!-- benchmarks:{name} --> block")
    changed, out, pos = [], [], 0
    for name, start, end in spans:
        new = blocks[name].replace("\n", nl)
        if body[start:end].replace("\r\n", "\n") != blocks[name]:
            changed.append(f"block {name}")
        out += [body[pos:start], new]
        pos = end
    out.append(body[pos:])
    text_ = "".join(out)
    for stem, alt in alts.items():
        pattern = alt_pattern(stem)
        matches = pattern.findall(text_)
        if len(matches) != 1:
            raise DataError(f"{where}: expected one <img alt=... src=\"benchmarks/charts/{stem}.svg\">, found {len(matches)}")
        if matches[0][1] != esc(alt):
            changed.append(f"alt text of {stem}")
        text_ = pattern.sub(lambda m: m.group(1) + esc(alt) + m.group(3), text_)
    return text_, changed


def readme_outputs(data):
    """{README path relative to the root: (current text, regenerated text, changes)} for every README with blocks."""
    blocks, alts, result = readme_blocks(data), readme_alts(data), {}
    for rel, names in README_BLOCKS.items():
        path = os.path.join(ROOT, *rel.split("/"))
        with open(path, encoding="utf-8", newline="") as f:
            body = f.read()
        new, changed = rewrite_readme(body, blocks, names, alts if rel == "README.md" else {}, rel)
        result[rel] = (body, new, changed)
    return result


def check_readme(data):
    """Every published string in the data must appear in the README it was transcribed from."""
    problems = []
    for key, st in data["sets"].items():
        readme = os.path.join(ROOT, "samples", "WasmHost", "README.md") if key == "wasm" else os.path.join(ROOT, "README.md")
        with open(readme, encoding="utf-8") as f:
            body = f.read()
        for s in st["series"]:
            pubs = [p["published"] for p in s.get("points", []) if "published" in p]
            if isinstance(s.get("published"), str):
                pubs.append(s["published"])
            elif isinstance(s.get("published"), dict):
                pubs += list(s["published"].values())
            for pub in pubs:
                if pub not in body:
                    problems.append(f"{key}/{s['id']}: {pub!r} is not in {os.path.relpath(readme, ROOT)}")
    # The NuGet README repeats the headline table (a generated block) and quotes the Kestrel TCP figure.
    nuget = os.path.join(ROOT, "Json-Rpc", "README.md")
    with open(nuget, encoding="utf-8") as f:
        body = f.read()
    wanted = [(f"headline/{s['id']}", v) for s in data["sets"]["headline"]["series"] for v in (s["published"], s.get("against")) if v]
    wanted += [(f"kestrel/{s['id']}", s["published"]) for s in data["sets"]["kestrel"]["series"] if s["id"] == "ours-tcp"]
    for where, pub in wanted:
        if pub not in body:
            problems.append(f"{where}: {pub!r} is not in Json-Rpc/README.md")
    return problems


def check_outputs(outputs):
    problems = []
    for name, content in outputs.items():
        path = os.path.join(HERE, name)
        if not os.path.exists(path):
            problems.append(f"{name}: missing")
            continue
        with open(path, encoding="utf-8", newline="") as f:
            if f.read().replace("\r\n", "\n") != content:
                problems.append(f"{name}: differs from the rendered output")
    return problems


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--check", action="store_true", help="render in memory and fail if any committed output or README figure disagrees")
    args = ap.parse_args(argv)
    try:
        data = load()
        outputs = render_all(data)
        for name, content in outputs.items():
            if name.endswith(".svg"):
                xml.dom.minidom.parseString(content)
        readmes = readme_outputs(data)
    except (DataError, KeyError, ValueError, OSError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    problems = []
    if args.check:
        problems += check_outputs(outputs)
        problems += [f"{rel}: {c} differs from the data" for rel, (_, _, changed) in readmes.items() for c in changed]
    else:
        for name, content in outputs.items():
            with open(os.path.join(HERE, name), "w", encoding="utf-8", newline="\n") as f:
                f.write(content)
            print("wrote", os.path.relpath(os.path.join(HERE, name), ROOT))
        for rel, (old, new, changed) in readmes.items():
            if new != old:
                with open(os.path.join(ROOT, *rel.split("/")), "w", encoding="utf-8", newline="") as f:
                    f.write(new)
                print("wrote", rel, "(" + ", ".join(changed) + ")")
    # After the README tables are rewritten, so a published figure that only a generated table carries is found.
    problems = check_readme(data) + problems
    for p in problems:
        print("check:", p, file=sys.stderr)
    if not problems:
        print(f"data revision {data['revision']}: all outputs and README figures agree" if args.check else f"data revision {data['revision']}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
