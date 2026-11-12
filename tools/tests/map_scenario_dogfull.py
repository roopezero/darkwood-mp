"""The full map scenario (map_scenario.py) with MapScenarioDogWatch on in both games from the join: what wakes the
save's dogs and how many times they bite. Run with MAP_SCENARIO=map_scenario_dogfull tools/run-test.sh map."""
import map_scenario as _m

IN_MAPS = False  # the whole scenario again: not one of the "maps" batteries

SETTLE = _m.SETTLE
PRE_JOIN = _m.PRE_JOIN
STEPS = [("host", "dw-on", {"settle": 0}), ("client", "dw-on", {"settle": 0}), ("host", "mt-debug-threat", "on", {"settle": 0})] + _m.STEPS
POST = _m.POST
REQUIRE = _m.REQUIRE
