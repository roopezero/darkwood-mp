"""Map test steps for the epilogue from a new game started at it; same format as map_scenario.py. The host starts a
new game with Start Game's "Start at: Epilogue" and the Talking Tree burned (a fresh copy of your saves, hosting with
Direct); its game goes into the epilogue by itself once the world is ready. Only then does the client join: it gets
the host's save from the epilogue's start, goes into the host's part as it loads it, and the epilogue then goes
through as in map_scenario_epilogue.py. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue*.cs."""

from map_scenario_epilogue import POST as EPILOGUE

HOST_WORLD = "new"  # tools/run-test.sh: a new game, not the test world
HOST_ARGS = "--dwmp-start-epilogue burned"
# (tools/run-test.sh adds HOST_ARGS to the host's launch arguments: Start at: Epilogue)
FINAL_AROUND_HOST = True  # the final dumps around the host
REQUIRE = ["story"]  # the dreams' places differ from the world's: the story must match

PRE_JOIN = [
    # Before anyone joins: the host's game went into the epilogue by itself, with the Tree's flag set.
    ("host", "epilogue-started"),
    ("host", "epilogue-tree", "yes"),
]

STEPS = [
    # The client, let in while the host plays the epilogue, goes into its first part, with the host's flag.
    ("client", "epilogue-in"),
    ("client", "epilogue-tree", "yes"),
    # Both games built the same first part.
    ("host", "epilogue-layout", {"settle": 0}),
    ("client", "epilogue-layout", "{host:1}", "{host:2}"),
]

# Then the rest of the epilogue as usual.
DONE = {("host", "epilogue-flag"), ("host", "epilogue-start"), ("client", "epilogue-in"), ("host", "epilogue-layout"),
        ("client", "epilogue-layout")}
POST = [s for s in EPILOGUE if s[:2] not in DONE]
