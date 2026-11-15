"""Map test steps for what each player sees at the Dry Meadow's "Markings" (gridObj_signs_2_01: painted signs, an
area whose enter fades in spot_black and TINT, sprites that cover the whole screen, and whose exit fades them out);
same format as map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioView.cs. Run with
tools/run-test.sh map:view (map4_view.py is the same with four games).

A repeatable area's colour fades, particles and sounds were shared steps: one player walking in blacked out the other
player's screen wherever they were, and it stayed black until the last player inside walked out (or came back when a
player stepped over the edge again). Both players walk up to it at running speed, together and one after the other,
and in and out while the other is inside or far away. Every game watches its own view light each frame on the way
(black, a cone not worked out where its player stands, colliders cutting the view that shouldn't be there), and after
each move each game's darkness must match where its own player is: dark inside, clear outside."""

from mapsteps import SAFE_ON, SAFE_OFF

IN_MAPS = True
SETTLE = 1.0
SHOTS = "/tmp/darkwoodmp-test/map-view/shots"
SPRITES = ("spot_black", "TINT")
LEAD = "1500"

PRE_JOIN = []


def look(who, tag, want):
    return [(who, "vw-color", want, *SPRITES), (who, "vw-check", SHOTS, tag)]


STEPS = SAFE_ON + [
    ("host", "vw-tree", "gridObj_signs", "0"),
    ("host", "vw-mark", "gridObj_signs", "0"),
    ("client", "vw-mark", "gridObj_signs", "0"),
    # Together from 1500 out, at running speed.
    ("host", "vw-stage", LEAD + ";host=0/0;c1=0/80"),
    ("client", "vw-stage", LEAD + ";host=0/0;c1=0/80"),
    ("host+c1", "vw-approach", LEAD),
    *look("host", "together-in", "dark"),
    *look("client", "together-in", "dark"),
    # The client leaves while the host stays inside.
    ("client", "vw-stage", LEAD + ";c1=0/80"),
    *look("client", "client-out", "clear"),
    *look("host", "client-out", "dark"),
    # The host steps out over the edge and back in: the client, outside, stays clear.
    ("host", "vw-stage", "700;host=0/0"),
    ("host", "vw-approach", "700"),
    *look("client", "host-back-in", "clear"),
    *look("host", "host-back-in", "dark"),
    ("host", "vw-stage", LEAD + ";host=0/0"),
    *look("host", "both-out", "clear"),
    *look("client", "both-out", "clear"),
    # The client alone, the host 1500 out.
    ("client", "vw-approach", LEAD),
    *look("client", "client-in", "dark"),
    *look("host", "client-in", "clear"),
    # In and out at the area's edge, then out.
    ("client", "vw-stage", "500;c1=0/80"),
    ("client", "vw-pace", "500", "4"),
    ("client", "vw-stage", LEAD + ";c1=0/80"),
    *look("client", "after-pace", "clear"),
    *look("host", "after-pace", "clear"),
] + SAFE_OFF

POST = []
REQUIRE = []
