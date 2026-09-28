"""Fetch a Hugging Face Job's UTF-8 log and cut it into the framed files run.sh prints.

python benchmarks/hf/fetch.py <job id> <out dir> [--log <file>]

Writes job.log and job-id.txt, every `== BEGIN <name>.json` frame to <name>.json (checked with json.loads), and every
`== BEGIN <mode> <run>` frame (the publish profile's console output per run) to <mode>-<run>.txt with the ANSI colour
codes removed. With --log the log is read from that file instead of from `hf jobs logs`.
"""
import argparse
import json
import os
import pathlib
import re
import subprocess
import sys


JSON_FRAME = re.compile(r"^== BEGIN ([A-Za-z0-9_.-]+\.json)$")
RUN_FRAME = re.compile(r"^== BEGIN ([A-Za-z0-9_-]+) ([1-9][0-9]*)$")
ANSI = re.compile(r"\x1b\[[0-9;]*m")


def job_log(job_id):
    # The hf CLI writes the log with the console's code page unless told otherwise; the harness prints box-drawing characters.
    env = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1")
    result = subprocess.run(["hf", "jobs", "logs", job_id], stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, check=False, env=env)
    if result.returncode:
        raise SystemExit(result.stderr.decode("utf-8", errors="replace"))
    return result.stdout.decode("utf-8", errors="replace")


def split(log, out_dir):
    """Writes each framed file under out_dir; returns the paths written, JSON files first in log order."""
    lines = [line.rstrip("\r") for line in log.splitlines()]
    written = []
    index = 0
    while index < len(lines):
        match = JSON_FRAME.fullmatch(lines[index]) or RUN_FRAME.fullmatch(lines[index])
        if not match:
            index += 1
            continue
        is_json = match.re is JSON_FRAME
        label = match.group(1) if is_json else f"{match.group(1)} {match.group(2)}"
        end = f"== END {label}"
        try:
            last = lines.index(end, index + 1)
        except ValueError as exc:
            raise SystemExit(f"missing '{end}' after line {index + 1}") from exc
        body = lines[index + 1:last]
        if is_json:
            name = match.group(1)
            content = "\n".join(body) + "\n"
            try:
                json.loads(content)
            except json.JSONDecodeError as exc:
                raise SystemExit(f"{name}: not JSON ({exc})") from exc
        else:
            name = f"{match.group(1)}-{match.group(2)}.txt"
            content = "\n".join(ANSI.sub("", line) for line in body) + "\n"
        path = out_dir / name
        path.write_text(content, encoding="utf-8", newline="\n")
        written.append(path)
        index = last + 1
    return written


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("job_id")
    parser.add_argument("out_dir", type=pathlib.Path)
    parser.add_argument("--log", type=pathlib.Path, help="parse this saved log instead of calling hf jobs logs")
    args = parser.parse_args(argv)

    log = args.log.read_text(encoding="utf-8", errors="replace") if args.log else job_log(args.job_id)
    args.out_dir.mkdir(parents=True, exist_ok=True)
    (args.out_dir / "job.log").write_text(log, encoding="utf-8", newline="\n")
    (args.out_dir / "job-id.txt").write_text(args.job_id + "\n", encoding="utf-8", newline="\n")
    written = split(log, args.out_dir)
    for path in written:
        print(path)
    if not any(p.suffix == ".json" for p in written):
        raise SystemExit(f"no '== BEGIN <name>.json' frame in the log; see {args.out_dir / 'job.log'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
