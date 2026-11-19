#!/bin/bash
# tools/il-compare.sh BASE HEAD MANIFEST [--list]
# Builds two commits the way verify-identical.sh does and compares their DLLs
# against the manifest a rewrite rule wrote (guide section 4.5). Exit code 0
# means every DLL is either identical to the byte or differs only inside the
# manifest's members. --list also prints each changed method with the
# manifest entry it was credited to. Run it under the machine's build lock
# when other builds may be running.
set -euo pipefail
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:~/.dotnet/tools:$PATH

list=()
if [ $# -eq 4 ] && [ "$4" = "--list" ]; then
    list=(--list)
elif [ $# -ne 3 ]; then
    echo "usage: tools/il-compare.sh BASE HEAD MANIFEST [--list]"
    exit 2
fi
manifest="$(realpath "$3")"
if [ ! -f "$manifest" ]; then
    echo "No such manifest: $3"
    exit 2
fi

cd "$(git rev-parse --show-toplevel)"
mkdir -p .cache
exec 9>.cache/il-compare.lock
flock -n 9 || { echo "il-compare is already running"; exit 1; }

cleanup() {
    git worktree remove --force .cache/il-compare/base 2>/dev/null || true
    git worktree remove --force .cache/il-compare/head 2>/dev/null || true
    rm -rf .cache/il-compare
}
trap cleanup EXIT
cleanup
mkdir -p .cache/il-compare

build() {   # REV NAME
    git worktree add --detach -q ".cache/il-compare/$2" "$1"
    local root="$PWD/.cache/il-compare/$2"
    local project="$root/src/DarkwoodMP"
    if [ -d "$root/src/DarkwoodMP.MapTests" ]; then
        project="$root/src/DarkwoodMP.MapTests"
    fi
    dotnet build "$project" -c Release -v q -clp:ErrorsOnly \
        -nodeReuse:false -p:UseSharedCompilation=false \
        -p:DeployToGame=false \
        -p:RunAnalyzers=false -p:EnforceCodeStyleInBuild=false \
        -p:Deterministic=true \
        -p:DebugType=none -p:DebugSymbols=false \
        -p:IncludeSourceRevisionInInformationalVersion=false \
        "-p:PathMap=$root=/_/" \
        "-p:OutDir=$PWD/.cache/il-compare/out-$2/"
}

build "$1" base
build "$2" head

# The tool of this checkout, not of the commits being compared.
dotnet build tools/DarkwoodMP.Codemod -c Release -v q -clp:ErrorsOnly \
    -nodeReuse:false -p:UseSharedCompilation=false
tool="$PWD/tools/DarkwoodMP.Codemod/bin/Release/net8.0/DarkwoodMP.Codemod.dll"

# The same DLLs on both sides; each one identical or inside the manifest.
diff <(ls .cache/il-compare/out-base | grep '\.dll$') \
     <(ls .cache/il-compare/out-head | grep '\.dll$')
status=0
for dll in .cache/il-compare/out-head/*.dll; do
    name="$(basename "$dll")"
    if cmp -s ".cache/il-compare/out-base/$name" "$dll"; then
        echo "identical: $name"
        continue
    fi
    echo "differs: $name"
    if ! dotnet "$tool" ildiff ".cache/il-compare/out-base/$name" "$dll" \
        --manifest "$manifest" ${list[@]+"${list[@]}"}; then
        status=1
    fi
done
exit "$status"
