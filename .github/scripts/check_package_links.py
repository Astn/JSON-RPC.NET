#!/usr/bin/env python3
"""Checks that a published version of the four packages points at pages that exist.

For each of AustinHarris.JsonRpc, .AspNetCore, .SystemTextJson and .Newtonsoft at the given version, the script
downloads the .nupkg from nuget.org, reads projectUrl, the repository url, releaseNotes and the package README out
of the .nuspec, and requests every absolute http(s) URL found in them, expecting a 2xx. It then builds a scratch
console project against the core package, prints the ObsoleteAttribute on Handler.RegisterFuction,
Handler.UnRegisterFunction and Config.SetBeforeProcessHandler as the published assembly carries it, formats each
UrlFormat with its DiagnosticId, requests the page and asserts the anchor id is in the HTML.

Usage: python3 .github/scripts/check_package_links.py 2.0.1 [--keep]   (exit 1 on any failure)

The check runs against nuget.org, so it is a release step (after the publish workflow has run and the flat
container lists the version, which lags the push by a few minutes), not a pull-request gate.
"""
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

PACKAGES = ["AustinHarris.JsonRpc", "AustinHarris.JsonRpc.AspNetCore", "AustinHarris.JsonRpc.SystemTextJson",
            "AustinHarris.JsonRpc.Newtonsoft"]
FLAT = "https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg"
OBSOLETE = {  # member -> expected diagnostic id
    "Handler.RegisterFuction": "JSONRPC0002",
    "Handler.UnRegisterFunction": "JSONRPC0003",
    "Config.SetBeforeProcessHandler": "JSONRPC0001",
}
URL = re.compile(r"https?://[^\s<>()\"'`\]]+")
# Sample addresses in the READMEs (a host the reader runs) are not links to check.
LOCAL = re.compile(r"^https?://(localhost|127\.0\.0\.1|\[::1\]|[^/]+\.(test|example|localhost)|example\.(com|org|net))(:\d+)?(/|$)", re.I)
HEADERS = {"User-Agent": "JSON-RPC.NET package-link check (+https://github.com/Astn/JSON-RPC.NET)"}

PROGRAM = r"""
using System;
using System.Linq;
using System.Reflection;
using AustinHarris.JsonRpc;

static class Program
{
    static void Main()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        foreach (var m in typeof(Handler).GetMethods(All).Where(m => m.Name == "RegisterFuction" || m.Name == "UnRegisterFunction")
                 .Concat(typeof(Config).GetMethods(All).Where(m => m.Name == "SetBeforeProcessHandler")))
            foreach (var a in m.GetCustomAttributes<ObsoleteAttribute>())
                Console.WriteLine(m.DeclaringType.Name + "." + m.Name + "|" + a.DiagnosticId + "|" + a.UrlFormat + "|" + a.Message);
        Console.WriteLine("assembly|" + typeof(Handler).Assembly.GetName().FullName);
    }
}
"""

PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AustinHarris.JsonRpc" Version="{version}" />
  </ItemGroup>
