"""Map test steps for the epilogue's flat (epilog_part1c_room_dream) with the host going first: the host changes
clothes, walks into the kitchen and the living room and pets the dog, then the client does each after it; same
format as map_scenario.py. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue.Apartment.cs. The
epilogue ends the game, so these run alone (tools/run-test.sh map:epiapt2)."""

POST = [
    # The host's last step starts the epilogue: the client goes in by itself, then on to the flat; the host follows.
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    ("client", "epilogue-onward", "room", {"settle": 0}),
    ("host", "epilogue-at", "room"),
    # Each changes clothes at the wardrobe, the host first: each player's own clothes change, and each sees the
    # other's puppet in them.
    ("host", "epiapt-clothes", {"settle": 0}),
    ("client", "epiapt-clothes", {"settle": 0}),
    ("host", "epiapt-puppet-clothes"),
    ("client", "epiapt-puppet-clothes"),
    # Each walks into the kitchen and the living room after the host: each room lights up in each game.
    ("host", "epiapt-room", "kuchnia"),
    ("client", "epiapt-room", "kuchnia"),
    ("host", "epiapt-room", "dpokoj"),
    ("client", "epiapt-room", "dpokoj"),
    # Each pets the dog, the host first, and can move again afterwards.
    ("host", "epiapt-pet"),
    ("client", "epiapt-pet"),
]
