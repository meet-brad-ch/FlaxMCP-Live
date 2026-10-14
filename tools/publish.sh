#!/usr/bin/env bash
# Publishes the server to .publish/ (git-ignored), the folder the MCP client registration runs FlaxMcp.exe from.
# A running MCP client holds the exe: reconnect it (/mcp in Claude Code) after publishing.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
dotnet publish "$repo/src/FlaxMcp" -c Release -o "$repo/.publish" --nologo -v quiet
echo "published: $repo/.publish/FlaxMcp.exe"
