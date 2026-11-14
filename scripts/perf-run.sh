#!/usr/bin/env bash
# Вимірює затримки ключових ендпоінтів. Спершу: ./scripts/perf-seed.sh
set -euo pipefail

HOST="${HOST:-http://localhost:5080}"
N="${N:-200}"
# shellcheck disable=SC1091
source /tmp/lh_perf.env

TOKEN=$(curl -sS -f -X POST "$HOST/api/auth/login" -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

run() { # назва, клієнтів, запитів, метод, шлях, [тіло]
  local name="$1" c="$2" n="$3" method="$4" path="$5" body="${6:-}"
  local args=(-n "$n" -c "$c" -t 30 -m "$method" -H "Authorization: Bearer $TOKEN")
  if [ -n "$body" ]; then args+=(-T "application/json" -d "$body"); fi
  echo; echo "=== $name | клієнтів: $c, запитів: $n ==="
  hey "${args[@]}" "$HOST$path" | grep -E "Average|Slowest|50% in|95% in|99% in|\[[0-9]+\]"
}

SINCE=$(date -u -v-7d +%Y-%m-%dT%H:%M:%SZ)
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
TASK_ID=$(uuidgen | tr 'A-Z' 'a-z')
UPSERT="{\"title\":\"Perf upsert\",\"description\":null,\"deadlineUtc\":null,\"priority\":1,\"categoryId\":null,\"reminderAtUtc\":null,\"completedAtUtc\":null,\"estimatedMinutes\":30,\"subtasks\":[],\"clientUpdatedAtUtc\":\"$NOW\"}"
LOGIN="{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}"

# Прогрів (JIT, пул з'єднань, перше ранжування створює рекомендації)
for path in /api/tasks /api/habits /api/recommendations/ranking; do
  hey -n 30 -c 1 -H "Authorization: Bearer $TOKEN" "$HOST$path" > /dev/null
done

run "GET /health (база для порівняння)"            1  "$N" GET "/health"
run "GET /api/tasks (повний список, ~230 завдань)" 1  "$N" GET "/api/tasks"
run "GET /api/tasks (повний список)"               10 "$N" GET "/api/tasks"
run "GET /api/tasks?modifiedSince (дельта, 7 діб)" 1  "$N" GET "/api/tasks?modifiedSince=$SINCE"
run "GET /api/habits (10 звичок із відмітками)"    1  "$N" GET "/api/habits"
run "GET /api/recommendations/ranking (200 завдань)" 1  "$N" GET "/api/recommendations/ranking"
run "GET /api/recommendations/ranking"             10 "$N" GET "/api/recommendations/ranking"
run "PUT /api/tasks/{id} (запис)"                  1  "$N" PUT "/api/tasks/$TASK_ID" "$UPSERT"
run "POST /api/auth/login (bcrypt)"                1  30   POST "/api/auth/login" "$LOGIN"