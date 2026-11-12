#!/usr/bin/env bash
# In-game tests for DarkwoodMP, against the real game, with a scripted peer (tools/tests/*.py) as the other player,
# or (map tests) a second real game. docs/MULTIPLAYER_TESTING_GUIDE.md has the batteries, isolation and timings.
#
#   tools/run-test.sh ITEM...        tests, groups and map batteries, mixed, in any order, e.g.
#                                    tools/run-test.sh map:enemies map:world host-world
#   tools/run-test.sh --list         every test, group and map battery
#   tools/run-test.sh --plan ITEM... what ITEM... would run, in order, without running it
#
# Items:
#   a test name        (below)
#   all                every scripted-peer test (the 25 of $ALL; not shots, options, map, map4)
#   full               all, then the full two-game map
#   net combat world chars story menus authority      groups of scripted-peer tests (--list shows them)
#   map                the full two-game scenario (tools/tests/map_scenario.py: every module in one world)
#   map:NAME           one map battery: tools/tests/map_scenario_NAME.py alone, from a fresh test world and a fresh join
#   maps               every map battery, one after another (each in its own fresh world)
# A list is run once each, in the order given. A client test whose input is missing from $WORK gets it made first:
# capture (world.pkl) before any client test, host-world (world_rec.pkl) before client-world.
#
# One game runs the whole list: the script starts it with --dwmp-test-control and, before each test, sends it the
# test's title (shown at the top of the screen), a reset (back to a clean main menu) and the test's arguments.
# After each one: the game is sent back to its main menu, a client game stopped unless a map battery comes next,
# leftover downloaded worlds removed from the mod's caches, and your saves compared with their hashes from the start.
# At the end, a table: each item's result, time, first failure, its log, and the command that reruns it alone.
#   PASS  the test has a verdict of its own and it passed (map, map:*, map4, options, menu-entry, capture, host-world)
#   RAN   no failure seen (no FAIL line, no time-out, no exception), but its printed lines are the result: read them
#   SKIP  the test can't run here (steam without the Steam client): the reason is shown
#   FAIL  a FAIL line, a time-out, an exception in Player.log, a failed step or differing dumps
#
# Host tests (the game hosts a COPY of your save; your own saves are never opened):
#   handshake     refusals (password, version, game, same key) and joins, against the TCP host
#   capture       download the host's world into $WORK/world.pkl (needed by the client tests)
#   host-combat   a spawned enemy, the dummy and a scripted fighter: threat, bites, hits, blood, noise, area damage,
#                 swings and aims (PlayerCue), invisibility and wards, player fx
#   host-world    scripted door/loot/drop/flags/trap/lamp/trader/NPC death; records $WORK/world_rec.pkl
#   host-chars    a new character, then the same player's character back after the host saved; then dead for good, kept dead on rejoin
#   host-progress shared essence and level: a scripted player cooks essence, says a level, spends skill points, resets its skills (mornings, once a day)
#   host-death    the host's character dies with a player in the world (bag shared, no world reset)
#   host-elsewhere  the host away as in a separate location, and back: enemies near the dummy keep running, its puppet hidden;
#                 a knocked object near the host sent where it came to rest; a location a joiner asks about loaded at the spot promised
#   host-reload   the host reloads its world; the player is told to reload
#   host-menu     the host goes back to the main menu: the player gets HostAway, the server stops
#   host-chapter  a scripted player asks for the next chapter: refused from far, for a wrong chapter, an unknown event, the
#                 epilogue; allowed beside it; then ChapterStart, WorldReload and the new world offered
#   host-end      the host starts the epilogue: the player gets GameEnd, the server stops
#   host-authority  a scripted client's requests the host allows or refuses: reach, state, damage, a disarm roll, speed
#   menu-entry    the menu list takes a click (Join Game in the main menu, Multiplayer in the pause menu) and opens the panel; screenshots into $WORK/shots
#   shots         screenshots into $WORK/shots: the world, the pause menu, its confirm dialog, the co-op panel
#   options       the Options window from both menus: a volume and a key stored in the game's settings, then put back
#                 (the prefs file compared); screenshots of every tab at 1920x1080 and 1280x720 into $WORK/options
#   drag          a window moved and resized by made-up mouse events, slowly and fast, in the main menu (issue #18)
#   steam         host a Steam lobby with your account and leave
# Client tests (the game joins a scripted host, which serves $WORK/world.pkl):
#   client-world    replays $WORK/world_rec.pkl (host-world makes it)
#   client-enemies  the host's enemies shown, moved and despawned
#   client-chars    a new character, then a changed one sent back (position, health, items, fewer slots)
#   client-reload   the host says its world changed: the game rejoins
#   client-away     the host sends HostAway: the game leaves and goes back to the main menu
#   client-chapter  ChapterStart: the game sends its character and waits; then WorldReload and it rejoins
#   client-end      GameEnd (the end screen): the game leaves, shows it, the credits, the main menu
#   client-epilogue GameEnd (the epilogue): the game leaves and plays the epilogue (or the end screen), then the main menu
#   client-drop     the host's connection just closes: the game goes back to the main menu
#   client-authority  the host refuses our player's door action: the door and the rope it used come back
# Two-game tests (a second, real game joins as the client, from its own folder: tools/setup-client-game.sh):
#   map, map:NAME the host plays the test world (a fresh copy in the mod's cache, --dwmp-testworld --dwmp-reset), the
#                 client game joins it over Direct; the scenario's steps run in both, both dump their shared-world
#                 state and tools/tests/mapcompare.py compares the dumps (results in $WORK/map or $WORK/map-NAME).
#                 Between map items the client game is kept running and sent back to its main menu (MAP_FRESH_CLIENT=1
#                 starts it again for each instead).
#   map4          the same with MAP_CLIENTS client games (default 3; 4 is the most the mod takes, about 6 GB of RAM for
#                 five games), from tools/setup-client-game.sh 1..N, playing tools/tests/map4_scenario.py (results in
#                 $WORK/map4). MAP_SCENARIO=MODULE runs another tools/tests/MODULE.py in map or map4.
#
# Environment: GAME_DIR (the Darkwood folder), WORK (scratch folder, default /tmp/darkwoodmp-test).
#              LOAD_SHOTS=DIR: screenshots of each loading-screen step into DIR/host and DIR/client.
#              DWMP_ARGS: more --dwmp-* arguments for the game in host and client tests (e.g. --dwmp-window 1280x720).
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
T="$ROOT/tools/tests"
GAME_DIR="${GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Darkwood}"
DATA="${DARKWOOD_DATA:-$HOME/.config/unity3d/Acid Wizard Studio/Darkwood}"
SAVES="$DATA/1_4Save"
WORK="${WORK:-/tmp/darkwoodmp-test}"
LOG="$GAME_DIR/BepInEx/LogOutput.log"
ULOG="$DATA/Player.log"
PY="python3 -I"
CTL=27799

