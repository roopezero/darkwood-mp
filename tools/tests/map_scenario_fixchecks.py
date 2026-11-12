"""Map test steps for fixes in docs/INTERACTIONS.md that had no test in game; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioFixChecks.cs. Each does a thing in one game and checks it
there; a step in the other game checks what reached it."""

PRE_JOIN = []

STEPS = [
    # 44, 45: the client hits the host's dog: its health bar shows there and follows the host's health; the host's
    # bar doesn't show it; the copy's shadow lies as the game places it.
    ("host", "fx-spawn", "predeath"),
    ("client", "fx-hit", "15"),
    ("host", "fx-hurt"),
    # 43: the host brings it down to a pre-death stage; the client's copy stays in the fight, the client finishes
    # it; then 45: the copy's corpse with its shadow faded and its particles stopped.
    ("host", "fx-predeath"),
    ("client", "fx-crawling"),
    ("host", "fx-dead"),
    ("client", "fx-corpse"),
    ("host", "heal"),
    ("client", "heal"),
    # 28: an InteractiveItem's switch, both ways: the switcher's events run in its game, and a client's in the
    # host's too; the other game only sets it.
    ("host", "ii-setup"),
    ("client", "ii-setup"),
    ("host", "ii-switch", "on"),
    ("client", "ii-check", "on", "--not-fired"),
    ("client", "ii-switch", "off"),
    ("host", "ii-check", "off", "--fired"),
    # 40: a held flare and a held match dropped (Player.dropItem), each lying dropped in the other game.
    ("client", "drop-held", "flare"),
    ("host", "expect-dropped", "flare"),
    ("host", "drop-held", "matchstick"),
    ("client", "expect-dropped", "matchstick"),
    ("host", "heal"),
    ("client", "heal"),
    # 58: slow motion leaves time at its rate in co-op, in both games.
    ("host", "slowmo"),
    ("client", "slowmo"),
    # 50: a dog by the client (and far from the host) is near a player; one far from both isn't.
    ("client", "fx-goto", "client"),
    ("host", "near-joiner"),
    # 51: a forest spirit after the client leaves when the client goes inside (the host outside), and stays when
    # only the host is inside. (The centre is in the hideout: "drop" is outside.)
    ("host", "fx-goto", "drop"),
    ("client", "fx-inside"),
    ("host", "spirit", "leaves"),
    ("client", "fx-goto", "drop"),
    ("host", "fx-inside"),
    ("host", "spirit", "stays"),
    ("host", "fx-goto", "centre"),
    ("client", "fx-goto", "centre"),
    # 24: the client kills the night trader: its dream is the client's alone. (Last: the trader is gone after.)
    ("client", "kill-trader", {"settle": 3}),
    ("host", "trader-dream"),
    ("host", "heal"),
]

# Dump sections that mustn't be empty (added to map_scenario.REQUIRE).
REQUIRE = ["enemylooks"]
