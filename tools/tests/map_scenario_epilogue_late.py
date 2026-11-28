"""Map test steps for joining the epilogue late, in a later part; same format as map_scenario.py. The host starts a
new game at the epilogue (Start Game's "Start at: Epilogue", the Talking Tree standing), its game goes into the
epilogue by itself, and the host plays on alone to the flat (epilog_part1c_room_dream). Only then does the client
join: it must land in the flat, not in the first part, and the rest is played together: the flat, the crater and the
ending without the fire. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue*.cs (tools/run-test.sh
map:epilogue_late)."""

HOST_WORLD = "new"  # tools/run-test.sh: a new game, not the test world
HOST_ARGS = "--dwmp-start-epilogue standing"
FINAL_AROUND_HOST = True  # the final dumps around the host
REQUIRE = ["story"]  # the dreams' places differ from the world's: the story must match

PRE_JOIN = [
    ("host", "epilogue-started"),
    ("host", "epilogue-tree", "no"),
    # The host ends the parts itself, on to the flat, before anyone joins.
    ("host", "epilogue-onward", "room", {"settle": 0}),
]

STEPS = [
    # The client, let in while the host is in the flat, goes into the flat.
    ("client", "epilogue-in", "room"),
    ("host", "epilogue-at", "room"),
    ("client", "epilogue-tree", "no"),
]

POST = [
    # The flat together, as in map_scenario_epiapt2.py: each changes clothes, walks into the kitchen and the living room
    # after the host, and pets the dog.
    ("host", "epiapt-clothes", {"settle": 0}),
    ("client", "epiapt-clothes", {"settle": 0}),
    ("host", "epiapt-puppet-clothes"),
    ("client", "epiapt-puppet-clothes"),
    ("host", "epiapt-room", "kuchnia"),
    ("client", "epiapt-room", "kuchnia"),
    ("host", "epiapt-room", "dpokoj"),
    ("client", "epiapt-room", "dpokoj"),
    ("host", "epiapt-pet"),
    ("client", "epiapt-pet"),
    # On to the crater (the client ends each part; the host follows), then the ending without the fire: back to the
    # room, where the host's outcomes end in the credits, which the client follows to a main menu that works.
    ("client", "epilogue-onward", "crater", {"settle": 0}),
    ("host", "epilogue-at", "crater"),
    ("client", "epilogue-onward", "returnFromCrater", {"settle": 0}),
    ("host", "epilogue-at", "returnFromCrater"),
    ("host", "epilogue-outcomes", "start", {"settle": 0, "timeout": 320}),
    ("client", "epilogue-menu-works", {"timeout": 220}),
]
