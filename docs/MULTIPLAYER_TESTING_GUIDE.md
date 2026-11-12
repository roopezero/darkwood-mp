# Multiplayer testing guide

How to run DarkwoodMP's in-game tests in parts: groups, map batteries, their isolation and cleanup, and what the
time goes on. `.claude/skills/darkwood-test/SKILL.md` describes each test; `tools/run-test.sh --help` the options.

## Commands

```
tools/run-test.sh --list                          # every test, group and map battery
tools/run-test.sh --plan ITEM...                  # what would run, in order, prerequisites added; runs nothing
tools/run-test.sh ITEM...                         # tests, groups and batteries, mixed, in any order
tools/run-test.sh map:enemies map:world host-world
tools/run-test.sh all map:far                     # all can be mixed with other items
```

| Item | What runs |
|---|---|
| a test name | that test (`handshake`, `host-world`, `client-drop`, ...) |
| `all` | the 25 scripted-peer tests (not `shots`, `options`, `map`, `map4`) |
| `full` | `all`, then `map` |
| `net` | handshake host-menu host-reload client-reload client-away client-drop steam |
| `combat` | host-combat host-elsewhere client-enemies |
| `world` | capture host-world client-world host-death |
| `chars` | host-chars client-chars host-progress |
| `story` | host-chapter client-chapter host-end client-end client-epilogue |
| `menus` | menu-entry shots options |
| `authority` | host-authority client-authority |
| `map` | the full two-game scenario: every module in one world (`tools/tests/map_scenario.py`) |
| `map:NAME` | `tools/tests/map_scenario_NAME.py` alone, from a fresh test world and a fresh client join |
| `maps` | every map battery, one after another, except modules with `IN_MAPS = False` |

The batteries are data: every `tools/tests/map_scenario_NAME.py` is `map:NAME`, so a new module is a battery as soon
as it exists. Now: core, dreams, enemies, far, farevents, fixchecks, gaps, hostile, issues, items1, items2, locations,
night, npcs, skills, status, sweep, world, and dogfull (the whole scenario with the dog watch; not in `maps`).
`map_scenario_core.py` holds the scenario's first steps (door, take and drop, clock), which used to live only in
`map_scenario.py`; the full scenario is unchanged (same PRE_JOIN, STEPS, POST and REQUIRE, checked by hash).

Each item runs once, in the order given. A client test whose input isn't in `$WORK` gets it made first: `capture`
(world.pkl) before the first client test, `host-world` (world_rec.pkl) before `client-world`.

