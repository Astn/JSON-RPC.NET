#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "usage: launch.sh <ref: branch, tag or commit> <flavor> [runs]" >&2
  exit 2
fi
ref=$1
flavor=$2
runs=${3:-3}
if ! [[ $runs =~ ^[1-9][0-9]*$ ]]; then
  echo "runs must be a positive integer" >&2
  exit 2
fi

hf jobs run --detach --flavor "$flavor" --timeout 2h --name "jsonrpc-bench-$flavor" mcr.microsoft.com/dotnet/sdk:10.0 bash -c \
  'git clone https://github.com/Astn/JSON-RPC.NET.git /tmp/jsonrpc-runner && (git -C /tmp/jsonrpc-runner fetch origin "$1" && git -C /tmp/jsonrpc-runner checkout --detach FETCH_HEAD || git -C /tmp/jsonrpc-runner checkout --detach "$1") && exec bash /tmp/jsonrpc-runner/benchmarks/hf/run.sh "$1" "$2"' \
  bash "$ref" "$runs"
