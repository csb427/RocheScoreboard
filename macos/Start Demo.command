#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0, then run this file again.'
  read -r -p 'Press Return to close…' _roche_reply
  exit 1
fi
dotnet run --project RocheScoreboard.Mac/RocheScoreboard.Mac.csproj -- --legacy-demo