From a shell outside the desktop session (an agent's), the runner uses `DISPLAY=:0` and the newest
`/run/user/UID/xauth_*` when no display is set and `xdpyinfo` answers; otherwise it fails at once. Without a display
the game stops at "Loading in SingleInstance mode" (Player.log: "Desktop is 0 x 0 @ 0 Hz"); the runner prints the end
of Player.log when the game doesn't reach its main menu.

## Results

Each item's output goes to `$WORK/logs/ITEM.out` (`map:far` is `map-far.out`); map dumps and the client's log to
`$WORK/map` (full) or `$WORK/map-NAME`. The run ends with a table: item, result, seconds, the first failure line, and
for each failed item its log and the command that reruns it alone (`WORK=... tools/run-test.sh ITEM`).

- **PASS**: the test has a verdict of its own and nothing failed: map, map:*, map4, options, menu-entry, capture
  (world.pkl written), host-world (world_rec.pkl written).
- **RAN**: nothing failed (no FAIL line, no time-out, no exception), but the test prints what it saw and a person
  reads it. Most scripted-peer tests are this today (see the follow-ups).
- **FAIL**: a FAIL line, `FAILED STEP` (mapdriver), differing dumps, a time-out, an exception in Player.log, a
  Python traceback, or the test's body stopping (`die` ends that test only, not the run).

`combattest.py` now exits 1 with `FAIL never happened: ...` when an event it was told to wait for (its UNTIL list)
never came, so host-combat, host-reload and host-end fail on a missing event instead of only printing it.

## Isolation and cleanup

- One host game runs the whole list (`--dwmp-test-control 27799`); before each item it is sent `reset` (leave the
  session and world, back to the main menu) and the item's arguments.
- A map item starts the host on `--dwmp-testworld --dwmp-reset`: `TestWorld.Start` deletes
  `BepInEx/cache/DarkwoodMP/testworld` and copies your saves there again, then builds the test stations. So every
  map battery plays a fresh world, whatever ran before.
- The client game (map items) is kept running between map items and sent `reset`, then `args --dwmp-join ...`,
  with its downloaded worlds deleted first (`MAP_FRESH_CLIENT=1` starts it again for each item instead). It is
  stopped after a map item when the next item isn't one.
- A module run alone runs its `AT_JOIN` steps before its `STEPS` (map_scenario_issues.py's join check used to be
  skipped when it ran alone), a module with only `POST` steps (dreams) runs, and every module alone ends with the
  `alive` checks the full scenario ends with.
- After each item: the host game sent back to its main menu (FAIL if it doesn't go), client game 1 stopped (unless a
  map item is next), downloaded worlds left in the host's and client's `BepInEx/cache/DarkwoodMP/worlds` listed and
  removed, and your saves compared with their hashes from the start (the run stops if they changed).
- `game_stop` now stops only the host game (by its folder), not the client games.

## Where the time goes

Measured on 2026-11-12 (one machine, the runner's "took N s" and mapdriver's `phase:` lines).

`all` (25 items): **1213 s (20 min)**, no FAIL; steam SKIP (no Steam client running). Per test, seconds:
host-world 121, client-world 82, client-authority 81, host-authority 70, host-combat 68, steam 64 (the mod waits
60 s for Steam before it says "no Steam"), client-epilogue 63, host-chapter 62, client-end 52, host-chars 51,
host-reload 50, client-chars 46, host-death 44, handshake 43, host-elsewhere 38, client-enemies 36, host-menu 33,
host-end 31, host-progress 30, client-away 27, client-drop 27, menu-entry 26, client-chapter 22, capture 19,
client-reload 18. `net` alone: 295 s (8 items, capture added). Most of a host test is the game loading a fresh
sandbox copy (about 15 s) and the hooks' own timers (`--dwmp-test-reload 15`, `--dwmp-test-menu 10`).

`maps` (22 batteries, one host game, the client game kept between them): **4859 s (81 min)**. A map item's
fixed cost is about 45 s: the host's test world loaded and built 14-21 s, pre-join steps 0-12 s, client join
17-19 s, the final dumps 1-2 s, then the reset, cleanup and save check. The join is the same with a kept client
game (17 s) as with one started for it (19 s, counted from its control port answering): it is the world download
and load, so keeping the client game saves only its start-up. Per battery, total s (steps / their s):
enemies 664 (211 / 624), savage 561 (112 / 517), ui 505, items2 484 (165 / 441), world 451, gaps 247 (68 / 202),
issues 171 (29 / 118), fixchecks 165 (38 / 125), far 148 (25 / 103), locations 148 (31 / 107), npcs 134 (34 / 90),
farevents 132 (34 / 92), hostile 131 (32 / 84), night 103 (17 / 57), items1 101 (24 / 56), status 93 (5 / 18),
skills 92 (10 / 30), dreams 88 (POST only), core 75 (9 / 29), dice 48 (5 / 8), join 48 (3 / 5). ui and world ran
into a 180 s step time-out (see below), so theirs are not their real lengths. In the order checks: map:far
148-151 s (4 runs), map:sweep 105-109 s (the 3 runs that passed).

Mapdriver now also waits, before the join, until the host's dropped items have stopped moving (a phase line says
how long: 7-12 s, which adds that to each map item); the settle after each step is unchanged. `MAP_SETTLE=SECS` sets a shorter default settle for quick
runs (steps with their own settle keep it); the modules' SETTLE is the reference, and a pass with a shorter one
is not a pass with the default.

## Not done yet

- `maps` in one run doesn't pass yet (2026-11-12: 7 of 22 passed). The causes found:
  - The test kit's overflow (chainTrap, bandages, wire, rope) is dropped while the test world is built and still
    rolls when a kept client game joins; each game lets it roll on, and the dumps differed by 2-16 (dice, dreams,
    enemies, farevents, fixchecks, locations, skills; map:sweep once in four, after map:far). Mapdriver now waits for them to rest
    (7-12 s): dice, dreams, skills and fixchecks then passed in one run, the client game kept (440 s for the 4).
  - Mod bug: the host keeps its players' characters in memory by the save folder's path
    (`Characters.Records()`), so a new world in the same folder (each `--dwmp-reset` test world) gets the last
    one's records. After map:status's permadeath, the next batteries' client was "dead for good" on joining
    (sweep: "our player is dead"); its walks then never ended (ui, world: 180 s step time-outs).
  - Not yet looked at: issues (`wb-use`: no free slot in our bags), items2 (`i2-hurt gun`: no damage),
    night (journal differs), npcs (`npc-died`), status (`join-waited`: world not ready).
- Real verdicts for the RAN tests: worldobs.py (host-world, host-death, host-elsewhere) and storytest.py
  (host-chapter) need their expected events listed and checked, as combattest.py now does.
