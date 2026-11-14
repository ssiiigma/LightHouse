#!/usr/bin/env bash
# Запускає інтеграційні тести API: рядок підключення береться з user-secrets, окрема тестова база створюється й видаляється автоматично.
set -euo pipefail
cd "$(dirname "$0")/.."

CONNECTION=$(dotnet user-secrets list --project src/Lighthouse.Api | sed -n 's/^ConnectionStrings:Default = //p')
if [ -z "$CONNECTION" ]; then
  echo "У user-secrets немає ConnectionStrings:Default (див. налаштування пароля SQL Server)."; exit 1
fi

export LIGHTHOUSE_TEST_SQL=$(echo "$CONNECTION" | sed 's/Database=[^;]*;//')
dotnet test tests/Lighthouse.Api.Tests "$@"