ALL="capture handshake host-combat host-world client-world client-enemies client-chars host-chars host-progress host-death host-elsewhere host-reload host-menu host-authority menu-entry client-reload client-chapter client-away client-end client-epilogue client-drop client-authority host-end host-chapter steam"
declare -A DESC=(
  [handshake]="refusals (password, version, game) and a valid join"
  [capture]="download the host's world for the client tests"
  [host-combat]="threat, bites, hits, blood, noise, area damage, swings and aims, invisibility, wards and player fx with a scripted fighter"
  [host-world]="doors, loot, drops, story, traps, lamps, dragging, fire, workbench"
  [host-chars]="a player's character kept by the host and given back"
  [host-progress]="shared essence and levels, skill points spent, a skill reset in the morning once a day"
  [host-death]="the host dies with a player in the world"
  [host-elsewhere]="the host in a separate location: the world kept running around the dummy"
  [host-reload]="the host reloads its world; the player is told to reload"
  [host-menu]="the host goes to the main menu: the server stops"
  [host-authority]="what a player asks for, allowed or refused by the host"
  [host-chapter]="a player sets off the next chapter: everyone follows the host into it"
  [host-end]="the host reaches the game's end: the player sees it too, the server stops"
  [client-chapter]="the host starts a chapter: the game waits, then loads it"
  [client-end]="the host's end screen: shown here, then the credits and the main menu"
  [client-epilogue]="the host's epilogue: played here, then the main menu"
  [menu-entry]="Join Game and Multiplayer open the panel from both menus"
  [shots]="screenshots of the world, the pause menu and the co-op panel"
  [options]="the Options window: settings stored the game's way and put back, every tab at two sizes"
  [drag]="a window dragged and resized slowly and fast moves exactly as far as the mouse"
  [steam]="host a Steam lobby and leave"
  [client-world]="the host's world changes replayed in the game"
  [client-enemies]="the host's enemies shown, moved and despawned"
  [client-chars]="a new character, then a changed one from the host"
  [client-reload]="the host's world changed: the game rejoins"
  [client-away]="HostAway: back to the main menu"
  [client-drop]="the host's connection closes: back to the main menu"
  [client-authority]="a refused action rolled back in our game"
  [map]="two games: every map module in one world, then their world states compared"
  [map4]="five games: the host and four clients, near and far from each other, their world states compared"
)
# Groups of scripted-peer tests. Between them they hold every test of $ALL, and shots and options.
declare -A GROUP=(
  [net]="handshake host-menu host-reload client-reload client-away client-drop steam"
  [combat]="host-combat host-elsewhere client-enemies"
  [world]="capture host-world client-world host-death"
  [chars]="host-chars client-chars host-progress"
  [story]="host-chapter client-chapter host-end client-end client-epilogue"
  [menus]="menu-entry shots options drag"
  [authority]="host-authority client-authority"
)
GROUP_NAMES="net combat world chars story menus authority"
# Tests with a verdict of their own (PASS rather than RAN when nothing failed).
VERDICT=" map map4 options drag menu-entry capture host-world "

# Map batteries: every tools/tests/map_scenario_NAME.py is the battery map:NAME. "maps" runs those without
# "IN_MAPS = False" (a module that only repeats the whole scenario, as map_scenario_dogfull.py does).
map_batteries() { local f; for f in "$T"/map_scenario_*.py; do f="${f##*/map_scenario_}"; echo "${f%.py}"; done; }
in_maps() { ! grep -q "^IN_MAPS *= *False" "$T/map_scenario_$1.py"; }
battery_doc() { $PY -c 'import ast, sys; d = ast.get_docstring(ast.parse(open(sys.argv[1]).read())) or ""; d = " ".join(d.split()).replace("Map test steps for ", "", 1); print(d.split("; same format")[0].split(". ")[0][:110])' "$T/map_scenario_$1.py" 2>/dev/null; }

list() {
  echo "Groups:"
  echo "  all        $ALL"
  echo "  full       all, then map"
  local g; for g in $GROUP_NAMES; do printf "  %-10s %s\n" "$g" "${GROUP[$g]}"; done
  echo "  maps       every map battery below except those marked (not in maps)"
  echo "Tests:"
  local t; for t in $ALL shots options drag map map4; do printf "  %-16s %s\n" "$t" "${DESC[$t]}"; done
  echo "Map batteries (tools/tests/map_scenario_NAME.py, each from a fresh test world and join):"
  local b; for b in $(map_batteries); do printf "  %-20s %s%s\n" "map:$b" "$(in_maps $b || echo '(not in maps) ')" "$(battery_doc $b)"; done
}
PLAN=""; [ "${1:-}" = --plan ] && { PLAN=1; shift; }
case "${1:-}" in
  --list|-l) list; exit 0 ;;
  ""|--help|-h) sed -n '2,/^set -uo/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; exit 0 ;;
