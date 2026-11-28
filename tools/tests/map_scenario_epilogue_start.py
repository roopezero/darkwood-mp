"""Map test steps for the epilogue from a new game started at it; same format as map_scenario.py. The host starts a
new game with Start Game's "Start at: Epilogue" and the Talking Tree burned (a fresh copy of your saves, chapter 1,
hosting with Direct), the client joins it, and the host's "Start the epilogue" (the Multiplayer window's button) takes
both into the epilogue, which then goes through as in map_scenario_epilogue.py. The steps are in
src/DarkwoodMP.MapTests/World/MapScenarioEpilogue*.cs."""

from map_scenario_epilogue import POST as EPILOGUE

HOST_WORLD = "new"  # tools/run-test.sh: a new game, not the test world
HOST_ARGS = "--dwmp-start-epilogue burned --dwmp-shots /tmp/darkwoodmp-test/map-epilogue_start/window"
# (tools/run-test.sh adds HOST_ARGS to the host's launch arguments: Start at: Epilogue, and screenshots of the window)
FINAL_AROUND_HOST = True  # the final dumps around the host

SHOTS = "/tmp/darkwoodmp-test/map-epilogue_start/shots"

STEPS = [
    # The game was started at the epilogue: the host's Multiplayer window offers "Start the epilogue" (a screenshot).
    ("host", "epilogue-offered", SHOTS),
    # Nothing set the Tree's flag yet: the button sets it as it starts the epilogue.
    ("host", "epilogue-tree", "no"),
    ("client", "epilogue-tree", "no"),
]

# The host presses the button instead of running the game's last step; then the client has the host's flag.
POST = [("host", "epilogue-start", "button", {"settle": 0}), ("client", "epilogue-in"), ("client", "epilogue-tree", "yes")]
POST += [s for s in EPILOGUE if s[:2] not in (("host", "epilogue-start"), ("client", "epilogue-in"))]
