#!/usr/bin/env bash
# Перевіряє, що сервер віддає дані облікового запису. Запуск: ./scripts/check-account.sh your@email.com
set -uo pipefail
HOST="${HOST:-http://localhost:5080}"
EMAIL="${1:?Вкажіть email}"
read -rs -p "Пароль: " PASSWORD; echo
BODY=$(printf '{"email":"%s","password":"%s"}' "$EMAIL" "$PASSWORD")
CODE=$(curl -s -o /tmp/lh_login.json -w '%{http_code}' -X POST "$HOST/api/auth/login" -H 'Content-Type: application/json' -d "$BODY")
echo "login: HTTP $CODE"
[ "$CODE" = "200" ] || { head -c 300 /tmp/lh_login.json; echo; exit 1; }
TOKEN=$(sed -E 's/.*"accessToken":"([^"]+)".*/\1/' /tmp/lh_login.json)
for path in categories habits tasks; do
  CODE=$(curl -s -o /tmp/lh_$path.json -w '%{http_code}' "$HOST/api/$path" -H "Authorization: Bearer $TOKEN")
  echo "GET /api/$path: HTTP $CODE, об'єктів: $(grep -o '"id"' /tmp/lh_$path.json | wc -l | tr -d ' ')"
  [ "$CODE" = "200" ] || { head -c 400 /tmp/lh_$path.json; echo; }
done