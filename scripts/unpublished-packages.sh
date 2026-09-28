#!/usr/bin/env bash
# Prints "<PackageId> <Version>" for every package under src/ whose current <Version> is not on nuget.org yet.
# Order is dependency-safe: packages with no NinjaVault project reference come first
# (NinjaVault.Http before NinjaVault.Cdn).
set -euo pipefail

cd "$(dirname "$0")/.."

# nuget.org normalizes versions: a 4th part of 0 is dropped (1.2.3.0 -> 1.2.3).
normalize() {
    local v
    v=$(echo "$1" | tr '[:upper:]' '[:lower:]')
    if [[ "$v" =~ ^([0-9]+\.[0-9]+\.[0-9]+)\.0$ ]]; then
        v="${BASH_REMATCH[1]}"
    fi
    echo "$v"
}

emit() {
    local csproj="$1" id version lower published
    id=$(sed -n 's:.*<PackageId>\(.*\)</PackageId>.*:\1:p' "$csproj" | head -n1)
    version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj" | head -n1)
    if [[ -z "$id" || -z "$version" ]]; then
        return 0
    fi

    lower=$(echo "$id" | tr '[:upper:]' '[:lower:]')
    published=$(curl -fsSL "https://api.nuget.org/v3-flatcontainer/$lower/index.json" 2>/dev/null || echo '{"versions":[]}')
    if ! grep -q "\"$(normalize "$version")\"" <<< "$published"; then
        echo "$id $version"
    fi
}

for csproj in src/*/*.csproj; do
    if ! grep -q 'ProjectReference Include=".*NinjaVault' "$csproj"; then
        emit "$csproj"
    fi
done

for csproj in src/*/*.csproj; do
    if grep -q 'ProjectReference Include=".*NinjaVault' "$csproj"; then
        emit "$csproj"
    fi
done
