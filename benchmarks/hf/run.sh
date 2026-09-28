#!/usr/bin/env bash
set -euo pipefail

# run.sh <ref> [runs]     every harness mode once at three runs per point, then <runs> sweeps (the default, 3)
# run.sh <ref> publish    the published set: every mode writes JSON under /results (see benchmarks/hf/README.md)
if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: run.sh <ref: branch, tag or commit> [runs|publish]" >&2
  exit 2
fi
ref=$1
profile=${2:-3}
if [[ $profile != publish && ! $profile =~ ^[1-9][0-9]*$ ]]; then
  echo "the second argument must be a positive integer or publish" >&2
  exit 2
fi

cores=$(nproc)
if [[ -r /sys/fs/cgroup/cpu.max ]]; then
  read -r quota period < /sys/fs/cgroup/cpu.max
  if [[ $quota =~ ^[0-9]+$ && $period =~ ^[1-9][0-9]*$ ]]; then
    cores=$(((quota + period - 1) / period))
  fi
fi

work=$(mktemp -d)
git clone https://github.com/Astn/JSON-RPC.NET.git "$work"
# A branch, tag or full hash is fetched; anything else (a short hash) is resolved from the full clone.
git -C "$work" fetch origin "$ref" && git -C "$work" checkout --detach FETCH_HEAD || git -C "$work" checkout --detach "$ref"
cd "$work"
dotnet build -c Release TestServer_Console -nodeReuse:false
if [[ $profile == publish ]]; then
  dotnet build -c Release benchmarks/Baseline -nodeReuse:false
fi
dotnet run -c Release --no-build --project TestServer_Console -- --machine

run_mode() {
  dotnet run -c Release --no-build --project TestServer_Console -- "$@"
}

if [[ $profile != publish ]]; then
  runs=$profile
  scale_status=0
  run_mode --scale 3 "$cores" 4.0 || scale_status=$?
  printf 'Scale gate exit code: %s\n' "$scale_status"
  run_mode --sync 3
  run_mode --async 3 1
  run_mode --async 3 "$cores"
  run_mode --kestrel 3
  run_mode --kestrel 3 async
  run_mode --compare 3

  mkdir -p /results
  for ((k = 1; k <= runs; k++)); do
    name="sweep-$k.json"
    run_mode --sweep 2 "/results/$name"
    printf '== BEGIN %s\n' "$name"
    cat "/results/$name"
    printf '== END %s\n' "$name"
  done
  exit 0
fi

# The publish profile: each mode writes <stem>-<run>.json under /results (TestServer_Console/BenchResults.cs), and
# benchmarks/charts/ingest.py folds the files into benchmarks.json. The runs of different modes are interleaved so
# that drift over the job spreads across the modes instead of landing on one of them.
export JSONRPC_BENCH_RESULTS=/results
JSONRPC_BENCH_COMMIT=$(git rev-parse HEAD)
export JSONRPC_BENCH_COMMIT
mkdir -p /results

# framed <mode> <run> <command...>: runs the command with JSONRPC_BENCH_RUN=<run> between BEGIN and END lines, so
# fetch.py can cut each run's console output out of the log. A failed run is noted and the job goes on, so one bad run
# does not cost the others; the job exits non-zero at the end. The scaling gate's failure is a result (scale-1.json
# records it), not a failed run.
failed=()
last_status=0
framed() {
  local mode=$1 run=$2
  shift 2
  last_status=0
  printf '== BEGIN %s %s\n' "$mode" "$run"
  JSONRPC_BENCH_RUN=$run "$@" || last_status=$?
  printf '== END %s %s\n' "$mode" "$run"
  if ((last_status != 0)) && [[ $mode != scale ]]; then
    failed+=("$mode $run (exit $last_status)")
  fi
}

legacy() {
  printf 't\nq\n' | dotnet run -c Release --no-build --project TestServer_Console
}

baseline() {
  dotnet run -c Release --no-build --project benchmarks/Baseline
}

framed scale 1 run_mode --scale 3 "$cores" 4.0
printf 'Scale gate exit code: %s\n' "$last_status"
for i in 1 2; do
  framed sync "$i" run_mode --sync 3
  framed async-1 "$i" run_mode --async 3 1
  framed async-N "$i" run_mode --async 3 "$cores"
  framed kestrel "$i" run_mode --kestrel 3
  framed kestrel-async "$i" run_mode --kestrel 3 async
  framed compare "$i" run_mode --compare 3
done
framed legacy 1 legacy
framed baseline 1 baseline
framed legacy 2 legacy
framed baseline 2 baseline
framed legacy 3 legacy
for i in 1 2 3 4 5; do
  framed sweep "$i" run_mode --sweep 2
done

for file in /results/*.json; do
  name=$(basename "$file")
  printf '== BEGIN %s\n' "$name"
  cat "$file"
  printf '== END %s\n' "$name"
done
if ((${#failed[@]} > 0)); then
  printf 'Failed run: %s\n' "${failed[@]}"
  exit 1
fi
