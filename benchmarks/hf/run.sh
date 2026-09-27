#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  echo "usage: run.sh <ref> [runs]" >&2
  exit 2
fi
ref=$1
runs=${2:-3}
if ! [[ $runs =~ ^[1-9][0-9]*$ ]]; then
  echo "runs must be a positive integer" >&2
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
git -C "$work" fetch origin "$ref"
git -C "$work" checkout --detach FETCH_HEAD
cd "$work"
dotnet build -c Release TestServer_Console -nodeReuse:false
dotnet run -c Release --no-build --project TestServer_Console -- --machine

run_mode() {
  dotnet run -c Release --no-build --project TestServer_Console -- "$@"
}

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
