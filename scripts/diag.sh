#!/usr/bin/env bash
# Порівнює дані на сервері й на пристрої. Запуск з кореня: ./scripts/diag.sh your@email.com
set -uo pipefail
cd "$(dirname "$0")/.."
EMAIL="${1:?Вкажіть email облікового запису: ./scripts/diag.sh you@example.com}"
EMAIL_SQL="${EMAIL//\'/\'\'}"
PKG=com.lighthouse.planner

echo "===== ЧАС ====="
echo "комп'ютер:  $(date '+%Y-%m-%d %H:%M:%S %Z')"
echo "емулятор:   $(adb shell date '+%Y-%m-%d %H:%M:%S %Z' | tr -d '\r')  пояс: $(adb shell getprop persist.sys.timezone | tr -d '\r')"

echo; echo "===== СЕРВЕР ($EMAIL) ====="
CONN=$(dotnet user-secrets list --project src/Lighthouse.Api | sed -n 's/^ConnectionStrings:Default = //p')
PASS=$(echo "$CONN" | sed -n 's/.*Password=\([^;]*\).*/\1/p')
docker exec -e SQLCMDPASSWORD="$PASS" lighthouse-db /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -d Lighthouse -W -s " | " -Q "
SET NOCOUNT ON;
DECLARE @u UNIQUEIDENTIFIER = (SELECT Id FROM Users WHERE Email = N'$EMAIL_SQL');
SELECT 'USER' AS what, Email, TimeZoneId FROM Users WHERE Id=@u;
SELECT 'CATEGORY' AS what, Name, IsDeleted, CreatedAtUtc FROM Categories WHERE UserId=@u ORDER BY CreatedAtUtc;
SELECT 'HABIT' AS what, Title, FrequencyType, IsDeleted, CreatedAtUtc, (SELECT COUNT(*) FROM HabitLogs l WHERE l.HabitId=h.Id) AS Logs FROM Habits h WHERE UserId=@u ORDER BY CreatedAtUtc;
SELECT 'TASKS_BY_CREATED_DAY' AS what, CAST(CreatedAtUtc AS date) AS Day, COUNT(*) AS Created, SUM(CASE WHEN CompletedAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS Completed, SUM(CAST(IsDeleted AS int)) AS Deleted FROM Tasks WHERE UserId=@u GROUP BY CAST(CreatedAtUtc AS date) ORDER BY 2;
"

echo; echo "===== ПРИСТРІЙ ====="
DB=$(adb shell run-as $PKG find . -name 'lighthouse.db3' 2>/dev/null | tr -d '\r' | head -1)
if [ -z "$DB" ]; then echo "Бази на пристрої немає: застосунок перевстановлено або дані стерто."; exit 0; fi
mkdir -p /tmp/lh-diag && rm -f /tmp/lh-diag/*
for ext in "" "-wal" "-shm"; do
  adb exec-out run-as $PKG cat "$DB$ext" > "/tmp/lh-diag/lighthouse.db3$ext" 2>/dev/null
  [ -s "/tmp/lh-diag/lighthouse.db3$ext" ] || rm -f "/tmp/lh-diag/lighthouse.db3$ext"
done
T='(datetime((%s-621355968000000000)/10000000,"unixepoch"))'
sqlite3 -header -separator " | " /tmp/lh-diag/lighthouse.db3 "
SELECT 'CATEGORY' AS what, Name, IsDeleted, SyncState FROM Categories;
SELECT 'HABIT' AS what, Title, FrequencyType, IsDeleted, SyncState FROM Habits;
SELECT 'HABIT_LOGS' AS what, COUNT(*) AS n FROM HabitLogs;
SELECT 'TASKS' AS what, COUNT(*) AS total, SUM(CASE WHEN CompletedAtUtc IS NOT NULL THEN 1 ELSE 0 END) AS completed, SUM(IsDeleted) AS deleted, SUM(CASE WHEN SyncState<>0 THEN 1 ELSE 0 END) AS unsynced FROM Tasks;
SELECT 'COMPLETED_BY_DAY' AS what, date((CompletedAtUtc-621355968000000000)/10000000,'unixepoch') AS day, COUNT(*) AS n FROM Tasks WHERE CompletedAtUtc IS NOT NULL AND IsDeleted=0 GROUP BY day;
SELECT 'OUTBOX' AS what, EntityType, Operation, Attempts, LastError FROM Outbox;
"