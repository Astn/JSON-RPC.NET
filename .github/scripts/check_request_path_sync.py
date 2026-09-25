#!/usr/bin/env python3
"""Every synchronization point and every writable static on the request path must be listed, with a reason, in
.github/request-path-sync.allowlist: `lock (`, `Interlocked.`, `Volatile.Write`, `[ThreadStatic]` fields and static
fields that are not readonly, in the request-path files of the core and of both companion serializers.

A process-wide serialization point on the per-document path caps ProcessAsync at a few million requests per second
on every core count (the AsyncScratch pool lock, 2026-09-25), and the single-threaded micro-benchmarks cannot see
it; a static that one request writes and every core reads costs the same kind of cache-line traffic without a lock.
This check is a review aid: it catches a new lock, atomic or writable static on those files and asks for a written
reason (per-thread, miss-path, registration-only, read-only-after-init). It is not the measurement;
`TestServer_Console --scale` is (a probe moved behind an allowed lock would pass here and fail there). Mutable
objects reached through readonly references are outside its reach and belong to review.

Usage: python3 .github/scripts/check_request_path_sync.py  (from the repository root; exit 1 on any unlisted use)
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ALLOWLIST = os.path.join(ROOT, ".github", "request-path-sync.allowlist")
PATTERNS = ["Json-Rpc/JsonRpcProcessor*.cs", "Json-Rpc/Handler*.cs", "Json-Rpc/Invocation/*.cs", "Json-Rpc/Jsmn/*.cs",
            "Json-Rpc/Serialization/*.cs", "Json-Rpc/JsonRpcContext.cs", "Json-Rpc/JsonRpcRequestId.cs", "Json-Rpc/RpcBinding.cs",
            "AustinHarris.JsonRpc.SystemTextJson/*.cs", "AustinHarris.JsonRpc.Newtonsoft/*.cs"]
USE = re.compile(r"\block\s*\(|\bInterlocked\.|\bVolatile\.Write")
# A static field declaration that is not readonly or const: `[ThreadStatic] private static T name;` or `static T name = ...;`.
# Expression-bodied members (`=>`), methods, classes, events and operators are not fields.
FIELD = re.compile(r"^(?:\[ThreadStatic\]\s*)?(?:(?:public|private|internal|protected|new|volatile)\s+)*static\s+"
                   r"(?!readonly\b|const\b|class\b|void\b|partial\b|event\b|explicit\b|implicit\b|operator\b)"
                   r"(?!.*=>)(?!.*\()[\w<>\[\],.?\s]+?\s+\w+\s*(?:=[^;]*)?;$")


def load_allowlist():
    """{(file, code line stripped): reason}; lines are `file | code | reason`, `#` comments and blanks ignored."""
    allowed = {}
    with open(ALLOWLIST, encoding="utf-8") as f:
        for n, line in enumerate(f, 1):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = [p.strip() for p in line.split("|", 2)]
            if len(parts) != 3 or not all(parts):
                sys.exit(f"{ALLOWLIST}:{n}: expected `file | code | reason`")
            allowed[(parts[0].replace("\\", "/"), parts[1])] = parts[2]
    return allowed


def main():
    allowed = load_allowlist()
    seen = set()
    problems = []
    for pattern in PATTERNS:
        for path in sorted(glob.glob(os.path.join(ROOT, pattern))):
            rel = os.path.relpath(path, ROOT).replace("\\", "/")
            with open(path, encoding="utf-8-sig") as f:
                for n, line in enumerate(f, 1):
                    code = line.split("//", 1)[0].strip()
                    if not (USE.search(code) or FIELD.match(code)):
                        continue
                    key = (rel, code)
                    if key in allowed:
                        seen.add(key)
                    else:
                        problems.append(f"{rel}:{n}: `{code}` is not in {os.path.relpath(ALLOWLIST, ROOT)}; add it with a reason or take it off the request path")
    for key in allowed:
        if key not in seen:
            problems.append(f"{os.path.relpath(ALLOWLIST, ROOT)}: `{key[1]}` in {key[0]} no longer exists; remove the entry")
    for p in problems:
        print(p)
    if problems:
        return 1
    print(f"request-path synchronization: {len(seen)} listed uses, none unlisted")
    return 0


if __name__ == "__main__":
    sys.exit(main())
