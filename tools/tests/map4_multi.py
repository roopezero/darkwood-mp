"""A map4 scenario for several remote players at once (MAP_SCENARIO=map4_multi MAP_CLIENTS=3 tools/run-test.sh map4):
the host and client games c1..c3. Steps are in src/DarkwoodMP/World/MapScenarioMulti.cs (mt-*), with some from the
other scenarios. ("c1+c2+c3", STEP, ..., "AT", ...) runs a step in those games at once, AT being a moment a few
seconds ahead that they all act at (mapdriver.py).

1. Threat: an enemy between c1 and c2. c1 hits it, then c2 hits it harder: it turns to c2 although c1 is nearer
   (it goes by threat, not by who is closest); c3, standing away, is never its target. c2 kills it: it's dead
   with a searchable corpse in every game, c1 takes from the corpse and the dumps agree.
2. One container slot taken by c1, c2 and c3 at the same moment: exactly one gets it, the others are rolled back,
   every game agrees on what it holds.
3. One trader, two players: one talks to him, the other is told he's busy (TalkLock), then both trade with him, and
   once more at the same moment; his stock is the same in every game.
4. c3 far away (its copy of the test area switched off) while c1 opens a door, takes and drops things; c3 walks back
   and sees the door drawn open, not only flagged so, and the things where they are.
5. Night attackers: c1, c2 and the host in one hideout, c3 in another: each hideout gets one attacker each time,
   the shared hideout no more than the other (a budget per occupied hideout).
POST: c3 holds a container open and leaves: the host releases its lock, and c1 can open it.
"""
SETTLE = 1.5
CONTAINER = "Wardrobe_low_small_01"

STEPS = [
    ("host", "heal"),
    ("all-clients", "heal"),

    # 1. Threat among several remote players (kept alive: the dog bites while the host's steps wait).
    ("all-clients", "mt-tough", "on"),
    ("host", "mt-debug-threat", "on"),
    ("host", "mt-stand", "0", "-1200"),
    ("c1", "mt-stand", "-150", "1200"),  # close enough for c2 to stay in the dog's sight
    ("c2", "mt-stand", "150", "1200"),
    ("c3", "mt-stand", "0", "2900"),
    ("host", "mt-spawn", "DogMutated", "0", "1200", {"settle": 1}),
    ("c1", "mt-hit", "1", "3", {"settle": 0}),
    ("host", "mt-target", "c1", "12"),
    ("c2", "mt-hit", "4", "9", {"settle": 0}),  # the dog has 45 health
    ("host", "mt-target", "c2", "15", "--nearer", "c1"),
    ("c1", "heal"),
    ("c2", "heal"),
    ("host", "mt-never", "c3"),
    ("c2", "mt-hit", "40", "60", "--kill"),
    ("host", "mt-never", "c3"),
    ("host", "mt-dead"),
    ("c1", "mt-dead"),
    ("c2", "mt-dead"),
    ("c3", "mt-dead"),
    ("c1", "mt-corpse", "--take", {"settle": 2}),
    ("all", "compare", "c1", "enemies"),
    ("c3", "mt-corpse"),
    ("host", "mt-stand", "0", "0"),
    ("all-clients", "mt-stand", "0", "0"),
    ("all-clients", "mt-tough", "off"),
    ("all-clients", "heal"),

    # 2. One container slot, three players at the same moment.
    ("host", "put-item", CONTAINER),
    ("host", "mt-stand", "0", "0"),
    ("c1+c2+c3", "mt-grab", CONTAINER, "AT", {"winners": 1, "settle": 2}),
    ("host", "mt-locks", "0"),
    ("all", "compare", "host", "containers"),

    # 3. One trader, two players.
    ("host", "trader-restock"),
    ("c1", "mt-talk"),
    ("c2", "mt-talk", "--refused"),
    ("c1", "talk-end", {"settle": 3}),
    ("c2", "mt-talk"),
    ("c2", "talk-end", {"settle": 3}),
    ("c1", "trade"),
    ("c2", "trade", {"settle": 2}),
    ("c1+c2", "trade", "--race", "AT", {"min_winners": 1, "settle": 2}),
    ("all", "compare", "c1", "traders"),
    ("all-clients", "mt-stand", "0", "0"),

    # 4. Done while c3 is far away, seen when it's back.
    ("c1", "toggle-door", "close"),
    ("c3", "far-spot", "6000", "0", {"settle": 3}),
    ("c1", "toggle-door", "open"),
    ("c1", "take-item"),
    ("c1", "drop-item"),
    ("all", "compare", "c1", "doors,containers,dropped"),
    ("c3", "mt-home", {"settle": 3}),
    ("c3", "mt-door-drawn"),
    ("c2", "mt-door-drawn"),
    ("host", "mt-door-drawn"),
    ("all", "compare", "c3", "doors,containers,dropped"),

    # 5. Night attackers with players in two hideouts.
    ("host", "mt-stand", "0", "0"),
    ("c3", "mt-hideout-go", {"settle": 3}),
    ("host", "mt-night", "3", {"settle": 3}),
    ("c3", "mt-home", {"settle": 3}),

    ("host", "alive"),
    ("all-clients", "alive"),

    # A client's death: its story flags (player_diedAtLeastOneTime) reach every game.
    ("c1", "mt-die", {"settle": 3}),
    ("all", "compare", "c2", "doors"),
]

POST = [
    ("c3", "hold-open", CONTAINER),
    ("host", "mt-locks", "1"),
    ("c3", "mt-leave", {"settle": 2}),
    ("host", "mt-locks", "0"),
    ("c1", "mt-open-free", CONTAINER),
]

REQUIRE = ["doors", "containers", "dropped", "items", "story", "time"]
