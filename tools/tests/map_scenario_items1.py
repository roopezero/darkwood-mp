"""Map test steps for items used on the world; same format as map_scenario.py, and a whole scenario by itself
(MAP_SCENARIO=map_scenario_items1 tools/run-test.sh map). The steps are in src/DarkwoodMP/World/MapScenarioItems1.cs
(and "trap-place" in MapScenarioWorld.cs, which places the game's own way: Player.progressBarCompleted).

- Traps placed the way the player places them reach the other game, both ways, and go off there.
- The host decides what a client places: one out of reach is refused (it goes again and the item comes back),
  and so are forged ones (too far, a prefab no player can place, or one the player carries nothing to place).
  A trap given to the player a moment before it places it is allowed (its character goes to the host first).
- A door opened with a key: the host checks the key is in the (shared) journal; one sent without the key is
  refused, one with the key just picked up is allowed.
- The porter whistle: the host calls the porter, in both games; a call from a place where the player isn't is
  refused.
"""

SETTLE = 1.5

PRE_JOIN = [
    # Daytime, and the porter neither on the way nor at the hideout, in the save the client loads.
    ("host", "porter-ready"),
]

STEPS = [
    ("host", "heal"),
    ("client", "heal"),

    # Traps placed as the player places them, each sprung in the other game.
    ("host", "trap-place", "beartrap", "-150", "-60"),
    # 190 from the bear trap, clear of the walk to it: at -150,60 (120 from it) the walk passed close enough to
    # spring the client's own chain trap (Walk leaves alone the traps by where it goes).
    ("client", "trap-place", "chainTrap", "-300", "60", {"settle": 2}),
    ("client", "trap-fire", "beartrap@-150,-60"),
    ("client", "heal"),
    ("host", "trap-fire", "chainTrap@-300,60"),
    ("host", "heal"),

    # The host refuses what a client couldn't have placed.
    ("client", "trap-place-far", "beartrap"),
    ("client", "place-forged", "Traps/beartrap", "3000", "0"),
    ("client", "place-forged", "Characters/NPC/Porter", "0", "100"),
    # In reach, but with nothing that places it in its bags: the host checks the character the client sent first.
    ("client", "place-forged", "Traps/beartrap", "0", "100", "--none"),

    # A door locked with a key: refused without it, opened with it.
    ("host", "door-lock-key", "1"),
    ("client", "door-locked", "1", "yes"),
    ("client", "door-key", "1", "--forged"),
    ("host", "door-locked", "1", "yes"),
    ("client", "door-key", "1"),
    ("host", "door-locked", "1", "no"),

    # The porter whistle.
    ("client", "porter-forged"),
    ("client", "whistle"),
    ("host", "porter-came"),
    ("client", "porter-came"),

    ("host", "alive"),
    ("client", "alive"),
]

POST = []

REQUIRE = ["doors", "traps", "journal", "story", "time"]
