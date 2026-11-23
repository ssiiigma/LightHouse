#!/usr/bin/env bash
# Перевіряє, що секрети не потрапили в репозиторій. Запуск з кореня: ./scripts/check-secrets.sh
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
fail=0
bad() { echo "   ПОМИЛКА: $1"; fail=1; }

echo "1) Файли з секретами ігноруються git?"
for f in .env src/Lighthouse.Mobile/Platforms/Android/google-services.json firebase-service-account.json; do
  git check-ignore -q "$f" || bad "$f не в .gitignore"
done
[ $fail -eq 0 ] && echo "   OK"

echo "2) Секретні файли не відстежуються git?"
tracked=$(git ls-files | grep -E '(^|/)(\.env|google-services\.json|.*adminsdk.*\.json|client_secret.*\.json)$' || true)
[ -z "$tracked" ] && echo "   OK" || bad "відстежуються: $tracked"

echo "3) Підозрілі значення у відстежуваних файлах (крім scripts/, *.md, *.resx):"
hits=$(git grep -InE 'Password=[^;" ]+|-----BEGIN [A-Z ]*PRIVATE KEY-----|"private_key"|GOCSPX-[A-Za-z0-9_-]{10,}|AIza[0-9A-Za-z_-]{35}' \
  -- . ':(exclude)scripts' ':(exclude)*.md' ':(exclude).env.example' ':(exclude)*.resx' \
  | grep -vE 'Password=(CHANGE_ME|<[^>]*>|ВАШ_ПАРОЛЬ|\$|\{)' || true)
[ -z "$hits" ] && echo "   OK" || { echo "$hits"; bad "знайдено підозрілі значення"; }

echo "4) .env.example існує й без значень?"
if [ ! -f .env.example ]; then
  bad ".env.example відсутній"
elif grep -vE '^[[:space:]]*(#|$)' .env.example | grep -qE '=.+'; then
  bad ".env.example містить значення"
else
  echo "   OK"
fi

echo "5) Історія git: коміти, що торкались паролів чи ключів (інформаційно; змініть такі паролі, переписувати історію не потрібно):"
git log --all --oneline -S'Password=' -- '*.json' '*.cs' '*.yml' '*.yaml' | sed 's/^/   /'
git log --all --oneline -S'GOCSPX-' | sed 's/^/   /'

[ $fail -eq 0 ] && echo "Результат: усе гаразд" || echo "Результат: є зауваження"
exit $fail
