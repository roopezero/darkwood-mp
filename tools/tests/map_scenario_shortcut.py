"""Map test steps for the village shortcut in chapter 1 (the hole in the village's fence); same format as
map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioShortcut.cs. Run with tools/run-test.sh
map:shortcut.

Going through the hole on the village side fires the world event openVillageShortcut, which opens the hole on the
farm side. The village is a separate location (its own scene), so the hole on its side is in a game only once that
game has loaded the village. First the host opens it while the client has never been to the village (the client's
game has no such hole, and runs the world event by name), then it is closed again in both games and the client
opens it with the host far away (the host's game gets it open), and both go through the open hole into the village.

A late joiner gets it open from the host's save; that case passed with the host opening it before the join (in
PRE_JOIN), but then the host's save also has the village, so this order is the one kept."""

IN_MAPS = True
SHOTS = "/tmp/darkwoodmp-test/map-shortcut/shots"

PRE_JOIN = [
    ("host", "vs-info"),
    ("host", "vs-check", "closed"),
]

STEPS = [
    ("client", "vs-check", "closed"),
    # The host opens it; the client, which has never been to the village, gets it open.
    ("host", "vs-open", {"settle": 3}),
    # What the host sees by the hole on the farm side (one player couldn't see their left there).
    ("host", "vw-check", SHOTS, "farm-side"),
    ("host", "vs-check", "open"),
    ("client", "vs-check", "open"),
    # Closed again; the client opens it with the host far away, and the host gets it open.
    ("host", "vs-reset"),
    ("client", "vs-reset"),
    # The walk home crosses the meadow's mushroom traps, whose gas can kill the host after it arrives: safe for it,
    # until the end (the gas went on killing it while the client took its turn).
    ("host", "i2-safe", "on"),
    ("host", "mt-home"),
    ("client", "vs-check", "closed"),
    ("client", "vs-open", {"settle": 3}),
    ("client", "vw-check", SHOTS, "farm-side"),
    ("client", "vs-check", "open"),
    ("host", "vs-check", "open"),
    # Both go through the open hole into the village, and back to the world.
    ("host", "vs-pass", {"settle": 3}),
    ("host", "vw-check", SHOTS, "village"),
    ("client", "vw-check", SHOTS, "host-in-village"),
    ("host", "vs-leave"),
    ("client", "vs-pass", {"settle": 3}),
    ("client", "vw-check", SHOTS, "village"),
    ("host", "vw-check", SHOTS, "client-in-village"),
    ("client", "vs-leave"),
    ("host", "i2-safe", "off"),
]
