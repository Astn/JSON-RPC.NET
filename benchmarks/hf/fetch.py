"""Fetch a Hugging Face Job's UTF-8 log and framed sweep JSON files."""
import argparse
import json
import os
import pathlib
import re
import subprocess


FRAME = re.compile(r"^== BEGIN (sweep-[1-9][0-9]*\.json)$")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("job_id")
    parser.add_argument("out_dir", type=pathlib.Path)
    args = parser.parse_args()

    # The hf CLI writes the log with the console's code page unless told otherwise; the harness prints box-drawing characters.
    env = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1")
    result = subprocess.run(["hf", "jobs", "logs", args.job_id], stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, check=False, env=env)
    if result.returncode:
        raise SystemExit(result.stderr.decode("utf-8", errors="replace"))
    log = result.stdout.decode("utf-8", errors="replace")
    args.out_dir.mkdir(parents=True, exist_ok=True)
    (args.out_dir / "job.log").write_text(log, encoding="utf-8")

    lines = log.splitlines()
    index = 0
    written = 0
    while index < len(lines):
        match = FRAME.fullmatch(lines[index])
        if not match:
            index += 1
            continue
        name = match.group(1)
        end = f"== END {name}"
        try:
            last = lines.index(end, index + 1)
        except ValueError as exc:
            raise SystemExit(f"missing {end}") from exc
        content = "\n".join(lines[index + 1:last]) + "\n"
        json.loads(content)
        (args.out_dir / name).write_text(content, encoding="utf-8")
        print(args.out_dir / name)
        written += 1
        index = last + 1
    if not written:
        raise SystemExit(f"no '== BEGIN sweep-<k>.json' frame in the log; see {args.out_dir / 'job.log'}")


if __name__ == "__main__":
    main()
