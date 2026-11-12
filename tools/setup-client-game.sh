#!/usr/bin/env bash
# Makes (or refreshes) a second Darkwood folder for two-game tests, so a client game can run beside the host game.
#
#   tools/setup-client-game.sh [N]
#
# N (default 1) picks which client game: 1 is ~/.cache/darkwood-mp/client-game (home client-home), 2 to 4 are
# client-game-N (home client-home-N), for the four-client `map4` test. Each has its own player key and
# PlayerName ClientN.
#
# The game allows one copy per folder (its lock is <game>/unity.lock), so the client gets its own folder:
# copies of the executable, the Unity player, the loader and the launcher, a link to the real Darkwood_Data
# (only read), and a BepInEx folder of its own with the mod. Launch it with HOME set to $CLIENT_HOME so its
# Player.log, settings and saves are its own too (and Steam isn't found there: it plays as on GOG).
#
# Environment: GAME_DIR (the Darkwood folder), CLIENT_GAME and CLIENT_HOME (override the folders N picks).
# Safe to run again: it recopies what changed (a game update, a new build of the mod) and keeps the client's
# player key, name and saves.
set -euo pipefail

GAME_DIR="${GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Darkwood}"
N="${1:-1}"
SUF=""; [ "$N" = 1 ] || SUF="-$N"
C="${CLIENT_GAME:-$HOME/.cache/darkwood-mp/client-game$SUF}"
CH="${CLIENT_HOME:-$HOME/.cache/darkwood-mp/client-home$SUF}"

[ -d "$GAME_DIR/BepInEx/core" ] || { echo "no BepInEx in $GAME_DIR (install the mod first)"; exit 1; }
mkdir -p "$C" "$CH"

for f in Darkwood.x86_64 UnityPlayer.so libdoorstop.so darkwood_mp.sh steam_appid.txt; do
  [ -e "$GAME_DIR/$f" ] || continue
  cmp -s "$GAME_DIR/$f" "$C/$f" 2>/dev/null || cp -a "$GAME_DIR/$f" "$C/$f"
done
[ "$(readlink "$C/Darkwood_Data" 2>/dev/null)" = "$GAME_DIR/Darkwood_Data" ] || { rm -rf "$C/Darkwood_Data"; ln -s "$GAME_DIR/Darkwood_Data" "$C/Darkwood_Data"; }

mkdir -p "$C/BepInEx/config" "$C/BepInEx/plugins/DarkwoodMP" "$C/BepInEx/patchers"
for d in core patchers; do
  [ -d "$GAME_DIR/BepInEx/$d" ] && cp -a "$GAME_DIR/BepInEx/$d/." "$C/BepInEx/$d/"
done
# The mod: the build deploys there too (DarkwoodMP.csproj), and this copies the game's current one in any case.
cp -a "$GAME_DIR/BepInEx/plugins/." "$C/BepInEx/plugins/"
[ -f "$C/BepInEx/config/BepInEx.cfg" ] || cp "$GAME_DIR/BepInEx/config/BepInEx.cfg" "$C/BepInEx/config/" 2>/dev/null || true

# The mod's settings: the host's, with a name of its own, the first time.
CFG="$C/BepInEx/config/com.darkwoodmp.coop.cfg"
if [ ! -f "$CFG" ] && [ -f "$GAME_DIR/BepInEx/config/com.darkwoodmp.coop.cfg" ]; then
  cp "$GAME_DIR/BepInEx/config/com.darkwoodmp.coop.cfg" "$CFG"
fi
# Every time, so each client game is told apart in the others' logs (Client1..Client4).
[ -f "$CFG" ] && sed -i "s/^PlayerName = .*$/PlayerName = Client$N/" "$CFG"
# A player key of its own: the same key as the host's would be the same player (and refused).
KEY="$C/BepInEx/config/DarkwoodMP.player.key"
if [ -f "$KEY" ] && cmp -s "$KEY" "$GAME_DIR/BepInEx/config/DarkwoodMP.player.key"; then rm -f "$KEY"; fi
[ -f "$KEY" ] || python3 -c "import secrets; print(secrets.token_hex(32), end='')" >"$KEY"

# The game's settings (Unity's prefs) the first time: client 1's, else yours (only read). A game started with none
# never got its main menu ready to join from (a fresh client-home-2 timed out waiting for it).
PREFS=".config/unity3d/Acid Wizard Studio/Darkwood/prefs"
if [ ! -f "$CH/$PREFS" ]; then
  mkdir -p "$(dirname "$CH/$PREFS")"
  for from in "$HOME/.cache/darkwood-mp/client-home/$PREFS" "$HOME/$PREFS"; do
    [ -f "$from" ] && [ "$from" != "$CH/$PREFS" ] && { cp "$from" "$CH/$PREFS"; break; }
  done
fi

echo "client game: $C (home $CH)"
