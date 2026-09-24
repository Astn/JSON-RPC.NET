<!-- Written by the Codex gpt-6-astra model on 2026-09-23 from a read-only review of branch finish-netstandard-upgrade at 192a97f plus the uncommitted chart revision; brief: scratchpad astra_charts_prompt.txt. -->

# Chart design review for JSON-RPC.NET

## 1. Summary

- Replace categorical bars with log-scaled interval plots: show both endpoints of every published range.
- Use line charts for measured thread and connection sweeps, with visible uncertainty at each sampled count.
- Separate network comparisons from direct-call, pipe, and sequential-proxy measurements.
- Preserve existing README ranges when adding the sweep; its current single observations cannot replace repeated-run evidence.
- Keep library colours consistent, distinguish settings with shapes and dashes, and generate explicit light/dark variants.
- GitHub README images cannot provide tooltips or toggles; a linked HTML explorer becomes worthwhile with the eight-series sweep.
- Make one versioned data file authoritative for tables, SVGs, and HTML; fix precision, validation, and measurement captions first.

## 2. What the current charts get wrong

### Review basis

I read the README benchmark section and subsequent content, both requested benchmark implementations, the three original SVGs as text, and `render.py`.
I also read the WebAssembly results, the gRPC client implementation, and relevant `BenchmarkRunner` calculations.
The review is advice only; I did not modify files or run the benchmark or renderer.

The branch was `finish-netstandard-upgrade`, with HEAD at `192a97f`.
The eight inspected commits cover the chart introduction, comparison harness, performance pass, gRPC addition, and refreshed measurements.
Chroma queries returned benchmark context and older documentation, but no relevant chart-design decision that supersedes the repository evidence.

The working tree changed during this review.
Initially, the renderer produced linear bars; subsequently, revised charts, a revised renderer, and `sweep.json` appeared.
I read the revised renderer and sweep as well.
Findings below distinguish the original design from the working-tree revision; they are grounded in source and SVG geometry, not a browser visual inspection.

### Design and interpretation problems

1. **The original linear bars suppress the slower paths.**
   Their plot area is only 480 pixels wide.
   The comparison SVG gives gRPC approximately 6.6–6.9 pixels, the pipe row 4.4 pixels, and the sequential proxy 3.3 pixels.
   The original Kestrel HTTP-single bar is only 2.3 pixels wide.

2. **The original marks do not encode the ranges.**
   Bar lengths represent `(low + high) / 2`; only text preserves the endpoints.
   That midpoint is not necessarily an observed result, mean, or median.
   Two-thread throughput spans 7.6–9.5 M, an important difference that should be visible geometrically.

3. **The revised logarithmic bars improve visibility but retain an unsuitable length encoding.**
   `log_bars()` draws from an arbitrary positive axis minimum to a geometric midpoint.
   Those lengths cannot be interpreted as throughput ratios.
   Position a capped interval on the logarithmic axis instead.

4. **Measurement boundaries are mixed.**
   Direct byte processing, pipelined pipes, sequential typed proxies, raw TCP clients, and Grpc.Net.Client exercise different amounts of work.
   Grouping primarily by library does not resolve that distinction.
   Put network results together and in-process context in a separate panel.

5. **The captions overgeneralise concurrency.**
   The revised sweep subtitle says every connection has 256 requests in flight.
   TCP and gRPC use that pipeline setting; HTTP clients await each POST, containing either one RPC or 100 RPCs.
   Label the x-axis “client connections / gRPC channels,” never “threads,” for the network sweep.

6. **The new sweep is a different measurement set.**
   It currently contains eight network series, five connection counts, and one two-second observation per cell after warm-up.
   It does not sweep the direct-call or StreamJsonRpc in-process paths.
   Its 16-connection TCP result is 11.58 M, versus the README comparison range of 13.7–14.6 M.
   Its unary gRPC result is 404 k, versus 192–198 k.
   Preserve these as separate sessions; do not combine them into an unexplained range or silently substitute one for another.

7. **The revision loses published information.**
   It overwrites `kestrel-transports.svg` with single-run sweep lines, removing the original transport ranges.
   Its `fmt()` also changes 13.7–14.6 M into “14 M to 15 M” and 30.6–35.8 M into “31 M to 36 M.”
   These are material losses in a chart intended to communicate ranges.

