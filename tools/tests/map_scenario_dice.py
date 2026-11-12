"""Map test steps for the determinism audit (docs/MULTIPLAYER_DETERMINISM_AUDIT.md); same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioDice.cs. Each game's reply is logged by the driver: the two
dice-sets digests must be the same, or a location kept a different set of its children in the two games."""

STEPS = [
    # Which of its sets each location kept as it was made (Location.Awake rolls it in every game).
    ("host+client", "dice-sets"),
    # The scripted-event steps every game runs whose target is picked at random or nearest the own player.
    ("host", "dice-events"),
    ("client", "dice-events"),
]
