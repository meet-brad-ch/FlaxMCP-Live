#!/usr/bin/env bash
# Prepares a release on main: checks the tree and the changelog, writes the version, commits "Release x.y.z" and makes
# the annotated tag vx.y.z. It pushes nothing: push the commit and the tag when the release is approved, and the
# release workflow takes over (.github/workflows/release-live.yml).
set -euo pipefail
version="${1:?usage: release.sh x.y.z[-label]}"
repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || { echo "release: not on main" >&2; exit 1; }
[ -z "$(git status --porcelain)" ] || { echo "release: working tree is not clean" >&2; exit 1; }
grep -q "^## \[$version\]" CHANGELOG.md || { echo "release: CHANGELOG.md has no section [$version]" >&2; exit 1; }
tools/set_version.sh "$version"
git add -A
git commit -m "Release $version"
git tag -a "v$version" -m "FlaxMCP-Live $version"
echo "tagged v$version; push with: git push origin main v$version"
