#!/usr/bin/env bash
# Збирає й запускає застосунок на емуляторі, потім відновлює розв'язку портів.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/Lighthouse.Mobile -f net10.0-android -t:Run
./scripts/adb-reverse.sh
