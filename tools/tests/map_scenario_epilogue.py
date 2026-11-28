"""Map test steps for the epilogue played together; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioEpilogue.cs. The epilogue ends the game (the credits, the main menu), so these run
alone (MAP_SCENARIO=map_scenario_epilogue), after the dumps."""

import os

PART1 = "epilog_part1a_dream"
# EPILOGUE_TREE=0: the host's world hasn't burned the talking tree (chapter 2's choice) before the epilogue.
TREE = os.environ.get("EPILOGUE_TREE", "1") != "0"
# EPILOGUE_END=sleep: the ending without the fire (back to the room, to sleep); drop: the host goes away in the
# crater; else the burned forest.
END = os.environ.get("EPILOGUE_END", "burn")

POST = [("host", "epilogue-flag", "talkingTree_burnt", {"settle": 3})] if TREE else []
POST += [
    # The host's last step starts the epilogue: the session stays up and the client goes in by itself.
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    # Both games built the same first part: the same solid things in the same places (world and in the location).
    ("host", "epilogue-layout", {"settle": 0}),
    ("client", "epilogue-layout", "{host:1}", "{host:2}"),
    # The other players' name tags are drawn in the epilogue (the game hides its HUD there).
    ("host", "epilogue-tags"),
    ("client", "epilogue-tags"),
    # The client's end of the first part (as its endDream step does) takes both games on to the next part.
    ("client", "epilogue-next", {"settle": 0}),
    ("host", "epilogue-moved", PART1),
    # On through the parts to the crater (the client ends each; the host follows).
    ("client", "epilogue-onward", "crater", {"settle": 0}),
    ("host", "epilogue-at", "crater"),
]
if END == "drop":
    # The host's server goes away in the crater with no last word: the client leaves the host's world for a main menu
    # that works (no credits).
    POST += [
        ("host", "epilogue-drop", {"settle": 0}),
        ("client", "epilogue-menu-works", "direct", {"timeout": 220}),
    ]
elif END == "sleep":
    # The other ending: back to the room (the client's end of the crater takes both), where the host's outcomes screen
    # comes on (as at the end of its sleep) and ends in the credits by itself (the game's goToCredits). The client, in
    # the room, follows to the credits and gets back to a main menu that works.
    POST += [
        ("client", "epilogue-onward", "returnFromCrater", {"settle": 0}),
        ("host", "epilogue-at", "returnFromCrater"),
        ("host", "epilogue-outcomes", "start", {"settle": 0, "timeout": 320}),
        ("client", "epilogue-menu-works", {"timeout": 220}),
    ]
else:
    # The host sets the Being on fire: the burned-forest ending, in the client's crater too. Both crawl to their
    # death; the pan over the burning forest waits for the client's, then runs in both games, and the client has the
    # host's outcomes (burned). The host's outcomes end in the credits by themselves (the game's goToCredits): the
    # client's game follows, leaves the session and reaches a main menu that works.
    POST += [
        ("host", "epilogue-burn", {"settle": 0}),
        ("client", "epilogue-burned"),
        ("host", "epilogue-crawl-die", {"settle": 0}),
        ("host", "epilogue-held", {"settle": 0}),
        ("client", "epilogue-crawl-die", {"settle": 0}),
        ("host", "epilogue-pan", {"settle": 0}),
        ("client", "epilogue-pan", {"settle": 0}),
        ("host", "epilogue-outcomes", {"settle": 0, "timeout": 320}),
        ("client", "epilogue-menu-works", {"timeout": 220}),
    ]
