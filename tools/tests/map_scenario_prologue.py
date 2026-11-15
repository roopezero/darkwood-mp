"""Map test steps for the prologue played together; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioPrologue.cs. The host starts a new game (a fresh copy of your
saves, the prologue not skipped, hosting with Direct once the intro film is over), and the client joins it in the
forest (dream_tutorial_00). Then each stage of the prologue is ended by one player, and the other's ends with it."""

HOST_WORLD = "new"     # tools/run-test.sh: a new game, not the test world
FINAL_AROUND_HOST = True  # the final dumps around the host at the end (the hideout), not where the test began

KEY = "key_dream_tutorial"  # the key to the house door at the forest's start (dream_tutorial_00), in its wardrobe

STEPS = [
    # The client loaded the host's snapshot into the forest: the same stage, dreamt together, a first play.
    ("host", "prologue-in", "dream_tutorial_00"),
    ("client", "prologue-in", "dream_tutorial_00"),
    # The key in the wardrobe and the door it opens (a dream's key is marked inDream; it was once never shared,
    # so the host refused the joiner's unlock and nobody could get through). First the host takes it, and the
    # client unlocks the door with it; the host opens it again too.
    ("host", "prologue-key-look"),
    ("host", "prologue-key-take"),
    ("client", "prologue-key-has", KEY, "yes"),
    ("client", "prologue-key-door", KEY),
    ("host", "prologue-key-door-is", KEY, "open"),
    ("host", "prologue-key-door", KEY, "--reopen"),
    ("client", "prologue-key-door-is", KEY, "open"),
    # Back as it was (the key's removal sent as "-" in the shared dream): then the client takes it and unlocks.
    ("host", "prologue-key-reset", KEY),
    ("client", "prologue-key-has", KEY, "no"),
    ("client", "prologue-key-door-is", KEY, "locked"),
    ("client", "prologue-key-take"),
    ("host", "prologue-key-has", KEY, "yes"),
    ("client", "prologue-key-door", KEY),
    ("host", "prologue-key-door-is", KEY, "open"),
    ("host", "prologue-key-door", KEY, "--reopen"),
    # The client reaches the forest's end: the host checks it's the stage it's in, and everyone goes on.
    ("client", "prologue-next", {"settle": 0}),
    ("host", "prologue-in", "dream_tutorial_01"),
    ("client", "prologue-in", "dream_tutorial_01"),
    # The forest's key is still in both journals: the game drops what was found in a dream only when the
    # dreaming ends (Dreams.endDreaming), not when one stage leads into the next (wantToSwitchDream).
    ("host", "prologue-key-has", KEY, "yes"),
    ("client", "prologue-key-has", KEY, "yes"),
    # The host reaches the prologue's end: everyone wakes at the hideout.
    ("host", "prologue-next", {"settle": 0}),
    ("host", "prologue-over"),
    ("client", "prologue-over"),
    # The dream's key went with the dream, in both games (each game drops its own; the drop isn't sent).
    ("host", "prologue-key-has", KEY, "no"),
    ("client", "prologue-key-has", KEY, "no"),
    ("host", "centre-me"),
]
