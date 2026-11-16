"""Map test steps for steps that act on the player and for diving in the village cellar; same format as
map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioDive.cs.

A step whose target is the player (the unique object "player", or the player's own object) ran as a shared step, so
each game ran it on its own player: one player diving in the cellar (runFunction diveIn on "player") made every player
dive. It's now the player's own. O1 (each time): the client sets it off, only the client's player is acted on, then
the host. O2 (once): the client first; the host, as a later player, still gets its own part, and the shared step ran
once in each game.

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
    ("client", "loc-go", CELLAR, {"settle": 3}),
    ("client", "dive-scan", "cellar"),
    ("client", "loc-back"),
]
REQUIRE = []
