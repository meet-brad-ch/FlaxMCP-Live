#!/usr/bin/env bash
# Writes one release version into the three files that carry it: src/FlaxMcp/FlaxMcp.csproj (VersionPrefix and
# VersionSuffix), src/FlaxMcp/.mcp/server.json (both "version" fields) and src/FlaxMcpBridge/FlaxMcpBridge.flaxproj
# and src/FlaxMcpBridge/Source/FlaxMcpBridge/BridgeGamePlugin.cs (the numeric part only in these two). Use x.y.z or
# x.y.z-label, for example 0.2.0-preview.2.
# tools/check_version.sh verifies that the three agree.
set -euo pipefail
version="${1:?usage: set_version.sh x.y.z[-label]}"
if ! [[ "$version" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-([0-9A-Za-z.-]+))?$ ]]; then
  echo "set_version: '$version' is not x.y.z or x.y.z-label" >&2
  exit 1
fi
prefix="${BASH_REMATCH[1]}"
suffix="${BASH_REMATCH[3]:-}"
repo="$(cd "$(dirname "$0")/.." && pwd)"
csproj="$repo/src/FlaxMcp/FlaxMcp.csproj"
server="$repo/src/FlaxMcp/.mcp/server.json"
flaxproj="$repo/src/FlaxMcpBridge/FlaxMcpBridge.flaxproj"
plugin="$repo/src/FlaxMcpBridge/Source/FlaxMcpBridge/BridgeGamePlugin.cs"
sed -b -i -E "s|<VersionPrefix>[^<]*</VersionPrefix>|<VersionPrefix>$prefix</VersionPrefix>|" "$csproj"
if [ -n "$suffix" ]; then
  sed -b -i -E "s|<VersionSuffix>[^<]*</VersionSuffix>|<VersionSuffix>$suffix</VersionSuffix>|" "$csproj"
else
  sed -b -i -E "/<VersionSuffix>/d" "$csproj"
fi
sed -b -i -E "s|(\"version\": \")[^\"]*(\")|\1$version\2|" "$server"
sed -b -i -E "s|(\"Version\": \")[^\"]*(\")|\1$prefix\2|" "$flaxproj"
IFS=. read -r major minor patch <<< "$prefix"
sed -b -i -E "s|new Version\([0-9]+, [0-9]+, [0-9]+\)|new Version($major, $minor, $patch)|" "$plugin"
echo "version set: $version"
"$repo/tools/check_version.sh"
