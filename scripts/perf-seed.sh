#!/usr/bin/env bash
# Готує дані для вимірювання швидкодії API: новий користувач, 200 відкритих і 30 виконаних завдань,
# 5 категорій, 10 звичок із відмітками за 14 днів. Запуск з кореня: ./scripts/perf-seed.sh
# API має працювати на $HOST із піднятими лімітами запитів, інакше буде 429.
set -euo pipefail

HOST="${HOST:-http://localhost:5080}"
EMAIL="perf-$(date +%s)@example.com"
PASSWORD='Passw0rd!Perf1'

uuid() { uuidgen | tr 'A-Z' 'a-z'; }
iso() { date -u -v"$1" +%Y-%m-%dT%H:%M:%SZ; }   # macOS (BSD date): iso "+5H", iso "-3d"

put() {
  local path="$1" body="${2:-}"
  if [ -n "$body" ]; then
    curl -sS -f -X PUT "$HOST$path" -H "Authorization: Bearer $TOKEN" \
      -H "Content-Type: application/json" -d "$body" -o /dev/null
  else
    curl -sS -f -X PUT "$HOST$path" -H "Authorization: Bearer $TOKEN" -o /dev/null
  fi
}

RESPONSE=$(curl -sS -f -X POST "$HOST/api/auth/register" -H "Content-Type: application/json" \
  -d "{\"name\":\"Perf User\",\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")
TOKEN=$(echo "$RESPONSE" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
NOW=$(iso "+0H")

CATS=()
for c in 1 2 3 4 5; do
  id=$(uuid); CATS+=("$id")
  put "/api/categories/$id" "{\"name\":\"Category $c\",\"colorHex\":\"#4C8BF5\",\"icon\":null,\"clientUpdatedAtUtc\":\"$NOW\"}"
done

for i in $(seq 1 230); do   # 1..200 відкриті, 201..230 виконані
  id=$(uuid)
  priority=$((i % 4))
  estimate=$((15 + i % 90))
  if [ $((i % 3)) -eq 0 ]; then
    deadline=null
  else
    off=$((i - 20)); sign="+"; [ $off -lt 0 ] && sign=""
    deadline="\"$(iso "${sign}${off}H")\""
  fi
  if [ $((i % 6)) -eq 0 ]; then category=null; else category="\"${CATS[$((i % 5))]}\""; fi
  if [ $i -gt 200 ]; then completed="\"$(iso "-$(( (i - 200) * 20 ))H")\""; else completed=null; fi
  if [ $((i % 5)) -eq 0 ]; then
    subtasks="[{\"id\":\"$(uuid)\",\"title\":\"Step 1\",\"isDone\":true},{\"id\":\"$(uuid)\",\"title\":\"Step 2\",\"isDone\":false}]"
  else
    subtasks="[]"
  fi
  put "/api/tasks/$id" "{\"title\":\"Task $i\",\"description\":null,\"deadlineUtc\":$deadline,\"priority\":$priority,\"categoryId\":$category,\"reminderAtUtc\":null,\"completedAtUtc\":$completed,\"estimatedMinutes\":$estimate,\"subtasks\":$subtasks,\"clientUpdatedAtUtc\":\"$NOW\"}"
done

for h in $(seq 1 10); do
  hid=$(uuid)
  put "/api/habits/$hid" "{\"title\":\"Habit $h\",\"description\":null,\"frequencyType\":0,\"daysOfWeekMask\":0,\"timesPerWeek\":null,\"reminderTime\":null,\"colorHex\":\"#5BB97C\",\"icon\":null,\"categoryId\":null,\"clientUpdatedAtUtc\":\"$NOW\"}"
  for d in $(seq 0 13); do
    if [ $(( (h + d) % 4 )) -eq 0 ]; then continue; fi   # пропуски, щоб серії були різними
    put "/api/habits/$hid/logs/$(iso "-${d}d" | cut -c1-10)"
  done
done

printf 'EMAIL=%q\nPASSWORD=%q\n' "$EMAIL" "$PASSWORD" > /tmp/lh_perf.env
echo "Готово: користувач $EMAIL (дані для входу в /tmp/lh_perf.env)"