</Project>
"""

NUGET_CONFIG = """<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"""

failures = []


def fail(message):
    failures.append(message)
    print("FAIL " + message)


def fetch(url, binary=False):
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=60) as r:
        data = r.read()
        return (r.status, data if binary else data.decode("utf-8", "replace"))


def status(url):
    try:
        return fetch(url)[0]
    except urllib.error.HTTPError as e:
        return e.code
    except Exception as e:  # DNS, TLS, timeout
        return str(e)


def text(node):
    return (node.text or "").strip() if node is not None else ""


def strip_trailing(url):
    return url.rstrip(".,;:!?)")


def check_package(package, version, workdir):
    print("== " + package + " " + version)
    url = FLAT.format(id=package.lower(), version=version.lower())
    try:
        code, data = fetch(url, binary=True)
    except urllib.error.HTTPError as e:
        fail("%s: nupkg download %s -> %s" % (package, url, e.code))
        return
    path = os.path.join(workdir, package + "." + version + ".nupkg")
    with open(path, "wb") as f:
        f.write(data)
    z = zipfile.ZipFile(io.BytesIO(data))
    spec_name = [n for n in z.namelist() if n.endswith(".nuspec")][0]
    root = ET.fromstring(z.read(spec_name))
    ns = {"n": root.tag[1:].split("}")[0]} if root.tag.startswith("{") else {}
    meta = root.find("n:metadata", ns) if ns else root.find("metadata")

    def field(name):
        return text(meta.find("n:" + name, ns) if ns else meta.find(name))

    spec_version = field("version")
    if spec_version != version:
        fail("%s: nuspec version %s, expected %s" % (package, spec_version, version))
    project_url = field("projectUrl")
    repo = meta.find("n:repository", ns) if ns else meta.find("repository")
    repo_url = repo.get("url") if repo is not None else ""
    notes = field("releaseNotes")
    readme_name = field("readme")
    if not project_url:
        fail(package + ": no projectUrl")
    if not repo_url:
        fail(package + ": no repository url")
    if not notes:
        fail(package + ": no releaseNotes")
    if not readme_name:
        fail(package + ": no readme")
        readme = ""
    else:
        try:
            readme = z.read(readme_name).decode("utf-8", "replace")
        except KeyError:
            fail("%s: readme %s not in the package" % (package, readme_name))
            readme = ""
    urls = []
    for source, body in (("projectUrl", project_url), ("repository", repo_url), ("releaseNotes", notes), ("README", readme)):
        for u in URL.findall(body):
            u = strip_trailing(u)
            if LOCAL.match(u):
                continue
            if u not in [x[1] for x in urls]:
                urls.append((source, u))
    print("   %d urls" % len(urls))
    for source, u in urls:
        s = status(u)
        ok = isinstance(s, int) and 200 <= s < 300
        print("   %s %s  (%s)" % ("ok  " if ok else "FAIL", u, source))
        if not ok:
            fail("%s: %s from %s -> %s" % (package, u, source, s))


def check_obsoletions(version, workdir):
    print("== obsolete members in the published core assembly")
    scratch = os.path.join(workdir, "obsoletions")
    os.makedirs(scratch, exist_ok=True)
    with open(os.path.join(scratch, "Obsoletions.csproj"), "w", encoding="utf-8") as f:
        f.write(PROJECT.format(version=version))
    with open(os.path.join(scratch, "Program.cs"), "w", encoding="utf-8") as f:
        f.write(PROGRAM)
    with open(os.path.join(scratch, "nuget.config"), "w", encoding="utf-8") as f:
        f.write(NUGET_CONFIG)
    env = dict(os.environ, NUGET_PACKAGES=os.path.join(workdir, "packages"), DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    run = subprocess.run(["dotnet", "run", "--project", scratch, "-c", "Release"], capture_output=True, text=True, env=env)
    if run.returncode != 0:
        fail("scratch project failed:\n" + run.stdout[-3000:] + run.stderr[-3000:])
        return
    seen = {}
    for line in run.stdout.splitlines():
        parts = line.strip().split("|")
        if parts[0] == "assembly":
            print("   " + parts[1])
            if "PublicKeyToken=null" in parts[1]:
                fail("core assembly is not strong-named: " + parts[1])
            continue
        if len(parts) != 4:
            continue
        member, diag_id, url_format, message = parts
        seen.setdefault(member, set()).add((diag_id, url_format))
    for member, expected in OBSOLETE.items():
        if member not in seen:
            fail(member + ": no ObsoleteAttribute found")
            continue
        for diag_id, url_format in seen[member]:
            if diag_id != expected:
                fail("%s: DiagnosticId %s, expected %s" % (member, diag_id, expected))
                continue
            if not url_format:
                fail(member + ": no UrlFormat")
                continue
            page = url_format.format(diag_id)
            anchor = page.split("#", 1)[1] if "#" in page else ""
            try:
                code, html = fetch(page.split("#", 1)[0])
            except urllib.error.HTTPError as e:
                fail("%s: %s -> %s" % (member, page, e.code))
                continue
            has_anchor = anchor and ('id="%s"' % anchor) in html
            print("   %s %s -> %s %s" % ("ok  " if has_anchor else "FAIL", member, page, "anchor present" if has_anchor else "anchor missing"))
            if not has_anchor:
                fail("%s: anchor %s not in %s" % (member, anchor, page))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    keep = "--keep" in sys.argv
    if len(args) != 1:
        print(__doc__)
        sys.exit(2)
    version = args[0]
    workdir = tempfile.mkdtemp(prefix="jsonrpc-links-")
    try:
        for package in PACKAGES:
            check_package(package, version, workdir)
        check_obsoletions(version, workdir)
    finally:
        if keep:
            print("kept " + workdir)
        else:
            shutil.rmtree(workdir, ignore_errors=True)
    if failures:
        print("\n%d failure(s)" % len(failures))
        sys.exit(1)
    print("\nall package links and obsoletion anchors resolve for " + version)


if __name__ == "__main__":
    main()
