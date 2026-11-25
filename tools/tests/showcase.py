"""What the showcase scenarios (map_scenario_showcase_*.py) share; not a battery itself. They make footage for the
docs' clips and have no verdict of their own: both games record their screens at once (rec-start and rec-stop,
src/DarkwoodMP.MapTests/World/MapScenarioClips.cs) into CLIP_DIR/SEGMENT/host and /c1, which tools/make-clips.py
turns into a side-by-side WebP. The test world's labels and the test's title are hidden while they run."""

import os

from mapsteps import SAFE_ON, SAFE_OFF

CLIP = os.environ.get("CLIP_DIR", "/tmp/darkwoodmp-test/clips")
FPS = "12"
WIDTH = "960"


def rec(name):
    """Both games start recording into CLIP/name."""
    return ("host+c1", "rec-start", f"{CLIP}/{name}", FPS, WIDTH, {"settle": 0})


STOP = ("host+c1", "rec-stop", {"settle": 0})
OVERLAYS_OFF = ("host+c1", "clip-overlays", "off", {"settle": 0})
OVERLAYS_ON = ("host+c1", "clip-overlays", "on", {"settle": 0})

# Daytime, both players unhurtable and healed, a lit lantern on each one's hotbar (indoors is dark even at noon),
# side by side at the test area's centre.
SETUP = SAFE_ON + [
    ("host", "ee-day"),
    ("host", "heal"), ("client", "heal"),
    ("host", "i2-lantern", "on"), ("client", "i2-lantern", "on"),
    OVERLAYS_OFF,
    ("host", "mt-stand", "-60", "0"),
    ("client", "mt-stand", "60", "0", {"settle": 2}),
]

END = [
    OVERLAYS_ON,
    ("host+c1", "clip-face", "off", {"settle": 0}),
    ("host", "i2-lantern", "off"), ("client", "i2-lantern", "off"),
    ("host", "ee-day", "off"),
] + SAFE_OFF
