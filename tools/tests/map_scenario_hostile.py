"""Map test steps for hostile NPCs (gap 33: Combat.HostileNpcs); same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioHostileNpcs.cs. Two NPCs are put in the test area before the join, so both games
have them from the save. The client's player hits the first: the host's copy turns on the client's stand-in, the
host runs it as an enemy and the client shows the host's; it hurts the client; both players' hits go into its one
health (the dumps' "enemies" section has a hostile NPC's health); and it dies in both games. The second is sent
after the client by a scripted attack in the client's game (as a dialogue's outcome does)."""

SETTLE = 1.5

FIRST = "Wolfman_att"  # a fighter: 100 health, attacks players once hostile
SECOND = "Doctor_confronted2"

PRE_JOIN = [
    ("host", "npc-spawn", FIRST, "600", "400"),
    # Away from the gas bottle the world module sets off (-480,320 from the centre, 250 across) and the wardrobe it
    # burns (-630,330): at -600,400 the bottle killed the doctor (1 health) before any of its steps.
    ("host", "npc-spawn", SECOND, "-600", "900"),
]

# Both players healed whenever they're below half health (i2-safe): the hostile NPC lands every hit while the
# steps between its own run (dumps compared, the other NPC provoked), and a test player dying changes the story.
STEPS = [
    ("host", "i2-safe", "on", {"settle": 0}),
    ("client", "i2-safe", "on", {"settle": 0}),
    ("host", "npc-friendly", FIRST),
    ("client", "npc-friendly", FIRST),
    # The client's hit goes to the host's copy, which turns on the client.
    ("client", "npc-hit", FIRST, "20"),
    ("host", "npc-host-runs", FIRST, "client"),
    ("both", "compare", "client", "enemies,story"),
    ("client", "npc-attacks-us", FIRST),
    ("client", "heal"),
    # The host's hit goes into the same health.
    ("host", "npc-hit", FIRST, "20"),
    ("both", "compare", "client", "enemies,story"),
    # Killed at once: it shoots, and goes for the host too now (a death changes the story flags the dumps compare).
    ("client", "npc-kill", FIRST),
    ("host", "heal"),
    ("client", "heal"),
    ("host", "npc-dead", FIRST, "wolfman", "wolf"),
    ("client", "npc-dead", FIRST, "wolfman", "wolf"),
    # A scripted attack in the client's game: the host's copy comes after the client (the host keeps out of it).
    ("host", "npc-keep-away", SECOND, "2500"),
    ("client", "npc-provoke", SECOND),
    ("host", "npc-host-runs", SECOND, "client"),
    ("client", "npc-kill", SECOND),
    ("host", "npc-dead", SECOND, "doctor"),
    ("client", "npc-dead", SECOND, "doctor"),
    ("client", "heal"),
    ("host", "heal"),
    # The story after both deaths (the flags they set are listed in each game's log).
    ("host", "npc-dead", FIRST, "wolfman", "_"),
    ("client", "npc-dead", FIRST, "wolfman", "_"),
    ("host", "alive"),
    ("client", "alive"),
    ("host", "i2-safe", "off", {"settle": 0}),
    ("client", "i2-safe", "off", {"settle": 0}),
]

REQUIRE = ["enemies", "story"]

POST = []
