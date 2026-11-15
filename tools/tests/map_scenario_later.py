"""Map test steps for once-only triggers with a player's own steps; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioLater.cs.

A once-only trigger's player steps (a trip somewhere, an item given) ran only for whoever set it off: the next player
found it marked fired and got nothing, stuck on the wrong side of a hole or a ladder. Each later player now gets their
own part (ScriptedEvents.LaterStep) once, and the shared part still runs once in every game. L1: the host first, then
the client. L2: the client first, then the host.

And a trigger set off the moment a player arrives somewhere reached the host before the player's new position did,
and was refused as too far away (a once-only one was lost). A1 takes the client 3600 away, to A2, which it walks into
the frame it arrives: the host must take it."""

PRE_JOIN = []

TRIGGERS = [
    ("L1", "300", "300", "count", "item", "trip", "700", "300"),
    ("L2", "-300", "300", "count", "item", "trip", "-700", "300"),
    ("A1", "0", "-400", "trip", "3600", "-400"),
    ("A2", "3600", "-400", "count"),
]

STEPS = [
    *[(who, "later-setup", *t) for t in TRIGGERS for who in ("host", "client")],
    # L1: the host sets it off first, then the client gets its own part (item and trip), the shared part ran once each
    ("host", "later-fire", "L1"),
    ("client", "later-count", "L1", "1"),
    ("client", "later-fire", "L1"),
    ("host", "later-count", "L1", "1"),
    ("client", "later-count", "L1", "1"),
    ("client", "later-again", "L1"),
    ("host", "later-again", "L1"),
    # L2: the client first (a request), then the host's own part, in its own game
    ("client", "later-fire", "L2"),
    ("host", "later-count", "L2", "1"),
    ("host", "later-fire", "L2"),
    ("host", "later-count", "L2", "1"),
    ("client", "later-count", "L2", "1"),
    ("host", "later-again", "L2"),
    # a trigger set off the moment the client arrives somewhere far
    ("client", "arrive-go", "A1", "A2"),
    ("host", "later-count", "A2", "1"),
    ("host", "heal"),
    ("client", "heal"),
]
