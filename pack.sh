#!/usr/bin/env bash
# Builds DarkwoodMP and produces ready-to-extract zips for Windows and Linux in dist/.
set -euo pipefail
BEPINEX_VER=5.4.23.2
ROOT="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}" PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1

dotnet build "$ROOT/src/DarkwoodMP" -c Release -p:DeployToGame=false
DLL="$ROOT/src/DarkwoodMP/bin/Release/DarkwoodMP.dll"

CACHE="$ROOT/.cache"; mkdir -p "$CACHE" "$ROOT/dist"
# win_x86 too: whether Darkwood.exe is 64-bit on every store and version isn't known (the x64 proxy DLL does
# nothing in a 32-bit game). DarkwoodMP-win.zip is x64; DarkwoodMP-win_x86.zip is for a 32-bit Darkwood.exe.
for plat in win_x64 win_x86 linux_x64; do
  zip="$CACHE/BepInEx_${plat}_${BEPINEX_VER}.zip"
  [ -f "$zip" ] || curl -sSLf -o "$zip" "https://github.com/BepInEx/BepInEx/releases/download/v${BEPINEX_VER}/BepInEx_${plat}_${BEPINEX_VER}.zip"
  stage="$(mktemp -d)"
  unzip -q "$zip" -d "$stage"
  mkdir -p "$stage/BepInEx/plugins/DarkwoodMP"
  cp "$DLL" "$stage/BepInEx/plugins/DarkwoodMP/"
  if [ "$plat" = linux_x64 ]; then
    sed -i 's/^executable_name=""/executable_name="Darkwood.x86_64"/' "$stage/run_bepinex.sh"
    chmod +x "$stage/run_bepinex.sh"
    cp "$ROOT/scripts/darkwood_mp.sh" "$stage/" && chmod +x "$stage/darkwood_mp.sh"
  fi
  # Installation and use instructions; CRLF on Windows so every editor shows the lines.
  if [ "${plat#win_}" != "$plat" ]; then sed 's/$/\r/' "$ROOT/scripts/README-win.txt" > "$stage/DarkwoodMP-README.txt"
  else cp "$ROOT/scripts/README-linux.txt" "$stage/DarkwoodMP-README.txt"; fi
  # Licenses of the mod and of everything BepInEx bundles (its zip has none); CRLF on Windows as above.
  mkdir -p "$stage/DarkwoodMP-LICENSES"
  for f in "$ROOT"/LICENSES/*.txt "$ROOT/LICENSE"; do
    name="$(basename "$f")"; [ "$name" = LICENSE ] && name=DarkwoodMP.txt
    if [ "${plat#win_}" != "$plat" ]; then sed 's/$/\r/' "$f" > "$stage/DarkwoodMP-LICENSES/$name"; else cp "$f" "$stage/DarkwoodMP-LICENSES/$name"; fi
  done
  out="$ROOT/dist/DarkwoodMP-${plat%_x64}.zip"
  rm -f "$out"; (cd "$stage" && zip -qr "$out" .)
  rm -rf "$stage"
  echo "Built $out"
done
