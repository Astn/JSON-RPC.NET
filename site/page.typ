// One documentation page. Typst's HTML export renders a Markdown file (through the cmarker
// package) inside this shell; site/build.py drives the compiler and finishes the page
// (heading anchors, table of contents, link resolution, syntax highlighting).
//
// Inputs (all strings, passed with `--input name=value`):
//   source       repo-relative path of the Markdown file to render
//   title        the page title
//   description  one sentence for the meta description
//   pages        JSON array of { "href", "label", "current" } for the page list
#import "@preview/cmarker:0.1.6"

#let source = sys.inputs.at("source")
#let title = sys.inputs.at("title")
#let description = sys.inputs.at("description", default: "")
#let pages = json(bytes(sys.inputs.at("pages", default: "[]")))
#let repo = "https://github.com/Astn/JSON-RPC.NET"

#set document(title: title)

// Images become plain <img> elements; Typst would otherwise try to load the file.
#let himg(src, alt: none) = html.elem("img", attrs: (
  src: src,
  alt: if alt == none { "" } else { alt },
))

// Code blocks keep their text and language; build.py highlights them.
#show raw.where(block: true): it => html.elem(
  "pre",
  html.elem("code", attrs: (class: "language-" + (if it.lang == none { "text" } else { it.lang })), it.text),
)

#let content = cmarker.render(
  read("/" + source),
  scope: (image: himg),
  html: (
    img: ("void", (attrs) => himg(attrs.at("src", default: ""), alt: attrs.at("alt", default: ""))),
    picture: (attrs, body) => html.elem("picture", body),
    source: ("void", (attrs) => html.elem("source", attrs: attrs)),
  ),
)

#let page-link(p) = {
  let attrs = (href: p.href)
  if p.current { attrs.insert("class", "current"); attrs.insert("aria-current", "page") }
  html.elem("a", attrs: attrs, p.label)
}

#html.elem("html", attrs: (lang: "en"), {
  html.elem("head", {
    html.elem("meta", attrs: (charset: "utf-8"))
    html.elem("meta", attrs: (name: "viewport", content: "width=device-width, initial-scale=1"))
    html.elem("meta", attrs: (name: "color-scheme", content: "light dark"))
    html.elem("title", title)
    if description != "" { html.elem("meta", attrs: (name: "description", content: description)) }
    html.elem("link", attrs: (rel: "stylesheet", href: "style.css"))
    html.elem("link", attrs: (rel: "stylesheet", href: "highlight.css"))
  })
  html.elem("body", {
    html.elem("a", attrs: (class: "skip", href: "#content"), "Skip to content")
    html.elem("header", attrs: (class: "top"), {
      html.elem("a", attrs: (class: "brand", href: "index.html"), "JSON-RPC.NET")
      html.elem("nav", attrs: ("aria-label": "Project"), {
        html.elem("a", attrs: (href: repo), "GitHub")
        html.elem("a", attrs: (href: "https://www.nuget.org/packages/AustinHarris.JsonRpc"), "NuGet")
        html.elem("a", attrs: (href: repo + "/releases"), "Releases")
      })
    })
    html.elem("div", attrs: (class: "layout"), {
      html.elem("nav", attrs: (class: "pages", "aria-label": "Pages"), for p in pages { page-link(p) })
      html.elem("main", attrs: (id: "content"), content)
      html.elem("nav", attrs: (class: "toc", id: "toc", "aria-label": "On this page"), none)
    })
    html.elem("footer", {
      [JSON-RPC.NET is MIT licensed. Source, issues and releases live on ]
      html.elem("a", attrs: (href: repo), "GitHub")
      [. This page is built from ]
      html.elem("a", attrs: (href: repo + "/blob/master/" + source), source)
      [.]
    })
  })
})
