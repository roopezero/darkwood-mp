"""Map test steps for the first things the two-game test checked: a door, an item taken and dropped by each player,
the clock; same format as map_scenario.py, whose steps start with these. Alone: tools/run-test.sh map:core."""

SETTLE = 1.5

PRE_JOIN = [
    ("host", "toggle-door", "open"),
]

STEPS = [
    ("client", "toggle-door", "close"),
    ("client", "take-item"),
    ("client", "drop-item"),
    ("host", "drop-item", "gasoline"),  # the host's bags are full of the test kit: make room first
    ("host", "take-item"),
    ("host", "drop-item"),
    ("host", "time", "+1", {"settle": 3}),
]

REQUIRE = ["doors", "containers", "dropped", "items", "story", "time"]
