"""Map test steps for a part of the shared epilogue ended while the other player is in a dialogue; same format as
map_scenario.py. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue*.cs. In the flat (the third
part) the host talks to the wardrobe and the client ends the part: the host's game goes on to the crater with it, and
once the host closes the dialogue its screen isn't black (the black screens gone, the camera on, the controls back).
Then in the crater the other way round. The epilogue ends the game, so this runs alone."""

SHOTS = "/tmp/darkwoodmp-test/map-epidialog/shots"
FLAT = "epilog_part1c_room_dream"

POST = [
    ("host", "epilogue-flag", "talkingTree_burnt", {"settle": 3}),
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    ("client", "epilogue-onward", FLAT, {"settle": 0}),
    ("host", "epilogue-at", FLAT),
    # The host talks; the client's end of the part takes both games on.
    ("host", "epidialog-open", {"settle": 0}),
    ("client", "epilogue-onward", "crater", {"settle": 0}),
    ("host", "epidialog-after", SHOTS, {"settle": 0}),
    # The client talks; the host's end of the part takes both games on.
    ("client", "epidialog-open", {"settle": 0}),
    ("host", "epilogue-onward", "returnFromCrater", {"settle": 0}),
    ("client", "epidialog-after", SHOTS, {"settle": 0}),
]
