"""Map test steps for a client's trap that the host refuses; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioWorld.cs (trap-step, trap-refuse). Run with tools/run-test.sh map:traprefuse.

A client's player sets a trap off at once in its own game and asks the host (TrapFired as an Authority request).
The host refuses it when the trap had gone off there just before ("race": the host's own TrapFired for it is on its
way) or isn't there ("gone"). What the trap spawns (a vine trap's vine) waits for the host's yes, and a refused
trap's bite (the bear trap's hold, its damage) is undone. Each refusal is followed by a compare of every game: the
client must have no vine, trap or item the host hasn't."""

SAFE = [("host", "i2-safe", "on", {"settle": 0}), ("client", "i2-safe", "on", {"settle": 0})]

STEPS = SAFE + [
    ("host", "heal"),
    ("client", "heal"),
    # A vine trap that isn't on the host any more: no vine in either game.
    ("client", "trap-away"),
    ("host", "trap-place", "chainTrap", "0", "-60"),
    ("host", "trap-refuse", "gone"),
    ("client", "trap-step", "chainTrap@0,-60", "refused", {"settle": 2}),
    ("host", "trap-refuse", "off"),
    ("all", "compare", "host", "traps"),
    # A vine trap that went off on the host just before: the host's vine, one in each game.
    ("client", "trap-away"),
    ("host", "trap-place", "chainTrap", "0", "-60"),
    ("host", "trap-refuse", "race"),
    ("client", "trap-step", "chainTrap@0,-60", "refused", {"settle": 2}),
    ("host", "trap-refuse", "off"),
    ("all", "compare", "host", "traps"),
    # A bear trap the host refuses: our player isn't held, and gets its health back.
    ("client", "heal"),
    ("client", "trap-away"),
    ("host", "trap-place", "beartrap", "0", "-60"),
    ("host", "trap-refuse", "gone"),
    ("client", "trap-step", "beartrap@0,-60", "refused", {"settle": 2}),
    ("host", "trap-refuse", "off"),
    ("all", "compare", "host", "traps"),
    # Not here: one the host allows. Its vine then holds the client's player in the client's game only (the host's
    # copy is spawned untargeted), which the vines section's "attached" shows; that was so before this test too.
    ("host", "heal"),
    ("client", "heal"),
    ("host", "i2-safe", "off", {"settle": 0}), ("client", "i2-safe", "off", {"settle": 0}),
]

REQUIRE = ["traps"]
