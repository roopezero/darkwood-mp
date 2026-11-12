"""Map test steps for players far apart; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioFarEvents.cs; run these alone with MAP_SCENARIO=map_scenario_farevents.

The client jumps to a far world location (the nearest one 5000 or more from the centre whose enter and exit
events don't move the player or end anything). The host keeps it entered for them, and runs its enter events
without its own player's part: no message, dialogue or item for the host's player. The client's game runs them as
its player's and the shared part. A few more jumps there and back: the host's culling mustn't leave and enter
locations at once (overlapping refresh passes). Then the host comes to the same location (only its own part of
the enter events runs), the client goes, the location stays entered for the host with no exit events, and when the
host goes too, its exit events run once, in full. Last, the host goes into a separate location and back while the
client is far: the client's area is refreshed during the host's loading screens.
"""

SETTLE = 1.5
LOCATION = "outside_church_underground_01"

PRE_JOIN = []

STEPS = [
    ("host", "fe-reset"),
    ("client", "fe-reset"),
    ("client", "fe-jump", "loc:auto", {"settle": 4}),
    ("host", "fe-check-host"),
    ("client", "fe-check-client"),
    ("client", "heal"),
    # Jumps there and back (each over 3000, which the host takes as the game moving the player)
    ("client", "fe-jump", "off:0,0", {"settle": 2}),
    ("client", "fe-jump", "loc:auto", {"settle": 2.5}),
    ("client", "fe-jump", "off:0,0", {"settle": 2.5}),
    ("client", "fe-jump", "loc:auto", {"settle": 3}),
    ("host", "fe-churn"),
    ("client", "fe-churn"),
    ("client", "heal"),
    # Both there, then the client goes, then the host
    ("host", "fe-jump", "to-client", {"settle": 3}),
    ("host", "fe-hostin"),
    ("client", "fe-jump", "off:0,0", {"settle": 3}),
    ("host", "fe-stay"),
    ("host", "heal"),
    ("host", "fe-jump", "off:0,0", {"settle": 1}),
    ("host", "fe-left"),
    # A generator far from the host drains by the client's skill
    ("client", "fe-jump", "gen:far", {"settle": 2}),
    ("host", "fe-generator"),
    ("client", "fe-jump", "off:0,0", {"settle": 2}),
    ("host", "heal"),
    ("client", "heal"),
    # The host goes into a separate location (its loading screen) while the client is far from it
    ("client", "fe-jump", "loc:auto", {"settle": 2.5}),
    ("host", "loc-go", LOCATION, {"settle": 3}),
    ("host", "loc-back", {"settle": 3}),
    ("host", "fe-loading"),
    ("client", "fe-jump", "off:0,0", {"settle": 3}),
    ("client", "heal"),
    ("host", "heal"),
    ("host", "alive"),
    ("client", "alive"),
]

REQUIRE = ["time"]
