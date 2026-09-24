# Documentation site

The pages at https://astn.github.io/JSON-RPC.NET/ are built from the Markdown files in this repository, so the README on GitHub, the package pages on NuGet and the site never drift apart. Nothing here is written twice.

| Page | Source |
| --- | --- |
| Overview | `README.md` |
| Serializers | `docs/serializers.md` |
| ASP.NET Core hosting | `AustinHarris.JsonRpc.AspNetCore/README.md` |
| Json.NET serializer | `AustinHarris.JsonRpc.Newtonsoft/README.md` |
| System.Text.Json serializer | `AustinHarris.JsonRpc.SystemTextJson/README.md` |
| WebAssembly sample | `samples/WasmHost/README.md` |
| Micro-benchmarks | `benchmarks/Micro/README.md` |
| Benchmark explorer | `benchmarks/charts/explorer.html`, copied as is |

## How it is built

[Typst](https://typst.app) renders each Markdown file to HTML through the [cmarker](https://typst.app/universe/package/cmarker) package inside `page.typ`, which is also the page shell: head, top bar, page list, article, table of contents, footer. Typst's HTML export is still marked experimental upstream, so `build.py` finishes what it leaves out, in plain Python:

- heading ids that match GitHub's anchors, so `#hosting-modes` works on both;
- a table of contents per page from the second- and third-level headings;
- relative links: another page becomes a site link; a file (a chart, the benchmark data) is copied under its repository path; a Markdown file or directory that is not a page links to GitHub;
- syntax highlighting with Pygments (light and dark);
- the charts, data and explorer copied under `benchmarks/charts/`.

`style.css` is the whole design: system fonts, a three-column layout that collapses on narrow screens, colors that follow the system light or dark setting.

## Build locally

Install Typst 0.14 or newer and Pygments (`pip install pygments`), then:

```bash
python site/build.py --out site/_out
python -m http.server --directory site/_out 8000
```

The first build downloads the cmarker package into Typst's package cache. `site/_out` is ignored by git.

## Deploy

`.github/workflows/pages.yml` runs the same build on every push to `master` that touches a documentation file, a chart or this directory, and deploys `site/_out` with GitHub Pages. A page that fails to compile fails the deploy, as does a chart that no longer matches `benchmarks/charts/benchmarks.json`.

## Add a page

Add a row to `PAGES` in `build.py` (output file, source Markdown, title, label, group, one-line description). Links from other Markdown files to that source resolve to the new page automatically.