esac

# What to run: the items expanded, each once, in order; then the inputs the client tests need put before them.
ITEMS=()
add() { local i; for i in "${ITEMS[@]}"; do [ "$i" = "$1" ] && return; done; ITEMS+=("$1"); }
for a in "$@"; do
  case "$a" in
    all) for t in $ALL; do add $t; done ;;
    full) for t in $ALL map; do add $t; done ;;
    maps) for b in $(map_batteries); do in_maps $b && add "map:$b"; done ;;
    map:*) [ -f "$T/map_scenario_${a#map:}.py" ] || { echo "unknown map battery $a (tools/run-test.sh --list)"; exit 1; }; add "$a" ;;
    *) if [ -n "${GROUP[$a]:-}" ]; then for t in ${GROUP[$a]}; do add $t; done
       elif [ -n "${DESC[$a]:-}" ]; then add "$a"
       else echo "unknown test or group $a (tools/run-test.sh --list)"; exit 1; fi ;;
  esac
done
needs() { # needs PREREQ FILE PATTERN: PREREQ put before the first item matching PATTERN, unless it is already
  # earlier in the list, or isn't in the list and FILE is in WORK (an item after it is moved before)
  local i n=-1 p=-1
  for i in "${!ITEMS[@]}"; do [ "${ITEMS[$i]}" = "$1" ] && p=$i; [ $n -lt 0 ] && [[ "${ITEMS[$i]}" == $3 ]] && n=$i; done
  [ $n -ge 0 ] || return 0
  [ $p -ge 0 ] && [ $p -lt $n ] && return 0
  [ $p -lt 0 ] && [ -f "$WORK/$2" ] && return 0
  [ $p -ge 0 ] && { unset 'ITEMS[p]'; ITEMS=("${ITEMS[@]}"); }
  ITEMS=("${ITEMS[@]:0:$n}" "$1" "${ITEMS[@]:$n}")
  echo "(added $1 before ${ITEMS[$((n + 1))]}: it makes \$WORK/$2)"
}
needs host-world world_rec.pkl client-world
needs capture world.pkl "client-*"