8. **Thread cost is not request latency.**
   The harness computes `threads × 1e9 / aggregate_RPC_per_second`.
   This is average processing time per worker, not a sampled network latency distribution.
   The revised comment calls all supplied nanosecond values fastest-run costs, but at two threads 9.5 M implies about 211 ns, not the published 222 ns.

9. **Layout and theme treatment need explicit design.**
   Long subtitles and settings are single SVG text elements, with fixed margins and no wrapping.
   The revised eight-series chart leaves approximately 500 pixels for the plot and uses circles for every setting.
   White cards remain readable in dark mode, but they do not provide the requested dark-theme presentation.

## 3. Recommended chart set

Keep three immediate README views: library comparison, transport comparison, and thread scaling.
Add the connection sweep as a fourth view.
Place in-process detail in an expandable section and WebAssembly detail in the sample README.

Across all views, “range” means the observed minimum–maximum over the stated runs, not a confidence interval.
Every interval gets full-opacity endpoint caps and a readable numeric range where space permits.
A published singleton gets a marker and a caption explaining that a range was not supplied.
Do not manufacture interval width to make a narrow range visible; preserve the endpoints in adjacent text.

Use the revised renderer’s Okabe–Ito family colours at full opacity:

| Meaning | Colour | Contrast on white | Contrast on `#0d1117` |
|---|---|---:|---:|
| JSON-RPC.Net | `#009e73` | 3.42:1 | 5.53:1 |
| StreamJsonRpc | `#0072b2` | 5.19:1 | 3.65:1 |
| gRPC for .NET | `#d55e00` | 3.87:1 | 4.89:1 |
| Context/reference marks | `#767676` | 4.54:1 | 4.17:1 |

