"""Map test steps for the epilogue played together; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioEpilogue.cs. The epilogue ends the game (the credits, the main menu), so these run
alone (MAP_SCENARIO=map_scenario_epilogue), after the dumps."""

PART1 = "epilog_part1a_dream"

POST = [
    # The host's last step starts the epilogue: the session stays up and the client goes in by itself.
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    # The client's end of the first part (as its endDream step does) takes both games on to the next part.
    ("client", "epilogue-next", {"settle": 0}),
    ("host", "epilogue-moved", PART1),
    # On through the parts to the crater (the client ends each; the host follows), where the host sets the Being on
    # fire: the burned-forest ending, in the client's crater too.
    ("client", "epilogue-onward", "crater", {"settle": 0}),
    ("host", "epilogue-at", "crater"),
    ("host", "epilogue-burn", {"settle": 0}),
    ("client", "epilogue-burned"),
    # Both crawl to their death; the pan over the burning forest waits for the client's, then runs in both games,
    # and the client has the host's outcomes (burned).
    ("host", "epilogue-crawl-die", {"settle": 0}),
    ("host", "epilogue-held", {"settle": 0}),
    ("client", "epilogue-crawl-die", {"settle": 0}),
    ("host", "epilogue-pan", {"settle": 0}),
    ("client", "epilogue-pan", {"settle": 0}),
    # The host's game goes to the credits: the client's follows, leaves the session and reaches the main menu.
    ("host", "epilogue-credits", {"settle": 0}),
    ("client", "epilogue-menu"),
]