[ -z "$PLAN" ] || { echo "${ITEMS[*]}"; exit 0; }
mkdir -p "$WORK/logs"
die() { echo "FAIL: $*"; [ "$BASH_SUBSHELL" -gt 0 ] || game_stop; exit 1; }
[ -x "$GAME_DIR/darkwood_mp.sh" ] || { echo "FAIL: no $GAME_DIR/darkwood_mp.sh (install the mod first)"; exit 1; }
[ -d "$SAVES/prof1" ] || { echo "FAIL: no saved profile 1 in $SAVES (play once first)"; exit 1; }
# Without a display the game stops at "Loading in SingleInstance mode" ("Desktop is 0 x 0" in Player.log).
# From a shell outside the desktop session (an agent's), use the session's display :0 and its X authority file.
if [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
  xa="$(ls -t /run/user/$(id -u)/xauth_* 2>/dev/null | head -1)"
  if [ -S /tmp/.X11-unix/X0 ] && DISPLAY=:0 XAUTHORITY="${xa:-${XAUTHORITY:-}}" timeout 5 xdpyinfo >/dev/null 2>&1; then
    export DISPLAY=:0; [ -n "$xa" ] && export XAUTHORITY="$xa"; echo "(no display set: using DISPLAY=:0${xa:+ with $xa})"
  else
    echo "FAIL: no display: set DISPLAY (and XAUTHORITY) as the desktop session has them, e.g. DISPLAY=:0"; exit 1
  fi
fi

save_hash() { (cd "$SAVES" && find . -type f -exec sha256sum {} + | sort -k2); }
BEFORE="$(save_hash)"
rm -rf "$WORK/saves-backup"; cp -a "$SAVES" "$WORK/saves-backup" # just in case

game_pid() { local p d; d="$(cd "$GAME_DIR" && pwd -P)"; for p in $(pgrep -x Darkwood.x86_64); do [ "$(readlink /proc/$p/cwd)" = "$d" ] && echo $p; done; }
game_stop() { # stop the host game (not the client games) and wait until it has exited (at most 10 s)
  local p i; p="$(game_pid)"; [ -n "$p" ] || return 0; kill $p
  for i in $(seq 1 50); do [ -n "$(game_pid)" ] || return 0; sleep 0.2; done
}
size() { stat -c %s "$1" 2>/dev/null || echo 0; }
since() { tail -c +$(($2 + 1)) "$1" 2>/dev/null; } # since <file> <offset>: what was written to it during this test
bg_until() { # bg_until <pattern> <seconds> <command...>: run a scripted player until the game logs <pattern>, then stop it
  local pat="$1" secs="$2"; shift 2
  "$@" & local p=$!
  wait_log "$pat" "$secs"; sleep 1
  kill -TERM $p 2>/dev/null; wait $p
}
wait_log() { # wait_log <pattern> <seconds>: in what the mod logged during this test
  local i; for i in $(seq 1 $(($2 * 4))); do since "$LOG" "$LOG0" | grep -q "$1" && return 0; sleep 0.25; done
  echo "  (timed out waiting for \"$1\" in the log)"; return 1
}
# BepInEx writes its log file from a buffer it flushes every 2 s, so a read right after a test's last step can miss
# its last lines (host-reload's "Your world changed" did). flushed: wait 2.5 s, unless that was done in the last 2 s.
flushed() { local now; now=$(date +%s%N); [ $(( (now - ${FLUSH_T:-0}) / 1000000 )) -lt 2000 ] && return; sleep 2.5; FLUSH_T=$(date +%s%N); }
mod_log() { flushed; since "$LOG" "$LOG0" | grep "DarkwoodMP\]" | grep -v "\[sprite\]\|\[test control\]" | grep -E "$1"; }
ctl() { $PY "$T/ctl.py" $CTL "$@"; }

game_start() { # the one game for this run, at its main menu, taking commands
  game_stop; rm -f "$LOG"
  # The shell that waits on the game must not hold the test's output (a pipe into tee): tee would never end.
  (cd "$GAME_DIR" && setsid ./darkwood_mp.sh --inner --dwmp-test-control $CTL >"$WORK/game.out" 2>&1 &) </dev/null >/dev/null 2>&1
  LOG0=0
  wait_log "Main menu ready" 120 || { echo "  the end of Player.log (\"Desktop is 0 x 0\": no display):"; tail -5 "$ULOG" | sed 's/^/    /'; die "the game didn't reach its main menu"; }
}
game_alive() { [ -n "$(game_pid)" ]; }

# The client games, for the map tests: each from its own folder (and HOME, so its Player.log and saves are its own
# too), made by tools/setup-client-game.sh N. Client 1 is client-game (CLIENT_GAME/CLIENT_HOME override it),
# client N is client-game-N; client N takes commands on 27799 - N (27798, 27797, ...).
cgame() { [ "$1" = 1 ] && echo "${CLIENT_GAME:-$HOME/.cache/darkwood-mp/client-game}" || echo "$HOME/.cache/darkwood-mp/client-game-$1"; }
chome() { [ "$1" = 1 ] && echo "${CLIENT_HOME:-$HOME/.cache/darkwood-mp/client-home}" || echo "$HOME/.cache/darkwood-mp/client-home-$1"; }
cctl() { echo $((27799 - $1)); }
clog() { echo "$(cgame $1)/BepInEx/LogOutput.log"; }
culog() { echo "$(chome $1)/.config/unity3d/Acid Wizard Studio/Darkwood/Player.log"; }
client_pid() { local p d; d="$(cd "$(cgame $1)" 2>/dev/null && pwd -P)"; for p in $(pgrep -x Darkwood.x86_64); do [ "$(readlink /proc/$p/cwd)" = "$d" ] && echo $p; done; }
client_game_stop() { # client_game_stop N
  local p i; p="$(client_pid $1)"; [ -n "$p" ] || return 0; kill $p
  for i in $(seq 1 50); do [ -n "$(client_pid $1)" ] || return 0; sleep 0.2; done
}
declare -A CULOG0 CLOG0
client_game_start() { # client_game_start N <args...>: client game N, from its folder, taking commands on $(cctl N)
  local n=$1; shift
  client_game_stop $n
  local cg ch; cg="$(cgame $n)"; ch="$(chome $n)"
  CLIENT_GAME="$cg" CLIENT_HOME="$ch" GAME_DIR="$GAME_DIR" "$ROOT/tools/setup-client-game.sh" $n >/dev/null || die "couldn't set up client game $n"
  rm -rf "$cg/BepInEx/cache/DarkwoodMP/worlds/"* "$(clog $n)"
  mkdir -p "$(dirname "$(culog $n)")"; CULOG0[$n]=$(size "$(culog $n)"); CLOG0[$n]=0
  # The timeout is only a safety net (client_game_stop ends it): the full map scenario takes over an hour.
  (cd "$cg" && HOME="$ch" setsid timeout ${CLIENT_TIMEOUT:-7200} ./darkwood_mp.sh --inner --dwmp-test-control $(cctl $n) "$@" >"$WORK/client-game-$n.out" 2>&1 &) </dev/null >/dev/null 2>&1
}
client_join() { # client_join N <args...>: client game N joins with <args>: the running one sent back to its main menu
  # first (it keeps running between map items), or a new one started with them
  local n=$1; shift
  if [ -z "${MAP_FRESH_CLIENT:-}" ] && [ -n "$(client_pid $n)" ] && [ "$($PY "$T/ctl.py" $(cctl $n) reset 2>&1)" = "" ]; then
    rm -rf "$(cgame $n)/BepInEx/cache/DarkwoodMP/worlds/"*
    CULOG0[$n]=$(size "$(culog $n)"); CLOG0[$n]=$(size "$(clog $n)")
    echo "  (client game $n kept: back at its main menu, joining again)"
    $PY "$T/ctl.py" $(cctl $n) args "$@" || die "client game $n didn't take its arguments"
  else
    client_game_start $n "$@"
  fi
}
client_wait_log() { # client_wait_log N <pattern> <seconds>
  local i; for i in $(seq 1 $(($3 * 2))); do since "$(clog $1)" "${CLOG0[$1]:-0}" | grep -q "$2" && return 0; sleep 0.5; done
  echo "  (timed out waiting for \"$2\" in client $1's log)"; return 1
}
client_report() { # client_report N DIR: its log into DIR, what it refused, its exceptions; sets EXC
  local n=$1 cl; cl="$(clog $n)"; flushed
  since "$cl" "${CLOG0[$n]:-0}" >"$2/c$n-LogOutput.log"; [ $n = 1 ] && cp "$2/c$n-LogOutput.log" "$2/client-LogOutput.log"
  grep "DarkwoodMP\]" "$2/c$n-LogOutput.log" | grep -E "refused|authority\]|\[map\]" | head -12 | sed "s/^/  c$n: /"
  local ex; ex="$(since "$(culog $n)" "${CULOG0[$n]:-0}" | grep -ci exception)"
  [ "$ex" = "0" ] && echo "  client game $n: no exceptions" || { echo "  CLIENT GAME $n EXCEPTIONS: $ex"; since "$(culog $n)" "${CULOG0[$n]:-0}" | grep -i -m3 -A6 exception; EXC=$((EXC + ex)); }
}

host() { # host <extra args...>: the game hosts a fresh copy of your save
  rm -rf "$WORK/sandbox"; cp -a "$SAVES" "$WORK/sandbox"
  ctl args --dwmp-host --dwmp-sandbox "$WORK/sandbox" --dwmp-continue 1 ${LOAD_SHOTS:+--dwmp-load-shots "$LOAD_SHOTS/host"} ${DWMP_ARGS:-} "$@"
  wait_log "loading your profile" 60
}
world_ready() { wait_log "World ready" 120; } # the host's world has loaded and players can join
client() { # client <fake-host args...>: a scripted host on 27721; the game joins it
  [ -f "$WORK/world.pkl" ] || die "no $WORK/world.pkl: run 'capture' first"
  rm -rf "$GAME_DIR/BepInEx/cache/DarkwoodMP/worlds/"*
  $PY "$T/worldtest.py" serve 27721 "$WORK/world.pkl" "$@" >"$WORK/serve.out" 2>&1 & local sp=$!
  sleep 0.5
  local extra=""; [ "${1:-}" = "--authority" ] && extra="--dwmp-test-client-actions"
  [ "${1:-}" = "--end" ] && extra="--dwmp-test-quick-end"
  ctl args --dwmp-join 127.0.0.1:27721 --dwmp-debug-world ${LOAD_SHOTS:+--dwmp-load-shots "$LOAD_SHOTS/client"} ${DWMP_ARGS:-} $extra
  wait $sp; cat "$WORK/serve.out"
}

map_body() { # map_body SCENARIO DIR: the two-game test of tools/tests/SCENARIO.py, results in DIR
  local dir="$2" t0 rc
  export MAP_SCENARIO="$1"
  rm -rf "$dir"; mkdir -p "$dir"
  echo "  scenario: $MAP_SCENARIO"
  t0=$(date +%s)
  ctl args --dwmp-host --dwmp-testworld --dwmp-reset --dwmp-debug-world
  wait_log "Test world: built" 150 || die "the test world wasn't built"
  since "$LOG" "$LOG0" | grep -o "Test world: built.*" | head -1
  echo "  phase: host's test world loaded and built in $(( $(date +%s) - t0 )) s"; t0=$(date +%s)
  $PY "$T/mapdriver.py" pre $CTL || echo "  FAIL: a pre-join step failed"
  echo "  phase: pre-join steps in $(( $(date +%s) - t0 )) s"
  client_join 1 --dwmp-join 127.0.0.1:27720 --dwmp-debug-world
  $PY "$T/mapdriver.py" run $CTL $(cctl 1) "$dir"; rc=$?
  EXC=0; client_report 1 "$dir"
  flushed; since "$LOG" "$LOG0" >"$dir/host-LogOutput.log" # the host's side of the run, read with the client's
  mod_log "\[map\]|refused|authority\]" | head -12 | sed 's/^/  host: /'
  [ $rc = 0 ] && [ "$EXC" = "0" ] && echo "  map: PASS" || echo "  map: FAIL (dumps and logs in $dir)"
}

exceptions() { # the host game's new exceptions in Player.log since the test began
  local ex; ex="$(since "$ULOG" "$ULOG0" | grep -ci exception)"
  [ "$ex" = "0" ] && echo "  no exceptions" || { echo "  EXCEPTIONS: $ex"; since "$ULOG" "$ULOG0" | grep -i -m3 -A6 exception; }
}
saves_check() {
  [ "$(save_hash)" = "$BEFORE" ] && echo "  your saves: unchanged" || die "YOUR SAVES CHANGED: they're backed up as they were before the tests in $WORK/saves-backup"
}
leftovers() { # downloaded worlds left in the mod's caches (host game and client game 1): listed, then removed
  local d n
  for d in "$GAME_DIR/BepInEx/cache/DarkwoodMP/worlds" "$(cgame 1)/BepInEx/cache/DarkwoodMP/worlds"; do
    n=$(ls -A "$d" 2>/dev/null | wc -l)
    [ "$n" = 0 ] || { echo "  cleanup: $n downloaded world(s) left in $d, removed"; rm -rf "$d/"*; }
  done
}

run_body() { # run_body ITEM: one test (in a subshell: a "die" ends this test, not the run)
  local t="$1"
  case "$t" in
    map:*) map_body "map_scenario_${t#map:}" "$WORK/map-${t#map:}" ;;
    map) map_body "${MAP_SCENARIO:-map_scenario}" "$WORK/map" ;;
    handshake)
      host --dwmp-password test; world_ready
      $PY "$T/tcptest.py" 27720 ;;
    capture)
      host; world_ready
      for i in $(seq 1 20); do out="$($PY "$T/worldtest.py" fetch 27720 "$WORK/world.pkl" 2>&1)"; echo "$out" | grep -q "expected SaveOffer" || break; sleep 4; done
      echo "$out" ;;
    host-combat)
      host --dwmp-dummy --dwmp-spawn DogMutated --dwmp-debug-threat --dwmp-test-night --dwmp-test-blast --dwmp-test-fx
      wait_log "dwmp-spawn:" 60
      $PY "$T/combattest.py" 27720 6057 350 60 verdict,bitten,blood,sound,blast,aura,flame,boom,cues,away,psound,hurt,burn,combat,scream,effect,afternight | grep -v "^PlayerDamage"
      mod_log "is away|back in the world" | head -4
      mod_log "joiner ai test|JoinerAi|ReactToJoiner|reacts to|woken by" | head -12
      mod_log "threat|notice|test-blast" | grep -Ev "night|swings|aims|invisible" | head -26
      grep "DarkwoodMP\] Wards:" "$LOG" | head -4 # logged at start-up
      mod_log "swings|aims|invisible" | grep -v "Dummy:" | head -8
      mod_log "night" | grep -v "after-night" | head -12
      mod_log "\[fx\]|test-fx" | head -20
      mod_log "enemyfx|after-night" | head -8 ;;
    host-world)
      host --dwmp-test-world --dwmp-debug-world; world_ready
      bg_until "world test\] done" 120 $PY "$T/worldobs.py" 27720 120 "$WORK/world_rec.pkl" | cut -c1-160
      mod_log "world test|story" ;;
    host-chars)
      host --dwmp-test-save 8; world_ready
      $PY "$T/chartest.py" 27720
      ls "$WORK/sandbox/prof1/darkwoodmp_players/"
      mod_log "character" ;;
    host-progress)
      host; world_ready
      $PY "$T/progresstest.py" 27720
      mod_log "\[progress\]" ;;
    host-death)
      host --dwmp-test-death --dwmp-debug-world; world_ready
      bg_until "test-death: alive again" 60 $PY "$T/worldobs.py" 27720 60 "$WORK/death_rec.pkl" | grep -E "deathDrop|time syncs"
      mod_log "death|didn't" ;;
    host-elsewhere)
      host --dwmp-dummy --dwmp-spawn DogMutated --dwmp-test-away --dwmp-debug-world
      wait_log "dwmp-spawn:" 60
      bg_until "test-away: back in" 180 $PY "$T/worldobs.py" 27720 180 "$WORK/away_rec.pkl" --ask-location=outside_church_underground_01 | grep -E "ObjMove|joined|Location" | cut -c1-160
      mod_log "test-away|host is away|host is back|is away|back in the world|pushed|\[locations\]" | head -16 ;;
    host-reload)
      host --dwmp-dummy --dwmp-spawn DogMutated --dwmp-test-reload 15
      wait_log "dwmp-spawn:" 60
      $PY "$T/combattest.py" 27720 6057 350 90 reload | grep -E "WorldReload|HostAway|message counts"
      mod_log "world changed" ;;
    host-menu)
      host --dwmp-test-menu 10; world_ready
      $PY "$T/combattest.py" 27720 6057 350 60 | grep -E "HostAway|closed"
      sleep 4
      $PY -c "import socket; socket.create_connection(('127.0.0.1', 27720), timeout=3); print('the server still takes connections')" 2>/dev/null || echo "the server has stopped"
      mod_log "test-menu|server stops" ;;
    host-chapter)
      host --dwmp-test-chapter --dwmp-debug-world; world_ready
      wait_log "\[chapter test\] .* chapter triggers" 60
      TRIG="$(since "$LOG" "$LOG0" | grep -o "\[chapter test\] events [0-9-]* [0-9-]* [0-9]* at [0-9.-]* [0-9.-]* to chapter [0-9]*" | head -1 | awk '{print $4, $5, $6, $8, $9, $12}')"
      mod_log "chapter test"
      [ -n "$TRIG" ] || die "no chapter event to ask for"
      $PY "$T/storytest.py" 27720 $TRIG
      mod_log "[Cc]hapter|authority\\]|world changed|character" | grep -v "chapter test" ;;
    host-end)
      host --dwmp-test-end 5; world_ready
      $PY "$T/combattest.py" 27720 6057 350 60 end | grep -E "GameEnd|HostAway|closed|played"
      sleep 3
      $PY -c "import socket; socket.create_connection(('127.0.0.1', 27720), timeout=3); print('the server still takes connections')" 2>/dev/null || echo "the server has stopped"
      mod_log "test-end|game's end|server" ;;
    host-authority)
      host --dwmp-dummy --dwmp-spawn DogMutated --dwmp-debug-world; wait_log "dwmp-spawn:" 60
      $PY "$T/authtest.py" 27720
      mod_log "authority\\]" ;;
    menu-entry)
      rm -rf "$WORK/shots"; mkdir -p "$WORK/shots"
      ctl args --dwmp-test-menu-entry --dwmp-shots "$WORK/shots"
      wait_log "main menu: clicked" 30
      host --dwmp-test-menu-entry --dwmp-shots "$WORK/shots"
      wait_log "pause menu: clicked" 120
      mod_log "menu-entry\\]"
      [ "$(since "$LOG" "$LOG0" | grep -c 'clicked, panel open: True')" -ge 2 ] || die "the menu entry didn't open the panel" ;;
    options)
      rm -rf "$WORK/options"; mkdir -p "$WORK/options"
      L1=$LOG0
      ctl args --dwmp-test-options "$WORK/options"
      wait_log "\[options\] done" 240
      LOG0=$(size "$LOG")
      host --dwmp-test-options "$WORK/options"
      wait_log "\[options\] done" 300
      LOG0=$L1
      mod_log "\[options\] (PASS|FAIL|done|put back|deleted|main|pause)"
      [ "$(mod_log "\[options\] done: all passed" | wc -l)" -ge 2 ] || die "the Options test failed (see the lines above)" ;;
    drag)
      ctl args --dwmp-test-drag
      wait_log "\[drag\] done" 90
      mod_log "\[drag\]"
      mod_log "\[drag\] done: all passed" | grep -q . || die "the drag test failed (see the lines above)" ;;
    shots)
      rm -rf "$WORK/shots"; host --dwmp-shots "$WORK/shots"; world_ready
      bg_until "shots\] done" 60 $PY "$T/combattest.py" 27720 6057 350 60 >/dev/null
      ls "$WORK/shots" ;;
    steam)
      ctl args --dwmp-steam-test
      wait_log "steam-test: left\|steam-test: no Steam" 80
      mod_log "steam|Steam|lobby"
      # Steam not running (the game started outside it, or no Steam client): nothing to test here, not a failure.
      since "$LOG" "$LOG0" | grep -q "steam-test: no Steam" && echo "  SKIP: no Steam in this game (start the Steam client to run this test)" ;;
    client-world)
      [ -f "$WORK/world_rec.pkl" ] || die "no $WORK/world_rec.pkl: run 'host-world' first"
      client --replay "$WORK/world_rec.pkl"
      mod_log "\[world\]|refused|workbench|Journal" | grep -Ev "clock|fuel" ; mod_log "fuel" | tail -4 ;;
    client-enemies)
      client --enemies; sleep 5
      mod_log "Showing|corpse|\[fx\]|enemyfx|after-night" ;;
    client-chars)
      client --chars
      mod_log "character" ;;
    client-reload)
      client --reload --chars
      mod_log "notice|Sent Hello" ;;
    client-authority)
      client --authority
      mod_log "client test|authority\\]|host's state" ;;
    client-chapter)
      client --reload --chars --chapter
      mod_log "notice|Sent Hello|chapter" ;;
    client-end)
      client --end 2
      wait_log "Back at the main menu after the ending" 90
      mod_log "game's end|ending|end screen|credits|main menu" ;;
    client-epilogue)
      client --end 1
      wait_log "Back at the main menu after the ending" 150
      mod_log "game's end|ending|epilogue|end screen|credits|main menu" ;;
    map4)
      rm -rf "$WORK/map4"; mkdir -p "$WORK/map4"
      export MAP_SCENARIO="${MAP_SCENARIO:-map4_scenario}" MAP_CLIENTS="${MAP_CLIENTS:-3}"
      echo "  scenario: $MAP_SCENARIO, $MAP_CLIENTS client games"
      # The mod takes 5 players, the host included; more clients than that need --dwmp-max-players (tests only).
      MAXP=""; [ "$MAP_CLIENTS" -gt 4 ] && MAXP="--dwmp-max-players $((MAP_CLIENTS + 1))"
      # Several games on one machine: small windows; the clients at the lowest quality and 30 frames a second.
      ctl args --dwmp-host --dwmp-testworld --dwmp-reset --dwmp-debug-world --dwmp-window 960x540 $MAXP
      wait_log "Test world: built" 150 || die "the test world wasn't built"
      since "$LOG" "$LOG0" | grep -o "Test world: built.*" | head -1
      $PY "$T/mapdriver.py" pre $CTL || echo "  FAIL: a pre-join step failed"
      # A memory sample every 10 s while it runs: RSS of every game, and what's available.
      ( while :; do echo "$(date +%T) $(free -m | awk '/^Mem/{print "avail", $7}') $(free -m | awk '/^Swap/{print "swapused", $3}') $(ps -o rss= -p $(pgrep -x Darkwood.x86_64 | tr '\n' ',' | sed 's/,$//') 2>/dev/null | tr '\n' ' ')"; sleep 10; done ) >"$WORK/map4/memory.log" 2>&1 & MEMP=$!
      # One at a time: each client's world loaded before the next starts (the host sends each its save).
      PORTS=""
      for n in $(seq 1 $MAP_CLIENTS); do
        client_game_start $n --dwmp-join 127.0.0.1:27720 --dwmp-debug-world --dwmp-window 480x270 --dwmp-low-quality --dwmp-fps 30
        client_wait_log $n "World ready" 300
        PORTS="$PORTS${PORTS:+,}$(cctl $n)"
      done
      $PY "$T/mapdriver.py" run $CTL $PORTS "$WORK/map4"; MAPRC=$?
      kill $MEMP 2>/dev/null
      EXC=0; for n in $(seq 1 $MAP_CLIENTS); do client_report $n "$WORK/map4"; done
      mod_log "\[map\]|refused|authority\]" | head -16 | sed 's/^/  host: /'
      echo "  memory (MB available, swap used, RSS KB of each game; lowest available):"; sort -k3 -n "$WORK/map4/memory.log" | head -1 | sed 's/^/    /'
      awk '{for (i = 6; i <= NF; i++) if ($i > m[i]) m[i] = $i} END {printf "    peak RSS (MB):"; for (i = 6; i in m; i++) printf " %d", m[i] / 1024; print ""}' "$WORK/map4/memory.log"
      for n in 1 2 3 4; do client_game_stop $n; done
      [ $MAPRC = 0 ] && [ "$EXC" = "0" ] && echo "  map4: PASS" || echo "  map4: FAIL (dumps and logs in $WORK/map4)" ;;
    client-away|client-drop)
      client "--${t#client-}"
      wait_log "Back at the main menu" 30
      mod_log "host|main menu" | grep -v "Sent Hello" ;;
  esac
}

