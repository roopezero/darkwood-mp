"""The `map4` view scenario (MAP_SCENARIO=map4_view tools/run-test.sh map4): the host and three client games.

1. The Dry Meadow's "Markings" (see map_scenario_view.py): the host and c1 walk up to it together at running speed,
   c2 1500 behind and c3 6000 behind; each game must be dark only while its own player is inside, whoever else is.
2. The lights players carry, between every pair of games: c1's torch, c2's flashlight and c3's lantern must show
   on their puppets in every other game (the host relays the clients' states to each other).
3. All four at the Old Woods hideout (big_hideout_03) with lanterns lit: every game's view light and light dot must
   still be its own, worked out where its own player stands (a joiner once saw the host's view there).
Every step that moves a player watches that game's view each frame (black, a mesh not drawn here, ghost colliders);
vw-check screenshots go to $WORK/map-view/shots."""

SETTLE = 1.0
SHOTS = "/tmp/darkwoodmp-test/map-view/shots4"
SPRITES = ("spot_black", "TINT")
GAMES = ["host", "c1", "c2", "c3"]
ALL = "+".join(GAMES)
SPEC = "1500;host=0/0;c1=0/80;c2=1500/40;c3=6000/-40"

PRE_JOIN = []


def each(*step):
    return [(g, *step) for g in GAMES]


def look(who, tag, want):
    return [(who, "vw-color", want, *SPRITES), (who, "vw-check", SHOTS, tag)]


def others(who):
    return [g for g in GAMES if g != who]


def carried(who, hold, shows):
    """WHO takes HOLD out (or a lantern on), every other game sees it on WHO's puppet; then it's put away."""
    on = (who, "i2-lantern", "on") if hold == "lantern" else (who, "lt-hold", hold)
    off = (who, "i2-lantern", "off") if hold == "lantern" else (who, "lt-hold", "fists")
    return [on, *[(g, "lt-check", shows) for g in others(who)], off, *[(g, "lt-check", "none") for g in others(who)]]


STEPS = [
    *each("i2-safe", "on", {"settle": 0}),
    *each("vw-mark", "gridObj_signs", "0", {"settle": 0}),
    # 1. The Markings.
    *[(g, "vw-stage", SPEC) for g in GAMES],
    (ALL, "vw-approach", "1500"),
    *look("host", "m-front-in", "dark"), *look("c1", "m-front-in", "dark"),
    *look("c2", "m-front-in", "clear"), *look("c3", "m-front-in", "clear"),
    # The ones behind catch up (c2 has 1500 to go, c3 6000).
    ("c2+c3", "vw-approach", "6000", {"timeout": 400}),
    *look("c2", "m-all-in", "dark"), *look("c3", "m-all-in", "dark"),
    *look("host", "m-all-in", "dark"), *look("c1", "m-all-in", "dark"),
    # The front two leave, the others stay; then those leave too.
    ("host", "vw-stage", "1500;host=0/0"), ("c1", "vw-stage", "1500;c1=0/80"),
    *look("host", "m-front-out", "clear"), *look("c1", "m-front-out", "clear"),
    *look("c2", "m-front-out", "dark"), *look("c3", "m-front-out", "dark"),
    ("c2", "vw-stage", "1500;c2=0/40"), ("c3", "vw-stage", "1500;c3=0/-40"),
    *[s for g in GAMES for s in look(g, "m-all-out", "clear")],
    # 2. Lights between every pair.
    *[(g, "lt-check", "none") for g in GAMES],
    *carried("c1", "torch", "torch"),
    *carried("c2", "flashlight", "flashlight"),
    *carried("c3", "lantern", "lantern"),
    *carried("host", "lantern", "lantern"),
    # 3. All four at the Old Woods hideout, lanterns lit.
    *each("vw-mark", "big_hideout_03", "0", {"settle": 0}),
    *[(g, "vw-stage", "200;host=0/0;c1=0/120;c2=120/0;c3=120/120") for g in GAMES],
    *each("i2-lantern", "on"),
    *each("lt-check", "lantern"),
    *[(g, "vw-check", SHOTS, "oldwoods") for g in GAMES],
    *each("i2-lantern", "off"),
    *each("i2-safe", "off", {"settle": 0}),
    ("host", "alive"),
    ("all-clients", "alive"),
]

POST = []
REQUIRE = []
