#!/usr/bin/env bash
# Creates changesets/<PackageId>/<Version>.md from the template, using the csproj's current <Version>.
# Usage: scripts/new-changeset.sh <PackageId> [Patch|Minor|Major]
set -euo pipefail

cd "$(dirname "$0")/.."
id="${1:?Usage: scripts/new-changeset.sh <PackageId> [Patch|Minor|Major]}"
type="${2:-Patch}"
csproj="src/$id/$id.csproj"

if [[ ! -f "$csproj" ]]; then
    echo "No project at $csproj" >&2
    exit 1
fi

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -n1)
target="changesets/$id/$version.md"

if [[ -f "$target" ]]; then
    echo "$target already exists - bump <Version> in $csproj first." >&2
    exit 1
fi

mkdir -p "changesets/$id"
sed -e "s/{{PackageId}}/$id/g" -e "s/{{Version}}/$version/g" -e "s/{{Date}}/$(date +%F)/g" -e "s/{{Type}}/$type/g" \
    changesets/_template.md > "$target"
echo "Created $target - fill in Changes / Why / Breaking Changes."
