"""Map test steps for the epilogue's flat (epilog_part1c_room_dream), gone through by the client first, in the order
players reported trouble in: the client changes clothes at the wardrobe before the host, then the host must still get
into the kitchen and the living room and change its own clothes; the client drags the bed and breaks the floorboard
under it, and the host must see both. Same format as map_scenario.py. The steps are in
src/DarkwoodMP.MapTests/World/MapScenarioEpilogue.Apartment.cs and .Flat.cs. The epilogue ends the game, so these run
alone (tools/run-test.sh map:epiapt)."""

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
    # The client changes clothes first, as the wardrobe's dialogue does it (the outcome runs only while the dialogue
    # isn't shown yet). The event's shared steps reach the host: the kitchen's wall is gone there too.
    ("client", "epiapt-wardrobe", {"settle": 0}),
    ("host", "epiapt-wall-gone"),
    ("host", "epiapt-room", "kuchnia"),
    ("host", "epiapt-room", "dpokoj"),
    # The host's own wardrobe dialogue isn't shown yet in its game: its clothes change too.
    ("host", "epiapt-wardrobe", {"settle": 0}),
    ("host", "epiapt-puppet-clothes"),
    ("client", "epiapt-puppet-clothes"),
    # The client walks into the kitchen and the living room: each lights up in its game.
    ("client", "epiapt-room", "kuchnia"),
    ("client", "epiapt-room", "dpokoj"),
    # Each pets the dog and can move again afterwards.
    ("client", "epiapt-pet"),
    ("host", "epiapt-pet"),
    # The bed: made draggable in both games; the client drags it, and it moves in the host's game.
    ("client", "epiapt-bed-ready"),
    ("host", "epiapt-bed-ready"),
    ("client", "drag", "Bed_epilog_01", "70", "0"),
    ("host", "epiapt-bed-moved", "70", "0"),
    # The floorboard under it: the client breaks it, and it's broken (the hole open) in the host's game.
    ("client", "epiapt-floor-ready"),
    ("host", "epiapt-floor-ready"),
    ("client", "epiapt-floor"),
    ("host", "epiapt-floor-broken"),
]
