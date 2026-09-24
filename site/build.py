#!/usr/bin/env python3
"""Build the documentation site for GitHub Pages.

Each page is a Markdown file that already lives in the repository (the README, the serializer
guide, the package READMEs, the sample and benchmark READMEs). Typst's HTML export renders it
through the cmarker package inside site/page.typ; this script drives the compiler and finishes
what the export leaves out:

- GitHub-compatible heading ids, so the anchors that work on github.com work on the site;
- a table of contents per page;
- relative links: a link to another page becomes a site link, a link to a file becomes a copy
  of that file under its repository path, a link to a Markdown file or a directory that is not
  a page goes to GitHub;
- syntax highlighting with Pygments;
- the benchmark charts, data and the interactive explorer, copied under benchmarks/charts/.

    python site/build.py --out site/_out
    python -m http.server --directory site/_out 8000

The Pages workflow runs the same command; a page that fails to compile fails the deploy.
"""
from __future__ import annotations

import argparse
import html
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

from pygments import highlight
from pygments.formatters import HtmlFormatter
from pygments.lexers import get_lexer_by_name
from pygments.util import ClassNotFound

ROOT = Path(__file__).resolve().parent.parent
REPO = "https://github.com/Astn/JSON-RPC.NET"
BRANCH = "master"
TYPST_VERSION = "0.14"

# (output file, source Markdown, page title, label in the page list, group, description)
PAGES = [
    ("index.html", "README.md", "JSON-RPC.NET", "Overview", "Guide",
     "A high performance JSON-RPC 2.0 server for .NET: bytes in, bytes out, pluggable serializers, Kestrel hosting."),
    ("serializers.html", "docs/serializers.md", "Serializers", "Serializers", "Guide",
     "How the built-in, Json.NET and System.Text.Json serializers differ, and how to configure or write one."),
    ("aspnetcore.html", "AustinHarris.JsonRpc.AspNetCore/README.md", "ASP.NET Core hosting", "ASP.NET Core hosting", "Packages",
     "Host JSON-RPC.NET in Kestrel: HTTP endpoint, raw TCP, Unix socket and named pipe connections, DI registration."),
    ("newtonsoft.html", "AustinHarris.JsonRpc.Newtonsoft/README.md", "Json.NET serializer", "Json.NET serializer", "Packages",
     "The Json.NET serializer package: settings, attributes, converters and lenient input."),
    ("systemtextjson.html", "AustinHarris.JsonRpc.SystemTextJson/README.md", "System.Text.Json serializer", "System.Text.Json serializer", "Packages",
     "The System.Text.Json serializer package: options, source generation and UTF-8 readers on the request bytes."),
    ("wasm.html", "samples/WasmHost/README.md", "WebAssembly sample", "WebAssembly sample", "More",
     "The server running inside the browser as Blazor WebAssembly, with a benchmark against plain interop."),
    ("micro.html", "benchmarks/Micro/README.md", "Micro-benchmarks", "Micro-benchmarks", "More",
     "BenchmarkDotNet timings of one request per shape, with allocation columns."),
]
EXPLORER = ("benchmarks/charts/explorer.html", "Benchmark explorer", "More")
CHARTS = ROOT / "benchmarks" / "charts"

HEADING = re.compile(r"<h([1-6])(?: id=\"[^\"]*\")?>(.*?)</h\1>", re.DOTALL)
CODE = re.compile(r"<pre><code class=\"language-([A-Za-z0-9_+-]+)\">(.*?)</code></pre>", re.DOTALL)
ATTR = re.compile(r"\b(href|src|srcset)=\"([^\"]*)\"")
TAGS = re.compile(r"<[^>]+>")
LEXERS = {"csharp": "csharp", "cs": "csharp", "bash": "bash", "sh": "bash", "powershell": "powershell",
          "json": "json", "xml": "xml", "html": "html", "csv": "text", "text": "text", "": "text",
          "proto": "protobuf", "yaml": "yaml", "typ": "text"}


def slug(text: str) -> str:
    """GitHub's heading anchor: lowercase, drop punctuation, spaces to hyphens."""
    text = html.unescape(TAGS.sub("", text)).strip().lower()
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


def run_typst(source: str, title: str, description: str, pages_json: str, out: Path) -> None:
    cmd = ["typst", "compile", "--features", "html", "--format", "html", "--root", str(ROOT),
           "--input", f"source={source}", "--input", f"title={title}",
           "--input", f"description={description}", "--input", f"pages={pages_json}",
           str(ROOT / "site" / "page.typ"), str(out)]
    result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    # The export prints a standing "under development" warning; only real diagnostics matter.
    noise = ("html export is under active development", "= hint:", "its behaviour may change", "do not rely on this",
             "see https://github.com/typst/typst/issues/5512")
    lines = [l for l in result.stderr.splitlines() if l.strip() and not any(n in l for n in noise)]
    if result.returncode != 0:
        sys.exit(f"typst failed on {source}:\n" + "\n".join(lines))
    for line in lines:
        print(f"  typst: {line}")


def highlight_code(page: str) -> str:
    formatter = HtmlFormatter(nowrap=True)

    def repl(m: re.Match) -> str:
        lang, body = m.group(1), html.unescape(m.group(2))
        name = LEXERS.get(lang.lower(), lang.lower())
        try:
            lexer = get_lexer_by_name(name, stripnl=False)
        except ClassNotFound:
            lexer = get_lexer_by_name("text", stripnl=False)
        code = highlight(body, lexer, formatter).rstrip("\n")
        return f'<pre><code class="language-{lang}">{code}</code></pre>'

    return CODE.sub(repl, page)


