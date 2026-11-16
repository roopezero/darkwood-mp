"""Things players bump into and shove, with several clients: MAP_SCENARIO=map4_push tools/run-test.sh map4 (the host
and three client games; MAP_CLIENTS=4 for four). The steps are in src/DarkwoodMP/World/MapScenarioPush.cs. DWMP_LAG_MS=120 in the
environment holds every message 120 ms in every game, as an internet link does (localhost has none).

Players push furniture, crates, lamps and open doors' leaves with their bodies, through the physics, as walking into
them does, or knock them with a hit. The reported bug: one player pushes a thing and the others still see it where it
was; when one of them then bumps into it there, it jumps back for the first player too. So each push is checked in
every game (the host's and every other client's), then another player bumps the same thing where their own game has
it, and every game is compared again (the `items` section: where each thing is; `loose`: its turn; `doors`; `leaves`: each open door's leaf angle):

- c1 bumps thing 0; c2 bumps it on from another side; the host bumps thing 1 and c3 bumps it on;
- c2 leans on thing 2 (keeps pressing for 5 s after it moved: into a wall, a corner, or just held);
- c3 knocks thing 3 with a hit from 150 away (a shot's push: further than a body's touch);
- c1 and c2 bump things one right after the other, with no compare between;
- c3 goes far away (its copy of the test area switched off by its culling), c1 bumps thing 4 meanwhile, c3 comes
  back, and the games are compared before c3 bumps it on;
- c1 opens the door nearest the centre, c2 and the host walk through its leaf;
- c1 and c2 go to a house far from the host, where things are named by where they stand (no save id), and push
  things there with the host far away (the host's game runs that part of the world for them);
- every player stands in another (soft collision: they're eased apart).
"""
import os

SETTLE = 1.5
CLIENTS = int(os.environ.get("MAP_CLIENTS", "3"))
FAR = "6000"

SECTIONS = "items,loose,doors"
WITH_LEAVES = SECTIONS + ",leaves"  # once a door is open (an empty section fails the compare)


def cmp(who="host", sections=SECTIONS):
    return ("all", "compare", who, sections)


def at(k):
    return [("host", "ps-at", k), ("all-clients", "ps-at", k)]


STEPS = [
    ("host", "heal"),
    ("all-clients", "heal"),
    ("host", "ps-list"), ("all-clients", "ps-list"),
    cmp(),
    # one pushes, another bumps it on where their game has it
    ("c1", "ps-bump", "0", "0"), *at("0"), cmp(),
    ("c2", "ps-bump", "0", "90"), *at("0"), cmp(),
    ("host", "ps-bump", "1", "180"),
    ("c3", "ps-bump", "1", "90"), *at("1"), cmp(),
    # pressed into whatever is behind it, and held there
    ("c2", "ps-lean", "2", "270"), *at("2"), cmp(),
    # a hit from further than a body's touch
    ("c3", "ps-hit", "3"), *at("3"), cmp(),
    # two pushes right after each other
    ("c1", "ps-bump", "0", "180", "60", {"settle": 0}),
    ("c2", "ps-bump", "1", "270"),
    cmp(),
    # pushed while another player's copy of it is switched off; they come back and bump it on
    ("c3", "far-spot", FAR, "0", {"settle": 3}),
    ("c1", "ps-bump", "4", "90"),
    ("c3", "mt-home", {"settle": 3}),
    *at("4"), cmp(),
    ("c3", "ps-bump", "4", "270"), *at("4"), cmp(),  # back the way c1 pushed it (a wall is west of it)
    # an open door's leaf, walked through
    ("c1", "toggle-door", "open", {"settle": 3}),
    ("c2", "ps-door", "0"), cmp(sections=WITH_LEAVES),
    ("host", "ps-door", "180"), cmp(sections=WITH_LEAVES),
    # players standing in each other
    ("c1", "ps-overlap"),
    ("c2", "ps-overlap"),
    ("host", "ps-overlap"),
    # a house far from the host: things named by where they stand
    ("c1", "far-spot", FAR, "1"),
    ("c2", "far-spot", FAR, "1", {"settle": 3}),
    ("c1", "ps-list", "new"), ("c2", "ps-list", "new"),
    cmp("c1"),
    ("c1", "ps-bump", "0", "0"), ("c2", "ps-at", "0"), cmp("c1"),  # a heavy wardrobe: it goes a little way
    ("c1", "ps-bump", "2", "0"), ("c2", "ps-at", "2"), cmp("c1"),
    ("c2", "ps-bump", "2", "90"), ("c1", "ps-at", "2"), cmp("c1"),
    ("c1", "ps-lean", "1", "180"), cmp("c1"),
    ("c1", "mt-home"), ("c2", "mt-home", {"settle": 3}),
    cmp(),
]

REQUIRE = ["items", "loose", "doors"]  # not leaves: none until a door is open
