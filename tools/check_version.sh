#!/usr/bin/env bash
# Fails when the release version differs between src/FlaxMcp/FlaxMcp.csproj, src/FlaxMcp/.mcp/server.json and
# src/FlaxMcpBridge/FlaxMcpBridge.flaxproj and BridgeGamePlugin.cs. With an argument (for example the tag v0.2.0) it also fails when that
# value, without a leading v, is not the project version. Prints the version when all agree.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
csproj="$repo/src/FlaxMcp/FlaxMcp.csproj"
server="$repo/src/FlaxMcp/.mcp/server.json"
flaxproj="$repo/src/FlaxMcpBridge/FlaxMcpBridge.flaxproj"
plugin="$repo/src/FlaxMcpBridge/Source/FlaxMcpBridge/BridgeGamePlugin.cs"
prefix="$(sed -n -E 's|.*<VersionPrefix>([^<]*)</VersionPrefix>.*|\1|p' "$csproj")"
suffix="$(sed -n -E 's|.*<VersionSuffix>([^<]*)</VersionSuffix>.*|\1|p' "$csproj")"
version="$prefix${suffix:+-$suffix}"
failed=0
while IFS= read -r found; do
  if [ "$found" != "$version" ]; then
    echo "check_version: server.json has '$found', FlaxMcp.csproj has '$version'" >&2
    failed=1
  fi
done < <(sed -n -E 's|^ *"version": "([^"]*)".*|\1|p' "$server")
bridge="$(sed -n -E 's|^[[:space:]]*"Version": "([^"]*)".*|\1|p' "$flaxproj")"
if [ "$bridge" != "$prefix" ]; then
  echo "check_version: FlaxMcpBridge.flaxproj has '$bridge', FlaxMcp.csproj has '$prefix'" >&2
  failed=1
fi
plugin_version="$(sed -n -E 's|.*new Version\(([0-9]+), ([0-9]+), ([0-9]+)\).*|\1.\2.\3|p' "$plugin")"
if [ "$plugin_version" != "$prefix" ]; then
  echo "check_version: BridgeGamePlugin.cs has '$plugin_version', FlaxMcp.csproj has '$prefix'" >&2
  failed=1
fi
if [ $# -gt 0 ] && [ "${1#v}" != "$version" ]; then
  echo "check_version: '$1' is not the project version '$version'" >&2
  failed=1
fi
[ "$failed" -eq 0 ] || exit 1
echo "$version"