These calculated contrasts apply to solid marks against the backgrounds, not translucent fills.
Use neutral theme-specific text for labels; the series colours are not all suitable for small text.
Shapes, dashes, panel titles, and direct labels make identification independent of colour.
Keep essential strokes at least 2 pixels at the intended display size.
The 3:1 graphical contrast target follows [W3C guidance](https://www.w3.org/WAI/WCAG21/understanding/non-text-contrast.html).

All sketches below describe layout; character positions are not calibrated measurements.

### 3.1. Library comparison at 16 connections

- **Purpose:** compare the measured network configurations while exposing framing, formatter, and client differences.
- **Data:** the six network rows in the README comparison table; exclude its three in-process rows.
- **Chart type:** horizontal capped interval plot, with a separate labelled gRPC subgroup.
- **Axes and scale:** categorical configurations vertically; RPC/s horizontally on a logarithmic axis, approximately 100 k–20 M.
- **Range encoding:** a segment from low to high, caps at both ends, and an aligned numeric column; 625 k is a singleton marker.
- **Series and colours:** green JSON-RPC.Net, blue StreamJsonRpc, vermilion gRPC; settings appear in row labels.
- **Caption:** five small calls, loopback, 16 connections/channels; TCP/gRPC pipeline 256; gRPC includes its .NET client on the server’s machine.
- Preserve the comparison corpus’s `jsonrpc` member and identify JSON-RPC.Net’s actual configured serializer; a StreamJsonRpc formatter name does not establish a shared serializer.

```text
Network configuration                 RPC/s, logarithmic       Reported range
JSON-RPC.Net TCP, raw documents                         |---|   13.7–14.6 M
StreamJsonRpc TCP, newline + STJ             |--|               1.38–1.44 M
StreamJsonRpc TCP, Content-Length + STJ      |--|               1.41–1.45 M
StreamJsonRpc TCP, Content-Length + Json.NET o                  625 k
gRPC: .NET client and service share the machine
  Unary                              |-|                      192–198 k
  Bidirectional stream               |-|                      200–209 k
                                  100 k    1 M    10 M  20 M
```

Use stable configuration order rather than repeatedly sorting small, overlapping differences.
The two StreamJsonRpc STJ ranges overlap; the graphic should not imply a decisive framing winner.

### 3.2. JSON-RPC.Net transport comparison

- **Purpose:** show the effect of transport and batching on the existing measured workload.
- **Data:** all four rows of the README Kestrel table, retaining its own session and request corpus.
- **Chart type:** horizontal capped interval plot, with the in-process reference separated by a rule.
- **Axes and scale:** transport vertically; logarithmic RPC/s horizontally, approximately 100 k–50 M.
- **Range encoding:** show all four low/high segments and their exact published labels.
- **Series and colours:** green network rows; neutral grey in-process reference.
- **Caption:** 16 HTTP clients or TCP connections; HTTP is one outstanding POST per client; TCP pipeline is 256.

```text
Transport                                      RPC/s, logarithmic
HTTP, one RPC per POST       |---|                   128–168 k
HTTP, 100 RPCs per POST                         |--| 12.7–13.7 M
TCP, 256 outstanding per connection               || 15.2–15.5 M
----------------------------------------------------------------
Reference: direct calls, 16 threads                    || 30.8–31.3 M
                          100 k      1 M      10 M      50 M
```

Label the unit as RPC/s, so a POST containing 100 RPCs contributes 100 operations.
Do not connect these categorical rows with a line.
Keep this snapshot when adding the sweep; they answer different questions.

### 3.3. Library-only scaling by worker threads

- **Purpose:** reveal aggregate throughput growth and the accompanying increase in per-worker processing cost.
- **Data:** the five README sync ranges at 1, 2, 4, 8, and 16 threads, plus the five published nanosecond figures.
- **Chart type:** two vertically aligned panels: throughput range envelope above, reported cost points below.
- **Axes and scale:** shared x-axis at 1, 2, 4, 8, 16, explicitly labelled “worker threads; each step doubles.”
- Use log2 x-positioning; use linear y-axes of approximately 0–40 M RPC/s and 0–600 ns respectively.
- **Range encoding:** throughput has capped vertical intervals and a lightly filled envelope bounded by visible low/high lines.
- Join adjacent range boundaries with straight segments; do not smooth or introduce an unlabelled midpoint line.
- The nanosecond figures have no supplied ranges: show them as individual reported values and say so.
- **Series and colours:** green throughput and cost marks; neutral annotation at eight physical cores.
- Treat 16 workers as exceeding physical core count; this benchmark does not establish exact per-core thread placement.

```text
Aggregate RPC/s; higher is better
40 M |                              upper boundary
30 M |                         |    |===|
20 M |                  |===|  |===|
10 M |           |===|
 0 M |    ||
     +-----1------2------4------8------16

Reported ns/RPC per worker; lower is better
600  |
400  |                                o
200  |     o      o      o      o
  0  +-----1------2------4------8------16
          217    222    230    298    446
```

Do not describe the nanosecond points as medians or fastest observations without recovering their provenance.
If costs are later derived from each throughput interval, transform endpoints as `[threads × 1e9 / high, threads × 1e9 / low]` and label them derived.
For example, the current 16-thread throughput implies approximately 447–523 ns per worker.
Keep the published scalar figures distinguishable from that derived range.

### 3.4. Network scaling by connections

- **Purpose:** show where each library/configuration scales, plateaus, or declines as client concurrency changes.
- **Data:** the eight network series in `sweep.json`, at exactly its five recorded connection counts.
- **Chart type:** three stacked line-chart panels, grouped by library, with identical axes and plot widths.
- **Axes and scale:** log2 x-axis at 1, 2, 4, 8, 16; shared logarithmic RPC/s y-axis, currently approximately 10 k–20 M.
- **Range encoding now:** one marker per observed value; explicitly state “one measured run per point; range unavailable.”
- **Range encoding after repeats:** median line through actual samples, with capped minimum–maximum whiskers at every point.
- Prefer whiskers over eight overlapping translucent bands; retain numeric low/high values in the accompanying table.
- **Series and colours:** retain family colours and use the configuration encodings below.
- Keep all configurations visible in the static panels; arbitrary selections belong in the HTML explorer.

| Family | Solid + circle | Long dash + square | Dotted + triangle |
|---|---|---|---|
| JSON-RPC.Net | TCP | HTTP batch 100 | HTTP single |
| StreamJsonRpc | Newline + STJ | Content-Length + STJ | Content-Length + Json.NET |
| gRPC | Unary | Bidirectional stream | — |

```text
Shared logarithmic RPC/s scale; layout only
JSON-RPC.Net      o------o------o------o------o   TCP
                 s------s------s------s------s   HTTP batch
                 ^------^------^------^------^   HTTP single
StreamJsonRpc    o------o------o------o------o   Newline + STJ
                 s------s------s------s------s   Header + STJ
                 ^------^------^------^------^   Header + Json.NET
gRPC             o------o------o------o------o   Unary
                 s------s------s------s------s   Stream
                 1      2      4      8      16 connections/channels
Future repeated points: capped vertical whisker through each median marker.
```

Use the actual observed shapes, including decreases; never force monotonic curves.
Do not infer CPU thread counts from connections, or fabricate in-process sweep lines from fixed comparison rows.
The caption must list TCP/gRPC pipeline 256, HTTP one POST outstanding, and batch size 100 separately.

### 3.5. In-process execution context

- **Purpose:** explain the overhead included in three different in-process calling paths.
- **Data:** the comparison table’s direct-call 2.6–3.6 M, pipe 117–142 k, and sequential-proxy 96–97 k rows.
- **Chart type:** separate horizontal interval plot under “In-process paths: different execution boundaries.”
- **Axes and scale:** path vertically; logarithmic RPC/s horizontally, approximately 50 k–5 M.
- **Range encoding:** capped low/high segments and numeric endpoint labels for all three rows.
- **Series and colours:** green direct-call row; blue pipe/proxy rows, distinguished by complete labels.
- Place this in an expandable detail section; its ranges remain visible whenever expanded.

```text
Direct bytes, one worker                         |-------|  2.6–3.6 M
Pipe pair, one client, 256 outstanding       |----|          117–142 k
Typed proxy, sequential await             ||                96–97 k
                                      50 k  100 k   1 M    5 M
```

Do not draw this as a continuation of the network sweep or call it an equal-feature implementation comparison.
The comparison direct-call range also remains distinct from the sync table’s newer 4.5–4.6 M range.

### 3.6. WebAssembly interpreter versus AOT

- **Purpose:** expose the effect of compilation and interop path within the browser workload.
- **Data:** all eleven rows in `samples/WasmHost/README.md`, using its operations/RPC-per-second columns.
- **Chart type:** paired horizontal dot plot, grouped into plain interop, single JSON-RPC, batched JSON-RPC, and internal .NET loop.
- **Axes and scale:** path vertically; logarithmic logical operations/s horizontally, approximately 1 k–10 M.
- **Range encoding now:** separate interpreter and AOT markers; no uncertainty intervals are available from the published table.
- State that AOT reports the better of two runs; the distance between interpreter and AOT is a deployment difference, not a low/high range.
- **Range encoding after repeats:** one capped interval per compilation mode, with its own sample count and summary policy.
- **Series and colours:** green JSON-RPC paths, neutral plain-interop references; circle for interpreter and diamond for AOT.
- Keep this chart in the sample README and link from the main README.

```text
Path                             Operations/s, logarithmic
Plain invokeMethod Add                o------D
Plain typed JSExport Add                                  oD
JSON-RPC invokeMethod Process      o---------D
JSON-RPC UTF-8 ProcessBytes            o-----------D
JSON-RPC UTF-8 batch 100                 o------------D
                                     o interpreter; D AOT
```

Use per-RPC throughput for batches, not calls-per-second or microseconds per batch.
Do not merge these `add(1, 2)` measurements into the server’s five-call comparison.

## 4. GitHub constraints and the interactive option

**Tooltips and toggles cannot work inside the README’s SVG images.**
GitHub’s image presentation does not expose the SVG as an interactive document.
Scripts, SVG-internal links, CSS hover interactions, and `<title>` tooltips cannot supply missing chart information.
External fonts and images are unavailable; SMIL/CSS animation can run, but does not enable interaction.
These restrictions are consistent with the [W3C SVG Integration draft’s secure animated image model](https://www.w3.org/TR/svg-integration/).

Generate explicit light and dark SVG variants from the same data and geometry.
Use white with `#1f2328` primary text for light mode; use `#0d1117` with `#f0f6fc` text for dark mode.
Use sufficiently contrasting secondary text in each theme, rather than reusing the light-theme muted colour.
GitHub supports selection through `<picture>` and `prefers-color-scheme`. [GitHub documentation](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/quickstart-for-writing-on-github)

Keep captions, range definitions, sample counts, and setting names visible without hover.
Provide descriptive image alt text and retain the numeric tables.
Do not animate benchmark values or alternate configurations over time.

A linked, dependency-free HTML explorer is worthwhile once the sweep is retained and repeated.
It should use the same datasets and uncertainty rules as the static charts, with inline SVG and vanilla JavaScript:

- Toggle individual configurations or isolate two curves while preserving their low/high whiskers.
- Inspect exact values, low/high, sample count, run duration, and measurement metadata through pointer, keyboard focus, or tap.
- Select a connection count and compare all corresponding configurations without substituting older session values.
- Switch between logarithmic and linear axes, preserving endpoints and clearly updating the scale label.
- Keep axes fixed during ordinary series toggling; make “fit selected series” an explicit action.
- Expose an accessible data table and a download of the underlying data, including missing-range flags.

Host it on GitHub Pages and provide a downloadable self-contained HTML file.
A GitHub repository HTML preview is not the interactive page.
The static README must remain complete when the explorer is never opened.

## 5. Data pipeline

Use one canonical, versioned file such as `benchmarks/charts/benchmarks.json`.
It should contain separate measurement sets for sync, Kestrel snapshot, comparison snapshot, network sweep, and WebAssembly.
Each set keeps its own workload, timing, versions, and summary policy.
All outputs consume those sets without pooling incompatible runs.

```text
Harness exports + faithfully transcribed published intervals
                           |
                           v
                 benchmarks.json
                           |
                  validate + summarise
                           |
            +--------------+--------------+
            v              v              v
      README tables   light/dark SVGs   self-contained HTML
      exact endpoints same endpoints   embedded same dataset
```

The canonical format needs these fields:

| Level | Required information |
|---|---|
| File | Schema version and stable measurement-set IDs |
| Measurement set | Date, source commit/dirty state, machine, runtime, GC, workload/corpus identity |
| Series | Stable ID, library, transport, framing, serializer, client implementation, batch and pipeline settings |
| Point | Thread/connection count, reported value or summary, low/high, sample count, range status |
| Raw run, when available | Completed RPC count, actual elapsed seconds, warm-up, repeat/session ID |
| Provenance | Published precision, aggregation policy, source location, and any unknown fields |

Distinguish “observed range,” “single observation,” “published value only,” and “best of two.”
Equal displayed endpoints do not prove zero variation.
Do not infer the two original observations from rounded README endpoints.
For historical ranges with unknown sample counts, keep the count unknown.

Adapt the present sweep format explicitly:
`connections[i]` pairs with `series[j].rpcPerSec[i]`, producing a point with one observation and unavailable repeat variability.
Add raw repetitions before calculating minimum, maximum, and median.
Recommend five measured repeats per cell, retaining warm-up and rotating configuration order across repeats.
Report those extrema as observed spread, not statistical confidence.

Preserve the README’s original endpoint precision during the initial transcription.
Generate its tables from the same canonical data thereafter, or make renderer check mode verify them.
Keep different workload/session IDs even when their dates and display names match.

For HTML, embed the validated JSON during generation so opening the downloaded file does not require `fetch()` or a local server.
Escape `<` in embedded JSON to prevent an accidental `</script>` terminator.
Have JavaScript use the already computed summaries, rather than implementing a second aggregation policy.
Include the same data revision identifier in HTML and SVG metadata.

## 6. Review of `benchmarks/charts/render.py` as code

The dependency-free approach is appropriate.
The revised `Linear`, `Log`, and chart functions provide a useful starting point.
The following changes apply to the working-tree revision inspected during this review.

1. **Fix value formatting before publishing revised charts.**
   `fmt()` rounds all values at or above 10 M to whole millions.
   Separate tick formatting from observation formatting, preserving source precision for low/high labels.
   Require 13.7–14.6 M and 30.6–35.8 M to survive unchanged.
   `fmt_range()` must not fall back to millions for a small sub-million interval that rounds identically.

2. **Replace `log_bars()` with `interval_chart()`.**
   Remove the background-to-midpoint rectangle and geometric midpoint calculation.
   Draw the actual low/high coordinates, caps, and singleton markers.
   Use a fixed numeric label column so a high endpoint cannot push its text beyond the canvas.

3. **Make centre statistics explicit in `line_chart()`.**
   It currently derives arithmetic midpoints from every interval.
   Accept an optional recorded median/value separately from low/high.
   For historical sync ranges, draw boundaries and capped intervals without asserting an observed centre.
   For single sweep observations, show markers and “range unavailable.”

4. **Preserve measurement-set identity and captions.**
   Do not overwrite the Kestrel range snapshot with a sweep chart under the same filename.
   Generate titles, concurrency descriptions, and sample-count notes from per-series metadata.
   The global pipeline value must not be presented as HTTP concurrency.

5. **Validate before constructing geometry.**
   Reject non-finite numbers, reversed ranges, non-positive log values, unknown kinds, duplicate IDs, and mismatched array lengths.
   Handle empty datasets, one x-value, and constant log domains explicitly.
   Current `zip()` calls can silently discard unmatched values; current x-positioning divides by `len(xs) - 1`.

6. **Make axis semantics and domains deliberate.**
   Equal index spacing is valid for this doubling sweep only when labelled accordingly.
   Implement log2 x-positioning from numeric counts and reusable 1–2–5 logarithmic ticks.
   Fit domains to all range endpoints with headroom; avoid extending a 14.6 M maximum unnecessarily to 100 M.
   Remove the redundant conditional around `ys.hi` in reference-label placement.

7. **Use stable style IDs rather than input order.**
   The current dash lookup changes identity when series order changes and fails if another setting exceeds its list.
   Map each series ID to colour, dash, and marker.
   Provide distinct marker shapes; preserve range caps independently of the central-line dash pattern.

8. **Centralise text and SVG construction.**
   `esc()` is only applied to some fields; titles, subtitles, annotations, and reference labels remain interpolated.
   Use `xml.etree.ElementTree`, or one consistently applied text/attribute escaping layer.
   Add `<title>` and `<desc>` for standalone accessibility, without promising README hover behaviour.
   Give clip paths unique IDs so multiple SVGs can coexist inline in the HTML explorer.

9. **Separate layout, themes, and rendering from file writes.**
   Return SVG text from pure rendering functions; place writing under `main()` and an `if __name__ == "__main__"` guard.
   Use standard-library `json`, `math`, `pathlib`, `argparse`, and XML utilities only.
   Add explicit line breaks or `tspan` wrapping, reserve space for range labels, and use theme backgrounds for marker fills.
   Keep range boundaries opaque even when their optional connecting envelope is translucent.

10. **Make generation reproducible and failure visible.**
    Add `--check` to render in memory and compare with committed outputs.
    Missing sweep data should fail when sweep outputs are requested, rather than silently leave stale committed charts.
    Use deterministic ordering, UTF-8, stable coordinate formatting, and no render-time timestamps.
    Add focused standard-library checks for endpoint preservation, invalid data, XML validity, and static/HTML summary agreement.
    Visually verify both themes at typical README width and narrow-screen width before accepting the eventual implementation.

## 7. Needs-decision items

- **needs decision: interval plots vs logarithmic bars, recommended interval plots because position represents the README snapshot values honestly and both low/high endpoints remain explicit.**

- **needs decision: one eight-series sweep panel vs three coordinated panels, recommended three panels because all eight series remain available with readable settings and per-point ranges on shared axes.**

- **needs decision: single-run sweep publication vs repeated sweep publication, recommended five repeats per cell before headline publication because the sweep needs observed low/high values; label the existing data provisional with range unavailable.**

- **needs decision: replacing snapshots with the sweep vs retaining both, recommended retaining both because the current sweep is a separate session and cannot preserve the README’s historical ranges by substitution.**

- **needs decision: midpoint curves vs evidence-backed summaries, recommended range boundaries for historical sync data and median curves for future raw repeats because neither arithmetic nor geometric midpoint establishes a measured typical result.**

- **needs decision: cost annotations vs a separate cost panel, recommended a separate panel because published nanosecond scalars have no supplied ranges and should not be mistaken for throughput-derived latency statistics.**

- **needs decision: static-only presentation vs a linked explorer, recommended a linked explorer after the static redesign because selecting settings and inspecting exact per-connection ranges adds value that GitHub images cannot provide.**

- **needs decision: one white SVG vs light/dark variants, recommended variants because the same data, scales, colours, and opaque range caps can remain readable while text and backgrounds match GitHub’s themes.**

- **needs decision: Python literals plus sweep JSON vs one canonical data file, recommended one versioned file because every table, static interval, and interactive tooltip must report the same endpoints and provenance.**