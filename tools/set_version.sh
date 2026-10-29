#!/usr/bin/env bash
# Writes one release version into the three files that carry it: src/FlaxMcp/FlaxMcp.csproj (VersionPrefix and
# VersionSuffix), src/FlaxMcp/.mcp/server.json (both "version" fields) and src/FlaxMcpBridge/FlaxMcpBridge.flaxproj
# (the numeric part only; a .flaxproj takes no label). Use x.y.z or x.y.z-label, for example 0.2.0-preview.2.
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
sed -i -E "s|<VersionPrefix>[^<]*</VersionPrefix>|<VersionPrefix>$prefix</VersionPrefix>|" "$csproj"
if [ -n "$suffix" ]; then
  sed -i -E "s|<VersionSuffix>[^<]*</VersionSuffix>|<VersionSuffix>$suffix</VersionSuffix>|" "$csproj"
else
  sed -i -E "/<VersionSuffix>/d" "$csproj"
fi
sed -i -E "s|(\"version\": \")[^\"]*(\")|\1$version\2|" "$server"
sed -i -E "s|(\"Version\": \")[^\"]*(\")|\1$prefix\2|" "$flaxproj"
echo "version set: $version"
"$repo/tools/check_version.sh"
