"""Map test steps for the prologue played together; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioPrologue.cs. The host starts a new game (a fresh copy of your
saves, the prologue not skipped, hosting with Direct once the intro film is over), and the client joins it in the
forest (dream_tutorial_00). Then each stage of the prologue is ended by one player, and the other's ends with it."""

HOST_WORLD = "new"     # tools/run-test.sh: a new game, not the test world
FINAL_AROUND_HOST = True  # the final dumps around the host at the end (the hideout), not where the test began

STEPS = [
    # The client loaded the host's snapshot into the forest: the same stage, dreamt together, a first play.
    ("host", "prologue-in", "dream_tutorial_00"),
    ("client", "prologue-in", "dream_tutorial_00"),
    # The client reaches the forest's end: the host checks it's the stage it's in, and everyone goes on.
    ("client", "prologue-next", {"settle": 0}),
    ("host", "prologue-in", "dream_tutorial_01"),
    ("client", "prologue-in", "dream_tutorial_01"),
    # The host reaches the prologue's end: everyone wakes at the hideout.
    ("host", "prologue-next", {"settle": 0}),
    ("host", "prologue-over"),
    ("client", "prologue-over"),
    ("host", "centre-me"),
]
