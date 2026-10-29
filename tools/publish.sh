#!/usr/bin/env bash
# Publishes the server to .publish/ (git-ignored), the folder the MCP client registration runs FlaxMcp.exe from.
# A running server holds its files: they are renamed aside first (Windows allows renaming a file in use), so the
# publish can write; reconnect the client (/mcp in Claude Code) to start the new build. Old copies are removed once free.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
out="$repo/.publish"
if [ -d "$out" ]; then
  for file in "$out"/*.dll "$out"/*.exe; do
    [ -e "$file" ] && mv -f "$file" "$file.old-$(date +%s%N)"
  done
  rm -f "$out"/*.old-* 2>/dev/null || true
fi
dotnet publish "$repo/src/FlaxMcp" -c Release -o "$out" --nologo -v quiet
echo "published: $out/FlaxMcp.exe (reconnect the MCP client to load it)"
