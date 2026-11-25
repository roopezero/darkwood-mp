"""Footage for the docs' clips (see showcase.py): at the enemies' fighting spot a dog comes for the client, bites it
and is beaten off while the host stands by and watches. Run with CLIP_DIR=DIR tools/run-test.sh map:showcase_combat,
then tools/make-clips.py DIR/combat OUT.webp."""

from showcase import END, SETUP, STOP, rec

IN_MAPS = False

PRE_JOIN = []

STEPS = SETUP + [
    ("client", "ee-arena"),
    ("host", "mt-stand", "-150", "1350"),
    ("host", "clip-face", "0", "1500"),
    ("client", "clip-face", "-150", "1350", {"settle": 1}),
    rec("combat"),
    ("host", "wait", "1.5", {"settle": 0}),
    ("client", "clip-face", "off", {"settle": 0}),
    ("host", "ee-spawn", "Dog", {"settle": 0.5}),
    ("client", "ee-fight", "Dog", "--hurts", "--moves", {"settle": 2}),
    STOP,
    ("host", "ee-clear"),
] + END

REQUIRE = []
