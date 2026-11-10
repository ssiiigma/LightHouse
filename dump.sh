#!/usr/bin/env bash
# Збирає вихідний код проєкту в один текстовий файл для передачі розробнику-напарнику.
OUT="lighthouse_dump.txt"
: > "$OUT"
find . -type f \( -name "*.cs" -o -name "*.xaml" -o -name "*.csproj" -o -name "*.sln" \
  -o -name "*.json" -o -name "*.yml" -o -name "*.md" -o -name "*.http" -o -name "*.resx" \
  -o -name "Dockerfile" -o -name ".env.example" -o -name "*.xml" -o -name "*.sh" -o -name "*.ps1" \) \
  -not -path "*/bin/*" -not -path "*/obj/*" -not -path "*/.git/*" \
  -not -path "*/.idea/*" -not -path "*/node_modules/*" \
  -not -name "*.Designer.cs" -not -name "*ModelSnapshot.cs" -not -name "*_*.cs" -not -name "dump.sh" \
  | sort | while read -r f; do
    echo "=================== FILE: $f ===================" >> "$OUT"
    cat "$f" >> "$OUT"
    echo >> "$OUT"
done
wc -c "$OUT"
