"""Map test steps for story progression that needed the host; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioStory2.cs.

B: a world trigger a joiner caused ran whole on the host, so its player part went to the host's player. C1 is a
once-only refreshWhenTurnedOn trigger, set off by a lamp while it's on (the way the game's machines do it). The client
switches the lamp on: the client gets the item and the host doesn't, and the shared part runs once in each game."""

PRE_JOIN = []

LAMP = "Lamp_round_yellow"

STEPS = [
    *[(who, "caused-setup", "C1", LAMP, "0", "350") for who in ("host", "client")],
    ("client", "switch", LAMP, "on", {"settle": 2}),
    ("client", "caused-check", "C1", "1", "1"),
    ("host", "caused-check", "C1", "1", "0"),
    ("client", "switch", LAMP, "off"),
    ("host", "heal"),
    ("client", "heal"),
]
