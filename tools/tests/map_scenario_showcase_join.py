"""Footage for the docs' clips, not a test with a verdict of its own (as map_scenario_showcase_combat.py): the
host opens the Multiplayer window from the pause menu; the client goes back to the main menu and joins again from Join
Game's server browser (its History tab has the host), through the loading screen, and appears beside the host. Run
with CLIP_DIR=DIR tools/run-test.sh map:showcase_join, then tools/make-clips.py DIR/join OUT.webp."""

from showcase import END, OVERLAYS_OFF, OVERLAYS_ON, SETUP, STOP, rec

IN_MAPS = False

PRE_JOIN = []

STEPS = SETUP + [
    rec("panel"),
    ("host", "clip-panel", "6"),
    STOP,
] + END

# After the dumps: the client leaves and joins again, recorded in both games.
POST = [
    OVERLAYS_OFF,
    rec("join"),
    ("client", "clip-join", "127.0.0.1:27720", {"timeout": 240}),
    STOP,
    OVERLAYS_ON,
]

REQUIRE = []
