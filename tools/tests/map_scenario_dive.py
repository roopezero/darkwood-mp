"""Map test steps for steps that act on the player and for diving in the village cellar; same format as
map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioDive.cs.

A step whose target is the player (the unique object "player", or the player's own object) ran as a shared step, so
each game ran it on its own player: one player diving in the cellar (runFunction diveIn on "player") made every player
dive. It's now the player's own. O1 (each time): the client sets it off, only the client's player is acted on, then
the host. O2 (once): the client first; the host, as a later player, still gets its own part, and the shared step ran
once in each game.

T1: the cellar's dive is a short scripted trip (transportPlayerToObject); the host's movement check took such a jump
for a speed hack and put the client back. The host now expects a trip it agreed to.

Last, the client goes into the cellar and logs its dive triggers (dive-scan: requirements, steps, the tank items).
"""

PRE_JOIN = []
IN_MAPS = False

CELLAR = "outside_village_ch2_cellar_01"

STEPS = [
    *[(who, "own-setup", *t) for t in [("O1", "300", "-300", "multi"), ("O2", "-300", "-300", "once")] for who in ("host", "client")],
    ("client", "own-fire", "O1", "2"),
    ("host", "own-count", "O1", "0", "1"),
    ("client", "own-count", "O1", "2", "1"),
    ("host", "own-fire", "O1", "2"),
    ("client", "own-count", "O1", "2", "2"),
    ("host", "own-count", "O1", "2", "2"),
    ("client", "own-fire", "O2", "2"),
    ("host", "own-count", "O2", "0", "1"),
    ("host", "own-fire", "O2", "2"),
    ("client", "own-count", "O2", "2", "1"),
    ("host", "own-count", "O2", "2", "1"),
    ("client", "own-fire", "O2", "0"),
    # A scripted trip of 1800 (over what the movement check lets a player walk in half a second, under its teleport
    # distance) that the host agreed to: it must not put the client back.
    *[(who, "trip-setup", "T1", "0", "600", "1800", "600") for who in ("host", "client")],
    ("client", "trip-go", "T1"),
    # O3: a trigger that fires each time with events that run once, as the cellar's first dive (its dream): the host
    # first, then the client still gets its own part, once.
    *[(who, "own-setup", "O3", "0", "-600", "eventsonce") for who in ("host", "client")],
    ("host", "own-fire", "O3", "2"),
    ("client", "own-count", "O3", "0", "1"),
    ("client", "own-fire", "O3", "2"),
    ("client", "own-count", "O3", "2", "1"),
    ("host", "own-count", "O3", "2", "1"),
    ("client", "own-fire", "O3", "0"),
    # K1: the full oxygen tank. Without it the client gets the "no tank" branch; once the host has held one, the
    # flag reaches the client and is saved, and the tank's branch runs for every player, also after the host lost it.
    *[(who, "tank-setup", "K1", "-600", "0") for who in ("host", "client")],
    ("client", "tank-fire", "K1", "0", "1"),
    ("client", "tank-flag", "0"),
    ("host", "tank-give"),
    ("client", "tank-flag", "1"),
    ("host", "tank-take"),
    ("host", "tank-flag", "1"),
    ("client", "tank-fire", "K1", "1", "1"),
    ("client", "tank-fire", "K1", "2", "1"),
    ("host", "tank-fire", "K1", "1", "0"),
    ("client", "loc-go", CELLAR, {"settle": 3}),
    ("client", "dive-scan", "cellar"),
    ("client", "loc-back"),
]
REQUIRE = []
