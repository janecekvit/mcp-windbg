#!/usr/bin/env bash
# Formats, validates and renders every docs/**/*.d2 to the .svg next to it.
# --check: fail on an unformatted or invalid source, or a stale or missing SVG (CI).
# Install D2 the way CI does, so the SVGs embed the same version string:
#   go install oss.terrastruct.com/d2@v0.7.2
set -euo pipefail
cd "$(dirname "$0")/.."

check=false
if [[ "${1:-}" == "--check" ]]; then check=true; fi

mapfile -t sources < <(find docs -name '*.d2' | sort)
if [[ ${#sources[@]} -eq 0 ]]; then
  echo "no D2 sources under docs/"
  exit 0
fi

for source in "${sources[@]}"; do
  if $check; then d2 fmt --check "$source"; else d2 fmt "$source"; fi
  d2 validate "$source"
  d2 "$source" "${source%.d2}.svg"
done

if $check; then
  stale=$(git diff --name-only -- 'docs/*.svg'; git ls-files --others --exclude-standard -- 'docs/*.svg')
  if [[ -n "$stale" ]]; then
    echo "$stale" >&2
    echo "Rendered SVGs are stale or missing. Run Scripts/render-diagrams.sh and commit the result." >&2
    exit 1
  fi
fi
