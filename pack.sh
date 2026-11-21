#!/usr/bin/env bash
# Builds DarkwoodMP and produces ready-to-extract zips for Windows and Linux in dist/.
set -euo pipefail
BEPINEX_VER=5.4.23.2
ROOT="$(cd "$(dirname "$0")" && pwd)"
. "$ROOT/tools/lib.sh" # safe_rm
# Each zip is staged in a mktemp folder, removed when it's done or when the script stops on an error.
TMPROOT="$(realpath -e "${TMPDIR:-/tmp}")"; stage=""
trap '[ -z "$stage" ] || safe_rm "$TMPROOT" "$stage"' EXIT
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build "$ROOT/src/DarkwoodMP" -c Release -p:DeployToGame=false -clp:ErrorsOnly
DLL="$ROOT/src/DarkwoodMP/bin/Release/DarkwoodMP.dll"

CACHE="$ROOT/.cache"; mkdir -p "$CACHE" "$ROOT/dist"
# text SRC DEST: a text file into the zip; CRLF on Windows so every editor shows the lines.
text() { if [ "${plat#win_}" != "$plat" ]; then sed 's/$/\r/' "$1" > "$2"; else cp "$1" "$2"; fi; }
# win_x86 too: whether Darkwood.exe is 64-bit on every store and version isn't known (the x64 proxy DLL does
# nothing in a 32-bit game). DarkwoodMP-win.zip is x64; DarkwoodMP-win_x86.zip is for a 32-bit Darkwood.exe.
for plat in win_x64 win_x86 linux_x64; do
  zip="$CACHE/BepInEx_${plat}_${BEPINEX_VER}.zip"
  [ -f "$zip" ] || curl -sSLf -o "$zip" "https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VER}/BepInEx_${plat}_${BEPINEX_VER}.zip"
  stage="$(mktemp -d "$TMPROOT/darkwoodmp-pack.XXXXXX")"
  unzip -q "$zip" -d "$stage"
  mkdir -p "$stage/BepInEx/plugins/DarkwoodMP"
  cp "$DLL" "$(dirname "$DLL")/DarkwoodMP.Core.dll" \
    "$stage/BepInEx/plugins/DarkwoodMP/"
  if [ "$plat" = linux_x64 ]; then
    sed -i 's/^executable_name=""/executable_name="Darkwood.x86_64"/' "$stage/run_bepinex.sh"
    chmod +x "$stage/run_bepinex.sh"
    cp "$ROOT/scripts/darkwood_mp.sh" "$stage/" && chmod +x "$stage/darkwood_mp.sh"
  fi
  # Installation and use instructions.
  readme=README-linux.txt; [ "${plat#win_}" != "$plat" ] && readme=README-win.txt
  text "$ROOT/scripts/$readme" "$stage/DarkwoodMP-README.txt"
  # Licenses of the mod and of everything BepInEx bundles (its zip has none).
  mkdir -p "$stage/DarkwoodMP-LICENSES"
  for f in "$ROOT"/LICENSES/*.txt "$ROOT/LICENSE"; do
    name="$(basename "$f")"; [ "$name" = LICENSE ] && name=DarkwoodMP.txt
    text "$f" "$stage/DarkwoodMP-LICENSES/$name"
  done
  out="$ROOT/dist/DarkwoodMP-${plat%_x64}.zip"
  rm -f "$out"; (cd "$stage" && zip -qr "$out" .)
  # Read the list first: pack.sh runs with pipefail, and `unzip -l | grep -q`
  # fails at random when grep stops reading early.
  files="$(unzip -Z1 "$out")"
  for f in DarkwoodMP.dll DarkwoodMP.Core.dll; do
    grep -qxF "BepInEx/plugins/DarkwoodMP/$f" <<<"$files" \
      || { echo "$f is missing from $out"; exit 1; }
  done
  # DarkwoodMP.MapTests.dll (the map test steps) goes to the test game folders
  # only, never to players.
  if grep -q MapTests <<<"$files"; then
    echo "the map tests (DarkwoodMP.MapTests.dll) are in $out"; exit 1
  fi
  safe_rm "$TMPROOT" "$stage"; stage=""
  echo "Built $out"
done
