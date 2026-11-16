"""Map test steps for story triggers that a player's shared action sets off; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioStory.cs.

A door opened or closed, a thing switched on or off and a trap disarmed are shared actions, which the other games
replay. A trigger on the thing (onOpenDoor, onTurnOn, onDisarmed) started in every replay as a whole event: the host's
player got the steps of the client who opened the door, and the client's own request came later and was refused as
already happened. Now only the player who did it sets the trigger off, and the others run its shared part.

Each trigger has two steps that act on the player who runs them and one shared step (act-count ID OWN SHARED).
D1 (once, a door): the client opens it, then the host as a later player. D2 (each time, a door): the host, then the
client. S1 (each time, a lamp switched on): the client. P1 (once, a trap disarmed; the host rolls it): the client.
"""

PRE_JOIN = []

TRIGGERS = [
    ("D1", "door:0", "onOpenDoor", "once"),
    ("D2", "door:1", "onOpenDoor", "multi"),
    ("S1", "Lamp_round_yellow", "onTurnOn", "multi"),
    ("P1", "trap:beartrap@0,-60", "onDisarmed", "once"),
]

STEPS = [
    *[(who, "act-setup", *t) for t in TRIGGERS for who in ("host", "client")],
    # D1: the client opens the door: its own steps run in its game, the shared step once in each game, and none of
    # the player's steps for the host's player. Then the host, as a later player, still gets its own part.
    ("client", "act-do", "D1", "open", "2"),
    ("host", "act-count", "D1", "0", "1"),
    ("client", "act-count", "D1", "2", "1"),
    ("host", "act-do", "D1", "close", "0"),
    ("host", "act-do", "D1", "open", "2"),
    ("client", "act-count", "D1", "2", "1"),
    ("host", "act-count", "D1", "2", "1"),
    # D2: the host's own door, replayed in the client's game: the client runs only the shared step.
    ("host", "act-do", "D2", "open", "2"),
    ("client", "act-count", "D2", "0", "1"),
    ("host", "act-count", "D2", "2", "1"),
    ("client", "act-do", "D2", "close", "0"),
    ("client", "act-do", "D2", "open", "2"),
    ("host", "act-count", "D2", "2", "2"),
    ("client", "act-count", "D2", "2", "2"),
    # S1: the client switches a lamp on.
    ("client", "act-do", "S1", "on", "2"),
    ("host", "act-count", "S1", "0", "1"),
    ("client", "act-count", "S1", "2", "1"),
    # P1: the client disarms a trap; the host rolls it and the client's own disarm sets the trigger off.
    ("client", "act-do", "P1", "disarm", "2"),
    ("host", "act-count", "P1", "0", "1"),
    ("client", "act-count", "P1", "2", "1"),
]
REQUIRE = []
