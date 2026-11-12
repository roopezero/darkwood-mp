"""Map test steps for leftover sync bugs; same format as map_scenario.py, and a whole scenario by itself
(MAP_SCENARIO=map_scenario_sweep tools/run-test.sh map). The steps are in src/DarkwoodMP/World/MapScenarioSweep.cs.

- A story flag the host sets while flag sharing is paused (inputs forbidden, as on the death screen) reaches the
  client once sharing resumes (it used to be swallowed: wolf_killed and player_diedAtLeastOneTime as the host died).
- A host's enemy whose loop stops without playIdleLoop("") (destroySounds) stops it in the client's copy too.
- An open door's leaf a player turns by physics (as walking into it) rests at the same angle in the other game, a
  client's push through the host (DoorPush), and the host's own.
"""

SETTLE = 1.5

PRE_JOIN = []

STEPS = [
    ("host", "heal"),
    ("client", "heal"),

    # Flags while sharing is paused.
    ("host", "sweep-flag-paused", "0"),
    ("both", "compare", "client", "story"),

    # An enemy's loop stopped by destroySounds.
    ("host", "spawn", "Dog", {"settle": 3}),
    ("host", "sweep-loop-stop"),
    ("both", "compare", "client", "enemies"),
    ("host", "kill-enemy"),
    ("host", "heal"),
    ("client", "heal"),

    # A door leaf pushed by the client, then by the host.
    ("client", "sweep-push-door", "40"),
    ("host", "sweep-door-leaf", "40"),
    ("host", "sweep-push-door", "75"),
    ("client", "sweep-door-leaf", "75"),
    ("both", "compare", "client", "doors"),
]

REQUIRE = ["doors", "story", "enemies"]

POST = []