is_map() { [ "$1" = map ] || [[ "$1" == map:* ]]; }
FAILRE='(^|[^A-Za-z])FAIL([^A-Za-z]|$)|FAILED|DIFFER|timed out|EXCEPTIONS|Traceback|YOUR SAVES CHANGED|no reply'
declare -a R_NAME R_STATUS R_SECS R_REASON R_LOG
TOTAL=${#ITEMS[@]}; N=0; START=$(date +%s)
[ $TOTAL -gt 0 ] || { echo "nothing to run (tools/run-test.sh --list)"; exit 1; }
# The games are started with setsid, so they outlive this script if it is killed (a time-out, a stopped task),
# and a trap doesn't help: bash runs it only after the running test ends, and never on SIGKILL. A watchdog in its
# own session notes this run's games (host and clients 1-4) every second while the script runs; once the script
# is gone, it stops those processes (the same PIDs, still in the same folders), and nothing it didn't see.
watchdog() {
  local seen="" now p d n
  while kill -0 $RUNNER 2>/dev/null; do
    now="$(game_pid; for n in 1 2 3 4; do client_pid $n; done)"; [ -n "$now" ] && seen="$(echo $seen $now | tr ' ' '\n' | sort -u)"
    sleep 1
  done
  for p in $seen; do d="$(readlink /proc/$p/cwd 2>/dev/null)" || continue
    case "$d" in "$(cd "$GAME_DIR" && pwd -P)"|"$HOME/.cache/darkwood-mp/client-game"*) kill $p 2>/dev/null && echo "$(date +%T) run-test.sh had ended: stopped game $p in $d" >>"$WORK/watchdog.log" ;; esac
  done
}
export RUNNER=$$ GAME_DIR WORK; export -f watchdog game_pid client_pid cgame
setsid bash -c watchdog </dev/null >/dev/null 2>&1 &
echo "Running $TOTAL: ${ITEMS[*]}"
game_start
for t in "${ITEMS[@]}"; do
  N=$((N + 1)); desc="${DESC[$t]:-}"
  [[ "$t" == map:* ]] && desc="two games: tools/tests/map_scenario_${t#map:}.py alone"
  out="$WORK/logs/${t/:/-}.out"
  echo "=== $t ($N/$TOTAL)"
  game_alive || { echo "  (the game had stopped: starting it again)"; game_start; }
  LOG0=$(size "$LOG"); ULOG0=$(size "$ULOG"); T0=$(date +%s)
  ctl title "Test ($N/$TOTAL): $t - $desc"
  ctl reset || { echo "  (no clean main menu: starting the game again)"; game_start; ctl title "Test ($N/$TOTAL): $t - $desc"; }
  LOG0=$(size "$LOG"); ULOG0=$(size "$ULOG")
  ( run_body "$t" ) 2>&1 | tee "$out"; rc=${PIPESTATUS[0]}
  exceptions 2>&1 | tee -a "$out"
  # Cleanup: the game back at its main menu; the client game stopped unless the next item is a map one too.
  next="${ITEMS[$N]:-}"
  if ! game_alive; then echo "  cleanup: the game had stopped (it is started again for the next test)" | tee -a "$out"
  elif ! ctl reset >/dev/null; then echo "  cleanup: FAIL the game didn't go back to its main menu" | tee -a "$out"; fi
  if [ -n "$(client_pid 1)" ] && ! { is_map "$t" && is_map "$next" && [ -z "${MAP_FRESH_CLIENT:-}" ]; }; then
    client_game_stop 1; [ -n "$(client_pid 1)" ] && echo "  cleanup: FAIL client game 1 still running" | tee -a "$out"
  fi
  leftovers | tee -a "$out"
  saves_check | tee -a "$out"; [ "${PIPESTATUS[0]}" = 0 ] || { game_stop; client_game_stop 1; exit 1; }
  secs=$(( $(date +%s) - T0 ))
  echo "  took $secs s" | tee -a "$out"
  reason="$(grep -E -m1 "$FAILRE" "$out" | sed 's/^ *//' | cut -c1-140)"
  if [ -z "$reason" ] && grep -q "^  SKIP:" "$out"; then status=SKIP; reason="$(grep -m1 "^  SKIP:" "$out" | sed 's/^ *//' | cut -c1-140)"
  elif [ $rc != 0 ] || [ -n "$reason" ]; then status=FAIL; [ -n "$reason" ] || reason="exit code $rc"
  elif [[ "$VERDICT" == *" $t "* ]] || [[ "$t" == map:* ]]; then status=PASS
  else status=RAN; fi
  [ "$t" = capture ] && [ $status = PASS ] && [ ! -f "$WORK/world.pkl" ] && { status=FAIL; reason="no world.pkl"; }
  [ "$t" = host-world ] && [ $status = PASS ] && [ ! -f "$WORK/world_rec.pkl" ] && { status=FAIL; reason="no world_rec.pkl"; }
  R_NAME+=("$t"); R_STATUS+=("$status"); R_SECS+=("$secs"); R_REASON+=("$reason"); R_LOG+=("$out")
done
ctl reset >/dev/null 2>&1; game_stop; client_game_stop 1
leftovers
echo "=== $N item(s) in $(( $(date +%s) - START )) s"
printf "%-22s %-5s %6s  %s\n" ITEM RESULT SECS "FIRST FAILURE"
for i in "${!R_NAME[@]}"; do printf "%-22s %-5s %6s  %s\n" "${R_NAME[$i]}" "${R_STATUS[$i]}" "${R_SECS[$i]}" "${R_REASON[$i]}"; done
nf=0; for i in "${!R_NAME[@]}"; do [ "${R_STATUS[$i]}" = FAIL ] || continue; nf=$((nf + 1))
  [ $nf = 1 ] && echo "Failed (log, then the command that reruns it alone):"
  echo "  ${R_NAME[$i]}: ${R_LOG[$i]}"; echo "    WORK=$WORK $0 ${R_NAME[$i]}"; done
echo "Logs: $WORK/logs/ITEM.out; map results in $WORK/map or $WORK/map-NAME. RAN: read the test's lines in its log."
[ $nf = 0 ]
