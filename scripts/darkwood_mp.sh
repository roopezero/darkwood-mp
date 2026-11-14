#!/bin/sh
# DarkwoodMP launcher for Steam on Linux.  Steam launch options:  ./darkwood_mp.sh %command%
# The BepInEx loader (libdoorstop) must only be preloaded into the game itself, not into Steam's
# runtime/container launchers (that breaks them). So we swap the game executable in Steam's
# command line for this script in "--inner" mode, and only enable the loader there.
GAME_DIR="$(cd "$(dirname "$0")" && pwd -P)"
GAME_EXE="$GAME_DIR/Darkwood.x86_64"

# run_game ARGS...: the game itself, with the loader. The loader is preloaded by its bare name and found through
# LD_LIBRARY_PATH, as BepInEx's run_bepinex.sh does: LD_PRELOAD splits on spaces, so a full path to a game folder
# with a space in it (a Steam library on "/mnt/My Games") would load no mod at all.
run_game() {
    export DOORSTOP_ENABLED=1
    export DOORSTOP_TARGET_ASSEMBLY="$GAME_DIR/BepInEx/core/BepInEx.Preloader.dll"
    export DOORSTOP_IGNORE_DISABLED_ENV=0
    export LD_LIBRARY_PATH="$GAME_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export LD_PRELOAD="libdoorstop.so${LD_PRELOAD:+:$LD_PRELOAD}"
    exec "$GAME_EXE" "$@"
}

if [ "${1:-}" = "--inner" ]; then
    shift
    run_game "$@"
fi

# Launched without arguments: run the game directly.
[ $# -eq 0 ] && run_game

# Rebuild Steam's command, replacing the game executable with "this-script --inner".
n=$#; i=0; found=0
while [ $i -lt $n ]; do
    a="$1"; shift; i=$((i+1))
    case "$a" in
        *Darkwood.x86_64) set -- "$@" "$GAME_DIR/darkwood_mp.sh" --inner; found=1 ;;
        *) set -- "$@" "$a" ;;
    esac
done
[ $found -eq 0 ] && echo "darkwood_mp.sh: Darkwood.x86_64 not found in launch command; starting without mods" >&2
exec "$@"
