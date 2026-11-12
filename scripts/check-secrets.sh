#!/usr/bin/env bash
# Перевірка, що секрети не потрапляють у репозиторій. Запуск з кореня: ./scripts/check-secrets.sh
set -u
fail=0

echo "1) .env ігнорується git?"
if git check-ignore -q .env; then echo "   OK"; else echo "   ПОМИЛКА: .env немає в .gitignore"; fail=1; fi

echo "2) .env не відстежується git?"
if git ls-files --error-unmatch .env >/dev/null 2>&1; then
  echo "   ПОМИЛКА: .env у git (виконайте: git rm --cached .env)"; fail=1
else echo "   OK"; fi

echo "3) Підозрілі значення у відстежуваних файлах:"
hits=$(git grep -nEI \
  "(Password|Pwd)=[^;\"' ]+|\"(Secret|ApiKey|ClientSecret)\"[[:space:]]*:[[:space:]]*\"[^\"]+\"|PRIVATE KEY|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}" \
  -- . ':!*.md' ':!scripts/check-secrets.sh' ':!tests' 2>/dev/null \
  | grep -v 'dev-only' | grep -v '\${' || true)
if [ -n "$hits" ]; then echo "$hits"; echo "   ПОМИЛКА: перегляньте рядки вище"; fail=1; else echo "   OK"; fi

echo "4) .env.example (значення мають бути заглушками):"
if [ -f .env.example ]; then sed 's/^/   /' .env.example; else echo "   файлу немає"; fi

echo "5) Історія git: коміти, що торкались паролів у файлах налаштувань (старі паролі мають бути змінені):"
git log --oneline -S"Password=" -- 'src/Lighthouse.Api/appsettings*.json' | sed 's/^/   /'

if [ "$fail" -eq 0 ]; then echo "Результат: OK"; else echo "Результат: є зауваження"; fi
exit "$fail"