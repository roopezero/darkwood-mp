"""Finds spots for the showcase clips (not a test): the host stands at points around the test area while both games
record a second of each (CLIP_DIR/scout-DX_DZ)."""

from showcase import END, SETUP, STOP, rec

IN_MAPS = False

PRE_JOIN = []

SPOTS = [(0, 600), (0, 900), (400, 900), (-400, 900), (600, 0), (-600, 0), (0, -600), (600, 600), (-600, 600),
         (600, -600), (-600, -600), (1000, 0), (-1000, 0)]

STEPS = SETUP + [s for dx, dz in SPOTS for s in (
    ("host", "mt-stand", str(dx), str(dz), {"settle": 1}),
    rec(f"scout_{dx}_{dz}"),
    ("host", "wait", "1", {"settle": 0}),
    STOP,
)] + END

REQUIRE = []
