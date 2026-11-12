"""Map test steps for the savages' (Villager kinds') attack animation, with dogs as the control; same format as
map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioSavage.cs. Run with
MAP_SCENARIO=map_scenario_savage tools/run-test.sh map. SV_ONLY=Kind1,Kind2 runs only those kinds.

Players reported savages that, attacking a player, looped one or two frames instead of their attack. For each kind
the host spawns it beside the client's player; both games sample every frame, for some seconds, each of the host's
enemies' sprite animators (sv-sample, at the same moment), and each fails if, there, a clip kept to one or two of
its frames while playing for a second or more; the client then checks it showed what the host's did (sv-compare).
Three rounds per kind: it attacks the standing client again and again; the client walks off mid-attack and comes
back; the host's player hits it (its threat goes to the host) and walks back. Then several savages at once.
"""

import os

SETTLE = 1.0

KINDS = ["Villager", "VillagerPitchfork", "Villager3_plank", "VillagerTorch", "Villager1_Burning", "Redneck03",
         "DogMutated", "Dog"]
ONLY = [k for k in os.environ.get("SV_ONLY", "").split(",") if k]
if ONLY:
    KINDS = ONLY

PRE_JOIN = []

SAFE = [("host", "i2-safe", "on", {"settle": 0}), ("client", "i2-safe", "on", {"settle": 0})]

STEPS = SAFE + [("host", "ee-day"), ("client", "ee-arena"), ("host", "heal")]


def rounds(tag, kind, n=1):
    return [
        ("host", "sv-spawn", kind, n, {"settle": 2.0}),
        ("host+client", "sv-sample", "AT", 10, f"{tag}-a", {"lead": 3}),
        ("client", "sv-compare", f"{tag}-a"),
        ("host+client", "sv-sample", "AT", 10, f"{tag}-b", "--walk", {"lead": 3}),
        ("client", "sv-compare", f"{tag}-b"),
        ("host+client", "sv-sample", "AT", 12, f"{tag}-c", "--provoke", {"lead": 3}),
        ("client", "sv-compare", f"{tag}-c"),
        ("host", "sv-clear"),
        ("client", "heal"), ("host", "heal"),
        ("client", "ee-arena", {"settle": 0.5}),
    ]


for kind in KINDS:
    STEPS += rounds(kind, kind)
if not ONLY:
    STEPS += rounds("many", "Villager,VillagerPitchfork,Villager3_plank")

STEPS += [("client", "heal"), ("host", "alive"), ("client", "alive"), ("host", "ee-day", "off"),
          ("host", "i2-safe", "off", {"settle": 0}), ("client", "i2-safe", "off", {"settle": 0})]

POST = []
REQUIRE = []
