"""The scenario of the `map4` test (tools/run-test.sh map4): the host and three client games c1..c3 (the mod's
limit of 4 players), or four with MAP_CLIENTS=4 (c4's steps are left out without it).
Same format as map_scenario.py; "all-clients" runs a step in each client game in turn, and
("all", "compare", WHO, SECTIONS) dumps every game around WHO's player and compares each client's with the host's.

First each client does a door, a take and a drop in the test area. Then c3 and c4 go far from the host, each to a
spot of its own ("far-spot MIN INDEX": the INDEXth house with a door and a container MIN or more from the test area,
in MapScenarioFar.cs), out of the part of the world the host's player and the other clients have switched on, and
do the same there; c1 and c2 go together to a third spot. The dumps around each far player must match the host's
(whose game switches the world on around every player) in every client, also those whose own copy of that part is
switched off. An enemy is sent only to the clients within 3000 of it (mapcompare.py's SECTION_RANGE).

To run part of it, or another scenario: MAP_SCENARIO=module tools/run-test.sh map4 (a module in tools/tests with
STEPS and optionally PRE_JOIN, POST, REQUIRE).
"""
import os

SETTLE = 1.5
CLIENTS = int(os.environ.get("MAP_CLIENTS", "3"))  # c4's steps only with a fourth client game
FAR = "6000"  # well beyond WorldGrid's switched-on area around the host (a few thousand at most)

PRE_JOIN = [
    ("host", "toggle-door", "open"),
]


def _near(who, door):
    return [(who, "toggle-door", door), (who, "take-item"), (who, "drop-item")]


def _far(who):
    return [(who, "toggle-door"), (who, "take-item"), (who, "drop-item")]


def _if4(steps):
    return steps if CLIENTS >= 4 else []


STEPS = [
    ("host", "heal"),
    ("all-clients", "heal"),
    ("host", "drop-item", "gasoline"),  # the host's bags are full of the test kit: room for what it takes
    ("host", "take-item"),
    ("host", "drop-item"),
    *_near("c1", "close"),
    *_near("c2", "open"),
    *_near("c3", "close"),
    *_if4(_near("c4", "open")),
    ("all", "compare", "host", "doors,containers,dropped"),

    # c3 and c4 far from the host, each at its own spot.
    ("c3", "far-spot", FAR, "0", {"settle": 3}),
    *_far("c3"),
    *_if4([("c4", "far-spot", FAR, "1", {"settle": 3}), *_far("c4")]),
    ("all", "compare", "c3", "doors,containers,dropped"),
    *_if4([("all", "compare", "c4", "doors,containers,dropped")]),

    # c1 and c2 together at a third spot.
    ("c1", "far-spot", FAR, "2"),
    ("c2", "far-spot", FAR, "2", {"settle": 3}),
    ("c1", "toggle-door"),
    ("c2", "take-item"),
    ("c1", "take-item"),
    ("c2", "drop-item"),
    ("c1", "drop-item"),
    ("all", "compare", "c1", "doors,containers,dropped"),

    ("host", "time", "+1", {"settle": 3}),
    ("host", "alive"),
    ("all-clients", "alive"),
]

REQUIRE = ["doors", "containers", "dropped", "items", "story", "time"]
