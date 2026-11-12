"""Map test steps for shared progression and the level dreams dreamt together; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioDreams.cs. They run after the dumps (a dream moves the players,
swaps their bags and sets the clock), before the client's death for good."""

DREAM = "dream_bunker_underground_01"

POST = [
    # The host sent the shared essence and level with the character; our bar shows it.
    ("client", "progress-shared"),
    # Out of the night trader's dream (the client's own, from the kill at the end of the steps).
    ("client", "dream-clear", {"settle": 0}),
    ("host", "dream-clear", {"settle": 0}),
    # The host's level brings a dream: the client goes into the same one by itself.
    ("host", "level-dream", DREAM, {"settle": 0}),
    ("client", "in-host-dream", DREAM),
    # The host's dream ends: the client's ends with it.
    ("host", "end-level-dream", {"settle": 0}),
    ("client", "back-from-dream"),
    ("client", "heal"),
]
