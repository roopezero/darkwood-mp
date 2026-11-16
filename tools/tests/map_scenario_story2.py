"""Map test steps for story progression that needed the host; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioStory2.cs (and map_scenario_later.py's later-* steps).

B: a world trigger a joiner caused ran whole on the host, so its player part went to the host's player. C1 is a
once-only refreshWhenTurnedOn trigger, set off by a lamp while it's on (the way the game's machines do it). The client
switches the lamp on: the client gets the item and the host doesn't, and the shared part runs once in each game.

D: a once-only trigger's own part (a trip, an item) owed to a later player was lost for a mid-game joiner and after
the world was loaded again, as what was owed was filled only by EventFired. D1: the host sets it off, then the client
gets it as already fired from its loaded world (no EventFired): the client still gets its trip and item, once, and
not again after its world is "loaded again" (the trigger made anew). D2: the client sets it off first; the host's game
saves and starts again, and its trigger is made anew as already fired: the host's player still gets its own part."""

PRE_JOIN = []

LAMP = "Lamp_round_yellow"
D1 = ("D1", "300", "300", "count", "item", "trip", "700", "300")
D2 = ("D2", "-300", "300", "count", "item", "trip", "-700", "300")

STEPS = [
    *[(who, "caused-setup", "C1", LAMP, "0", "350") for who in ("host", "client")],
    ("client", "switch", LAMP, "on", {"settle": 2}),
    ("client", "caused-check", "C1", "1", "1"),
    ("host", "caused-check", "C1", "1", "0"),
    ("client", "switch", LAMP, "off"),
    # D1: fired by the host before the client's game had it
    ("host", "later-setup", *D1),
    ("host", "later-fire", "D1"),
    ("client", "later-setup", *D1),
    ("client", "owed-loaded", "D1"),
    ("client", "later-fire", "D1"),
    ("client", "later-again", "D1"),
    ("client", "later-setup", *D1),
    ("client", "owed-loaded", "D1"),
    ("client", "later-again", "D1"),
    # D2: fired by the client first; the host's game is started again
    *[(who, "later-setup", *D2) for who in ("host", "client")],
    ("client", "later-fire", "D2"),
    ("host", "later-count", "D2", "1"),
    ("host", "owed-reload"),
    ("host", "later-setup", *D2),
    ("host", "owed-loaded", "D2"),
    ("host", "later-fire", "D2"),
    ("host", "later-again", "D2"),
    ("host", "heal"),
    ("client", "heal"),
]
