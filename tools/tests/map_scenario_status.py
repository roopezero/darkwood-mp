"""Map test steps for what the README listed as not yet tried in game; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioStatus.cs."""

import os

PRE_JOIN = [
    # The joiner is answered as if the host were in the prologue for 15 s from its first Hello (JoinWait).
    ("host", "hold-joiners", "15"),
]

STEPS = [
    # It waited, then came in by itself.
    ("client", "join-waited"),
    # The host's snapshot for the joiner: sav.dat written on a worker thread, and how long the host's game stopped.
    ("host", "join-stop"),
    # The Direct panel's LAN tab finds the host's game (its UDP info answer).
    ("client", "lan-search"),
]

# Disarming a trap shows: the client crouches over it, the host sees its puppet crouch, and it stands up after.
# map_scenario_world.py puts these in its traps part.
DISARM = [
    ("host", "trap-place", "chainTrap", "120", "-60"),
    ("client", "disarm-crouch", "chainTrap@120,-60"),
    ("host", "puppet-crouch", {"settle": 0}),
    ("client", "disarm-finish", "chainTrap@120,-60"),
    ("client", "heal"),
]

# After the dumps (a death changes the story flags): the client dies for good and watches the host.
# While watching, the client's view is open all around (issue #16), with a screenshot into $WORK/shots; then
# spectating ends as a world reload ends it and the view cone is put back.
POST = [
    ("client", "permadeath"),
    ("client", "spectator-view", os.path.join(os.environ.get("WORK", "/tmp/darkwoodmp-test"), "shots")),
    ("host", "sees-spectator"),
    ("client", "spectate-end"),
]
