#!/usr/bin/env bash
# Builds a Release and publishes it as a GitHub release with the mod DLLs
# attached. Pre-release channels (alpha/beta/rc) are marked as pre-releases.
#
# The build number is auto-incremented per (version, channel) and tracked in
# build.json (repo root, source-only — never shipped). The chosen number is
# also written into Info.Build so the compiled DLL knows its own build.
#
# Version + Channel come from Koren/Core/Info.cs — set those there, then run:
#   ./tools/release.sh
#
# The mod references the game's managed DLLs, so it can't build in CI — run
# this locally. Requires `gh` (authenticated) and `jq`.
set -euo pipefail

cd "$(dirname "$0")/.."

command -v gh >/dev/null || { echo "gh required: brew install gh (then 'gh auth login')" >&2; exit 1; }
command -v jq >/dev/null || { echo "jq required: brew install jq" >&2; exit 1; }

INFO="Koren/Core/Info.cs"
BUILDS="build.json"
[ -f "$BUILDS" ] || echo '{}' > "$BUILDS"

ver=$(grep -E 'const string Version' "$INFO" | sed -E 's/.*"([^"]+)".*/\1/')
chan=$(grep -E 'const string Channel' "$INFO" | sed -E 's/.*"([^"]+)".*/\1/')

if [ "$chan" = "stable" ]; then
  tag="v${ver}"
  title="v${ver}"
  notes="Stable release ${ver}."
  pre=""
else
  # Increment the per-(version, channel) build counter.
  cur=$(jq -r --arg v "$ver" --arg c "$chan" '.[$v][$c] // 0' "$BUILDS")
  next=$((cur + 1))

  tmp=$(mktemp)
  jq --arg v "$ver" --arg c "$chan" --argjson n "$next" '.[$v][$c] = $n' "$BUILDS" > "$tmp" && mv "$tmp" "$BUILDS"

  # Bake the number into the compiled const (BSD/macOS sed).
  sed -i '' -E "s/(const int Build = )[0-9]+;/\1${next};/" "$INFO"

  tag="v${ver}-${chan}-${next}"
  title="$tag"
  notes="${chan} build ${next} of ${ver}."
  pre="--prerelease"
  echo "Build number: ${cur} -> ${next}  (${ver} ${chan})"
fi

echo "Building ${tag} ..."
dotnet build Koren.Loader.ML/Koren.Loader.ML.csproj -c Release

koren="Koren/bin/Release/netstandard2.1/Koren.dll"
loader="Koren.Loader.ML/bin/Release/netstandard2.1/Koren.Loader.ML.dll"
[ -f "$koren" ] && [ -f "$loader" ] || { echo "build outputs missing — aborting" >&2; exit 1; }

echo "Publishing ${tag} ..."
if gh release view "$tag" >/dev/null 2>&1; then
  gh release upload "$tag" "$koren" "$loader" --clobber
else
  # shellcheck disable=SC2086
  gh release create "$tag" "$koren" "$loader" --title "$title" --notes "$notes" $pre
fi

echo "Done: ${tag}"
echo "Commit the bump:  git add ${INFO} ${BUILDS} && git commit -m \"Release ${tag}\""
