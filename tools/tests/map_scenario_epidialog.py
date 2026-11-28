"""Map test steps for a part of the shared epilogue ended while the other player is in a dialogue; same format as
map_scenario.py. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue*.cs. The client opens a dialogue,
the host ends the part: the client's game goes on with it, and once the client closes the dialogue its screen isn't
black (the black screens gone, the camera on, the controls back). Then the other way round. The epilogue ends the
game, so this runs alone."""

SHOTS = "/tmp/darkwoodmp-test/map-epidialog/shots"

POST = [
    ("host", "epilogue-flag", "talkingTree_burnt", {"settle": 3}),
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    # The client talks; the host's end of the part takes both games on.
    ("client", "epidialog-open", {"settle": 0}),
    ("host", "epilogue-next", {"settle": 0}),
    ("client", "epidialog-after", SHOTS, {"settle": 0}),
    # The host talks; the client's end of the part takes both games on.
    ("host", "epidialog-open", {"settle": 0}),
    ("client", "epilogue-next", {"settle": 0}),
    ("host", "epidialog-after", SHOTS, {"settle": 0}),
]
