"""Footage for the docs' clips (see showcase.py): the workbench's storage open in both games while each player puts
things in and takes them out, slot by slot. (A container opens for one player at a time, so the workbench is the one
place two players can watch the same storage change.) Run with CLIP_DIR=DIR tools/run-test.sh map:showcase_share, then
tools/make-clips.py DIR/workbench OUT.webp."""

from showcase import END, SETUP, STOP, rec

IN_MAPS = False

PRE_JOIN = []

STEPS = SETUP + [
    ("host", "ui-wb-open", {"settle": 1}),
    ("client", "ui-wb-open", {"settle": 1}),
    rec("workbench"),
    ("host", "wait", "1", {"settle": 0}),
    ("host", "ui-wb-set", "0", "nail", "7", {"settle": 2}),
    ("client", "ui-wb-set", "1", "wood", "12", {"settle": 2}),
    ("host", "ui-wb-set", "2", "wire", "5", {"settle": 2}),
    ("client", "ui-wb-set", "0", "-", "0", {"settle": 2}),
    ("host", "ui-wb-set", "1", "-", "0", {"settle": 3}),
    STOP,
    ("host", "ui-wb-set", "2", "-", "0"),
    ("host", "ui-wb-close"),
    ("client", "ui-wb-close"),
] + END

REQUIRE = []
