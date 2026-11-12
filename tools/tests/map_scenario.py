"""The scenario of the two-game `map` test (tools/run-test.sh map, run by tools/tests/mapdriver.py).

Each step is a tuple: (who, step, arg, ...), with an optional dict last for options:
    who      "host" or "client": whose game does it (in map4 also "c1".."c4", "client" being c1, and "all-clients")
    step     a step from src/DarkwoodMP/World/MapScenario.cs ("toggle-door", "take-item", "drop-item", "time",
             "wait", "goto", ...); its args follow, as strings
    options  {"settle": SECS}   wait this long after it before the next (default SETTLE), for it to reach the
                                 other game
             {"may_fail": True} a "fail" reply is printed but doesn't fail the test

("both", "compare", WHO, SECTIONS) (or "all") dumps every game there and then around WHO's player and compares them, with
SECTIONS (comma-separated) required not to be empty.

Things are picked nearest to the test area's centre, in both games.

PRE_JOIN runs in the host's game before the client joins (so it's in the save the client loads); STEPS runs
once both games are in the world. Then both dump their state and tools/tests/mapcompare.py compares them;
REQUIRE lists the dump sections that mustn't be empty.
"""

SETTLE = 1.5

# The first steps (map_scenario_core.py: a door, an item taken and dropped by each player, the clock).
import map_scenario_core as _core
PRE_JOIN = list(_core.PRE_JOIN)
STEPS = list(_core.STEPS)

# Every map_scenario_NAME.py is also a battery of its own: tools/run-test.sh map:NAME runs it alone, from a fresh test
# world and join (docs/MULTIPLAYER_TESTING_GUIDE.md). This module runs them all in one world, in this order.
# Status checks first (map_scenario_status.py: LAN search, prologue wait, crouch), then one of each shared thing
# (map_scenario_world.py), the reported bugs (map_scenario_issues.py), fixes that had no test in game
# (map_scenario_fixchecks.py, map_scenario_gaps.py), separate locations (map_scenario_locations.py: both players go
# into one and back), items used through the game's own code (map_scenario_items2.py: a hotbar lantern, map items,
# throwables, firearms), and last the night trader's kill (the trader is gone after it, and the client is in a dream).
import map_scenario_world as _world, map_scenario_issues as _issues, map_scenario_fixchecks as _fixchecks
import map_scenario_locations as _locations, map_scenario_status as _status, map_scenario_gaps as _gaps
import map_scenario_dreams as _dreams
# Parts added for the 4-player sweep (each self-contained, also runnable alone with MAP_SCENARIO=map_scenario_X).
import map_scenario_items1 as _items1
import map_scenario_enemies as _enemies  # each kind of enemy fought by the client
import map_scenario_npcs as _npcs
import map_scenario_far as _far  # far from the other player: enemies and doors switched off in their game
import map_scenario_hostile as _hostile  # hostile NPCs run by the host
import map_scenario_items2 as _items2  # guns, throwables, the lantern, map items
import map_scenario_farevents as _farevents  # a place's own events by who is there
import map_scenario_sweep as _sweep  # flags set while sharing is paused, enemy loop sounds
_extra = [_items1, _enemies, _far, _hostile, _items2, _farevents, _sweep]
_kill = [s for s in _fixchecks.STEPS if s[1] in ("kill-trader", "trader-dream")]
_fixes = [s for s in _fixchecks.STEPS if s not in _kill]
PRE_JOIN += _world.PRE_JOIN + _issues.PRE_JOIN + _fixchecks.PRE_JOIN + _gaps.PRE_JOIN + _locations.PRE_JOIN + _status.PRE_JOIN
STEPS = _issues.AT_JOIN + STEPS + _status.STEPS + _world.STEPS + _issues.STEPS + _fixes + _gaps.STEPS
for _m in _extra:
    PRE_JOIN += getattr(_m, "PRE_JOIN", [])
    STEPS += _m.STEPS
STEPS += _npcs.WALK + _npcs.MORNING  # (_npcs.KILL only alone: it ends in the killer's dream, as _kill does)
# The NPCs every game has by save id (npcpos) are checked before _kill: it kills the hideout's night trader, the last
# of them near the centre (the hostile NPCs die in _hostile), so the final dumps have none.
STEPS += _locations.STEPS + [("both", "compare", "client", "npcpos")] + _kill
# Nobody died: a death changes the story flags and resets things the dumps compare.
STEPS += [("host", "alive"), ("client", "alive")]

# After both games have dumped their state: a level dream dreamt together (it moves the players and sets the clock),
# then things that change it for good (a death).
POST = _dreams.POST + _status.POST

REQUIRE = (list(_core.REQUIRE) + getattr(_issues, "REQUIRE", [])
           + getattr(_fixchecks, "REQUIRE", []) + getattr(_gaps, "REQUIRE", []) + [r for _m in _extra for r in getattr(_m, "REQUIRE", [])] + [r for r in _npcs.REQUIRE if r != "npcpos"])
