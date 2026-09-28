# Benchmarks on Hugging Face Jobs

These scripts run the TestServer_Console benchmarks on a [Hugging Face Jobs](https://huggingface.co/docs/huggingface_hub/guides/jobs) machine and bring the results back. The published figures in the repository README come from the `publish` profile on a `cpu-performance` host.

## Scripts

- `launch.sh <ref> <flavor> [runs|publish]` starts a detached job on `<flavor>` (for example `cpu-performance`) in the `mcr.microsoft.com/dotnet/sdk:10.0` image, with a two-hour timeout. `<ref>` is a branch, tag or commit. It needs the `hf` CLI, logged in.
- `run.sh <ref> [runs|publish]` is what the job runs: it clones the repository, checks out `<ref>`, builds in Release and runs the harness. The core count comes from the cgroup CPU quota when there is one, else `nproc`.
- `fetch.py <job id> <out dir> [--log <file>]` reads the job's log (`hf jobs logs`, or a saved log with `--log`) and writes `job.log`, `job-id.txt`, every framed JSON file, and each framed run's console output as `<mode>-<run>.txt` with the colour codes removed.

## Profiles

With a number, or nothing (3), `run.sh` runs every mode once at three runs per point (`--scale 3 <cores> 4.0`, `--sync 3`, `--async 3 1`, `--async 3 <cores>`, `--kestrel 3`, `--kestrel 3 async`, `--compare 3`) and then that many `--sweep 2` runs, each printed between `== BEGIN sweep-<k>.json` and `== END sweep-<k>.json`.

With `publish` it also builds `benchmarks/Baseline`, sets `JSONRPC_BENCH_RESULTS=/results` so every mode writes a JSON file there, and records the commit in each file. It then runs:

1. the scaling gate once (its exit code is printed as `Scale gate exit code: N` and recorded in `scale-1.json`),
2. two rounds of `--sync 3`, `--async 3 1`, `--async 3 <cores>`, `--kestrel 3`, `--kestrel 3 async` and `--compare 3`, interleaved, so drift over the job spreads across the modes,
3. the 1.x-compatible string loop (menu entry `t`) three times, alternating with two runs of `benchmarks/Baseline` (1.2.3 from NuGet),
4. five `--sweep 2` runs.

Each run's console output is framed by `== BEGIN <mode> <run>` and `== END <mode> <run>`, and at the end every file under `/results` is printed between `== BEGIN <name>.json` and `== END <name>.json`. The whole profile has to finish inside the two-hour timeout that `launch.sh` sets.

## Republishing

To republish, run `benchmarks/hf/launch.sh <ref> cpu-performance publish`, then `python benchmarks/hf/fetch.py <job id> <dir>` to extract each run's JSON and text. `python benchmarks/charts/ingest.py <dir> --source <job id> --conditions "..."` folds the JSON into benchmarks.json and the sweep files, and `render.py` regenerates the charts, the explorer and the README tables between `<!-- benchmarks:... -->` markers. `render.py --check` fails when any of them is stale.

The same files come from a local run: set `JSONRPC_BENCH_RESULTS=<dir>` (and optionally `JSONRPC_BENCH_RUN=<n>` and `JSONRPC_BENCH_COMMIT=<sha>`) before running a mode.
