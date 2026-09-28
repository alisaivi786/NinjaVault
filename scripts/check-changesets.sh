#!/usr/bin/env bash
# Fails when a packable project's current <Version> has no matching changesets/<PackageId>/<Version>.md.
# Usage: scripts/check-changesets.sh [PackageId]   (no argument = check every package under src/)
set -euo pipefail

cd "$(dirname "$0")/.."
filter="${1:-}"
missing=0

for csproj in src/*/*.csproj; do
    id=$(sed -n 's:.*<PackageId>\(.*\)</PackageId>.*:\1:p' "$csproj" | head -n1)
    version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -n1)

    if [[ -z "$id" || -z "$version" ]]; then
        continue
    fi

    if [[ -n "$filter" && "$filter" != "$id" ]]; then
        continue
    fi

    changeset="changesets/$id/$version.md"
    if [[ -f "$changeset" ]]; then
        echo "ok       $id $version"
    else
        echo "MISSING  $id $version  (expected: $changeset - run: make changeset PACKAGE=$id)"
        missing=1
    fi
done

exit "$missing"
