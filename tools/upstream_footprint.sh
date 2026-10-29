#!/usr/bin/env bash
# Prints how much this fork changes the files that upstream (eXoz00rd/FlaxMCP) also has: for every file that exists
# at the fork point, the lines added, the lines removed and the number of hunks, then the totals. Each changed upstream
# line is a possible merge conflict, so smaller totals mean easier upstream merges. Files only this fork has are not
# counted. Usage: tools/upstream_footprint.sh [revision] (default HEAD); needs the remote "upstream".
set -euo pipefail
revision="${1:-HEAD}"
base="$(git merge-base "$revision" upstream/main)"
printf '%7s %7s %6s  %s\n' added removed hunks file
total_added=0
total_removed=0
total_hunks=0
files=0
while IFS=$'\t' read -r added removed path; do
  if ! git cat-file -e "$base:$path" 2>/dev/null; then
    continue
  fi
  hunks="$(git diff -U0 "$base" "$revision" -- "$path" | grep -c '^@@' || true)"
  printf '%7s %7s %6s  %s\n' "+$added" "-$removed" "$hunks" "$path"
  total_added=$((total_added + added))
  total_removed=$((total_removed + removed))
  total_hunks=$((total_hunks + hunks))
  files=$((files + 1))
done < <(git diff --numstat "$base" "$revision" | sort -k1,1nr)
printf '%7s %7s %6s  %s\n' "+$total_added" "-$total_removed" "$total_hunks" "TOTAL in $files upstream files (fork point ${base:0:9})"
