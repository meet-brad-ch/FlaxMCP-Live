#!/usr/bin/env bash
# Counts the merge conflicts that each given upstream revision would cause on a revision of this fork, without a
# checkout and without changing any branch (git merge-tree --write-tree, git 2.38 or later). Prints the conflicted
# files per revision and a total. Usage: tools/merge_probe.sh <our-revision> <upstream-revision>...
# Example: tools/merge_probe.sh HEAD upstream/main upstream/pr/86 upstream/pr/85
set -euo pipefail
ours="${1:?usage: merge_probe.sh <our-revision> <upstream-revision>...}"
shift
total=0
for theirs in "$@"; do
  conflicted="$(git merge-tree --write-tree --name-only --no-messages "$ours" "$theirs" | tail -n +2 || true)"
  count=0
  if [ -n "$conflicted" ]; then
    count="$(printf '%s\n' "$conflicted" | wc -l | tr -d ' ')"
  fi
  printf '%3s conflicted files  %s\n' "$count" "$theirs"
  if [ "$count" -gt 0 ]; then
    printf '%s\n' "$conflicted" | sed 's/^/      /'
  fi
  total=$((total + count))
done
printf '%3s conflicted files in total\n' "$total"
