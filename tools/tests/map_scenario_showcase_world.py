"""Footage for the docs' clips (see showcase.py). Doors: the client opens and closes a door while the host watches;
the host nails planks over a door and the client hits them. Fire: out on open ground the client throws a molotov and
the host a flare. Drag: the client drags a crate while the host watches. Before the doors and the drag, the player who
acts does something there first, so the other can walk up and watch (clip-watch). Run with CLIP_DIR=DIR
tools/run-test.sh map:showcase_world, then tools/make-clips.py DIR/SEGMENT OUT.webp."""

from showcase import END, SETUP, STOP, rec

IN_MAPS = False

PRE_JOIN = []

FREE = ("host+c1", "clip-face", "off", {"settle": 0})

STEPS = SETUP + [
    # Doors: the client goes to the door first.
    ("client", "toggle-door", "open", {"settle": 1}),
    ("host", "clip-watch", "110", {"settle": 1}),
    rec("doors"),
    ("client", "toggle-door", "close", {"settle": 1.5}),
    ("client", "toggle-door", "open", {"settle": 1.5}),
    ("client", "toggle-door", "close", {"settle": 1.5}),
    STOP,
    FREE,
    ("host", "door-barricade", "3", {"settle": 1}),
    ("client", "clip-watch", "110", {"settle": 1}),
    rec("barricade"),
    ("host", "door-barricade", "3", {"settle": 2}),
    ("client", "clip-face", "off", {"settle": 0}),
    ("client", "door-hit", "3", "40", {"settle": 2}),
    STOP,
    FREE,
    # Fire, on the open ground south of the test area (i2-throw's "fire" spot, 1300 south of the centre): the client
    # throws a molotov and the host a flare, side by side.
    ("host", "mt-stand", "-60", "-1050"),
    ("client", "mt-stand", "60", "-1050"),
    ("host+c1", "clip-face", "0", "-1300", {"settle": 1}),
    rec("fire"),
    ("host", "wait", "1", {"settle": 0}),
    ("client", "i2-throw", "molotov", "fire", {"settle": 0}),
    ("client", "clip-face", "0", "-1300", {"settle": 3}),
    ("host", "i2-throw", "flare", "fire", {"settle": 0}),
    ("host", "clip-face", "0", "-1300", {"settle": 4}),
    STOP,
    FREE,
    ("host", "heal"), ("client", "heal"),
    # Drag: the test area's movable crate, the client dragging it while the host watches.
    ("client", "drag", "crate_movable_01", "-60", "0", {"settle": 1}),
    ("host", "clip-watch", "120", {"settle": 1}),
    rec("drag"),
    ("client", "drag", "crate_movable_01", "120", "0", {"settle": 1}),
    ("client", "drag", "crate_movable_01", "-120", "60", {"settle": 1}),
    STOP,
    FREE,
] + END

REQUIRE = []
