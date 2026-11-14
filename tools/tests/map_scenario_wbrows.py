"""Map test steps for the workbench storage's rows (World/WorkbenchStorage.cs); same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioWbRows.cs (and MapScenarioUi.cs for the window).
Run alone: tools/run-test.sh map:wbrows.

Each workbench level adds a row of six slots to its storage. The host upgrades it at the open workbench; both
games' storage grows by a row at once and their open windows draw it; the client puts something in the new row's
last slot; the host saves and loads its world again (the client follows) and the item is still there. Then the
host upgrades it to the last level: every row must still be drawn on the screen in both games.
"""
import os

SHOTS = os.path.join(os.environ.get("WORK", "/tmp/darkwoodmp-test"), "shots")

STEPS = [
    # The storage has the level's slot count in both games as the world loads.
    ("host", "wb-rows-check"),
    ("client", "wb-rows-check"),
    ("host", "ui-wb-open"),
    ("client", "ui-wb-open"),
    # The host upgrades it: a row more here at once, and in the client's open window.
    ("host", "wb-upgrade"),
    ("host", "wb-rows-check", "--grown", "6", "--shot", SHOTS),
    ("client", "wb-rows-check", "--grown", "6", "--shot", SHOTS),
    # The client puts nails into the new row's last slot; the host takes it.
    ("client", "ui-wb-set", "-1", "nail", "4"),
    ("host", "ui-wb-check", "-1", "nail", "4"),
    ("client", "ui-wb-close"),
    # Saved and loaded again: the rows and the nails are kept, in both games.
    ("host", "wb-rows-reload", "-1", "nail", "4"),
    ("client", "wb-rows-check", "--after-reload", "--slot", "-1", "nail", "4"),
    ("host", "wb-rows-check", "--slot", "-1", "nail", "4"),
    # Up to the last level: every row is drawn on the screen (the window shrinks to fit), in both games.
    ("host", "ui-wb-open"),
    ("client", "ui-wb-open"),
    ("host", "wb-upgrade", "max"),
    ("host", "wb-rows-check", "--shot", SHOTS),
    ("client", "wb-rows-check", "--shot", SHOTS),
    ("client", "ui-wb-set", "-1", "nail", "2"),
    ("host", "ui-wb-check", "-1", "nail", "2"),
    ("client", "ui-wb-close"),
    ("host", "ui-wb-close"),
]
