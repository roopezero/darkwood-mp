"""Map test steps for the epilogue's flat (epilog_part1c_room_dream), gone through by the client; same format as
map_scenario.py. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue.Apartment.cs. The epilogue
ends the game, so these run alone (tools/run-test.sh map:epiapt)."""

POST = [
    # The host's last step starts the epilogue: the client goes in by itself, then on to the flat; the host follows.
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    ("client", "epilogue-onward", "room", {"settle": 0}),
    ("host", "epilogue-at", "room"),
    ("client", "epiapt-dump"),
    # Before changing clothes the body has no PetDog clip: petting the dog doesn't start, so nothing holds the client
    # still (the clip never plays, so it never ends).
    ("client", "epiapt-pet", "bare"),
    # The client changes clothes at the wardrobe: its own player's clothes change, and the host's puppet of it too.
    ("client", "epiapt-clothes", {"settle": 0}),
    ("host", "epiapt-puppet-clothes"),
    # The client walks into the kitchen and the living room: each lights up in its game.
    ("client", "epiapt-room", "kuchnia"),
    ("client", "epiapt-room", "dpokoj"),
    # The client pets the dog and can move again afterwards.
    ("client", "epiapt-pet"),
]
