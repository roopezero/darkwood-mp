"""Map test steps for the village shortcut when a joiner gets to the village first; same format as
map_scenario.py, steps in src/DarkwoodMP/World/MapScenarioShortcut.cs and MapScenarioView.cs.

The client goes through the hole on the village side first (the host's game loads the village for it, without going
there: World.Locations), then the host comes through the open hole on the farm side into the village, as a host did
when it couldn't see its left side. Each arrival is screenshotted and its view checked (vw-check)."""

IN_MAPS = True
SHOTS = "/tmp/darkwoodmp-test/map-shortcut2/shots"

PRE_JOIN = []

STEPS = [
    ("host", "i2-safe", "on", {"settle": 0}),
    ("client", "i2-safe", "on", {"settle": 0}),
    ("client", "vs-check", "closed"),
    ("client", "vs-open", {"settle": 3}),
    ("client", "vw-check", SHOTS, "farm-side"),
    ("host", "vs-check", "open"),
    ("client", "vs-pass", {"settle": 3}),
    ("client", "vw-check", SHOTS, "village"),
    # The host walks over to the farm side's hole (5382 -5253), where the client came out, and goes through it.
    ("host", "goto", "5500", "-5250", {"settle": 3}),
    ("host", "vs-pass", {"settle": 3}),
    ("host", "vw-check", SHOTS, "village"),
    ("client", "vw-check", SHOTS, "village-host-in"),
    ("host", "vs-leave"),
    ("host", "vw-check", SHOTS, "back"),
    ("client", "vs-leave"),
    ("client", "vw-check", SHOTS, "back"),
    ("host", "i2-safe", "off", {"settle": 0}),
    ("client", "i2-safe", "off", {"settle": 0}),
]
