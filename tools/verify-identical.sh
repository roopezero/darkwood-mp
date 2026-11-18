#!/bin/bash
# tools/verify-identical.sh BASE [HEAD]
# Builds two commits and fails unless their DLLs are byte-identical. This is
# the proof that a whitespace-only stage changed nothing in the compiled mod.
set -euo pipefail
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:~/.dotnet/tools:$PATH
cd "$(git rev-parse --show-toplevel)"
mkdir -p .cache
exec 9>.cache/verify-identical.lock
flock -n 9 || { echo "verify-identical is already running"; exit 1; }

cleanup() {
    git worktree remove --force .cache/verify-identical/base 2>/dev/null || true
    git worktree remove --force .cache/verify-identical/head 2>/dev/null || true
    rm -rf .cache/verify-identical
}
trap cleanup EXIT
cleanup
mkdir -p .cache/verify-identical

build() {   # REV NAME
    git worktree add --detach -q ".cache/verify-identical/$2" "$1"
    local root="$PWD/.cache/verify-identical/$2"
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
        "-p:OutDir=$PWD/.cache/verify-identical/out-$2/"
}

build "$1" base
build "${2:-HEAD}" head

# The same DLLs on both sides, and every one the same to the byte.
diff <(ls .cache/verify-identical/out-base | grep '\.dll$') \
     <(ls .cache/verify-identical/out-head | grep '\.dll$')
for dll in .cache/verify-identical/out-head/*.dll; do
    name="$(basename "$dll")"
    cmp ".cache/verify-identical/out-base/$name" "$dll"
    echo "identical: $name $(sha256sum "$dll" | cut -c1-16)"
done