def number_headings(page: str) -> tuple[str, list[tuple[int, str, str]]]:
    """Shift levels (Typst emits `#` as h2), add GitHub ids and anchor links, collect the TOC."""
    seen: dict[str, int] = {}
    toc: list[tuple[int, str, str]] = []

    def repl(m: re.Match) -> str:
        level = int(m.group(1)) - 1
        text = m.group(2).strip()
        base = slug(text) or "section"
        n = seen.get(base, 0)
        seen[base] = n + 1
        anchor = base if n == 0 else f"{base}-{n}"
        if 2 <= level <= 3:
            toc.append((level, anchor, html.unescape(TAGS.sub("", text))))
        return (f'<h{level} id="{anchor}">{text}'
                f'<a class="anchor" href="#{anchor}" aria-label="Link to this section">#</a></h{level}>')

    return HEADING.sub(repl, page), toc


def toc_html(toc: list[tuple[int, str, str]]) -> str:
    if len(toc) < 2:
        return ""
    items = [f'<a class="l{level}" href="#{anchor}">{html.escape(text)}</a>' for level, anchor, text in toc]
    return '<p class="toc-title">On this page</p>' + "".join(items)


def resolve_links(page: str, source: str, out_dir: Path, page_map: dict[str, str], anchors: set[str]) -> str:
    """Rewrite relative hrefs and srcs; copy referenced files under their repository path."""
    source_dir = (ROOT / source).parent
    problems: list[str] = []

    def target(value: str) -> str:
        if value.startswith(("http://", "https://", "mailto:", "//", "data:")):
            return value
        if value.startswith("#"):
            if value[1:] not in anchors:
                problems.append(f"anchor {value} not found")
            return value
        path_part, _, fragment = value.partition("#")
        rel = os.path.normpath(os.path.join(source_dir, path_part))
        try:
            repo_rel = Path(rel).resolve().relative_to(ROOT).as_posix()
        except ValueError:
            problems.append(f"link outside the repository: {value}")
            return value
        suffix = f"#{fragment}" if fragment else ""
        if repo_rel in page_map:
            return page_map[repo_rel] + suffix
        full = ROOT / repo_rel
        if full.is_dir():
            return f"{REPO}/tree/{BRANCH}/{repo_rel}"
        if not full.is_file():
            problems.append(f"missing file: {value}")
            return value
        if full.suffix.lower() in (".md", ".cs", ".csproj", ".py", ".proto", ".yml", ".yaml", ".sln", ".razor", ".props",
                                   ".html", ".js", ".css", ".txt"):
            return f"{REPO}/blob/{BRANCH}/{repo_rel}{suffix}"
        dest = out_dir / repo_rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(full, dest)
        return repo_rel + suffix

    def repl(m: re.Match) -> str:
        attr, value = m.group(1), m.group(2)
        if attr == "srcset":
            parts = [p.strip() for p in value.split(",")]
            new = ", ".join(" ".join([target(p.split()[0])] + p.split()[1:]) for p in parts if p)
            return f'{attr}="{new}"'
        return f'{attr}="{target(value)}"'

    # Only the rendered article carries repository-relative links; the shell's own links stay.
    start = page.index('<main id="content">')
    end = page.index("</main>", start)
    page = page[:start] + ATTR.sub(repl, page[start:end]) + page[end:]
    for p in problems:
        print(f"  {source}: {p}")
    return page


def build(out_dir: Path) -> None:
    if shutil.which("typst") is None:
        sys.exit("typst is not on PATH; install Typst 0.14 or newer (https://github.com/typst/typst/releases)")
    out_dir.mkdir(parents=True, exist_ok=True)
    page_map = {source: output for output, source, *_ in PAGES}
    nav_entries = [(output, label, group) for output, _, _, label, group, _ in PAGES]
    nav_entries.append(EXPLORER)

    for output, source, title, label, group, description in PAGES:
        print(f"{source} -> {output}")
        pages_json = json.dumps([
            {"href": href, "label": lbl, "group": group, "current": href == output}
            for href, lbl, group in nav_entries
        ])
        raw = out_dir / (output + ".typst.html")
        run_typst(source, title, description, pages_json, raw)
        page = raw.read_text(encoding="utf-8")
        raw.unlink()
        page, toc = number_headings(page)
        anchors = {anchor for _, anchor, _ in toc} | {
            m.group(1) for m in re.finditer(r'<h[1-6] id="([^"]+)"', page)}
        page = page.replace('<nav class="toc" id="toc" aria-label="On this page"></nav>',
                            f'<nav class="toc" id="toc" aria-label="On this page">{toc_html(toc)}</nav>')
        page = highlight_code(page)
        page = resolve_links(page, source, out_dir, page_map, anchors)
        (out_dir / output).write_text(page, encoding="utf-8")

    shutil.copyfile(ROOT / "site" / "style.css", out_dir / "style.css")
    light = HtmlFormatter(style="default").get_style_defs("pre code")
    dark = HtmlFormatter(style="github-dark").get_style_defs("pre code")
    (out_dir / "highlight.css").write_text(
        f"{light}\n@media (prefers-color-scheme: dark) {{\n{dark}\n}}\n", encoding="utf-8")

    charts_out = out_dir / "benchmarks" / "charts"
    charts_out.mkdir(parents=True, exist_ok=True)
    for f in CHARTS.iterdir():
        if f.suffix in (".svg", ".json") or f.name == "explorer.html":
            shutil.copyfile(f, charts_out / f.name)
    (out_dir / ".nojekyll").write_text("", encoding="utf-8")
    print(f"site written to {out_dir}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--out", default=str(ROOT / "site" / "_out"), help="output directory (default site/_out)")
    args = parser.parse_args()
    build(Path(args.out).resolve())


if __name__ == "__main__":
    main()
