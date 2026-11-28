"""Map test steps for the epilogue's flat (epilog_part1c_room_dream) with the host going first: the host changes
clothes at the wardrobe, then the client must still get into the kitchen and the living room and change its own
clothes; the host drags the bed and breaks the floorboard under it, and the client must see both. Same format as
map_scenario.py. The steps are in src/DarkwoodMP.MapTests/World/MapScenarioEpilogue.Apartment.cs and .Flat.cs. The
epilogue ends the game, so these run alone (tools/run-test.sh map:epiapt2)."""

POST = [
    # The host's last step starts the epilogue: the client goes in by itself, then on to the flat; the host follows.
    ("host", "epilogue-start", {"settle": 0}),
    ("client", "epilogue-in"),
    ("client", "epilogue-onward", "room", {"settle": 0}),
    ("host", "epilogue-at", "room"),
    # The host changes clothes first, as the wardrobe's dialogue does it; the kitchen's wall is gone in the client's
    # game too, and the client's own wardrobe dialogue still changes its clothes.
    ("host", "epiapt-wardrobe", {"settle": 0}),
    ("client", "epiapt-wall-gone"),
    ("host", "epiapt-room", "kuchnia"),
    ("client", "epiapt-room", "kuchnia"),
    ("host", "epiapt-room", "dpokoj"),
    ("client", "epiapt-room", "dpokoj"),
    ("client", "epiapt-wardrobe", {"settle": 0}),
    ("host", "epiapt-puppet-clothes"),
    ("client", "epiapt-puppet-clothes"),
    # Each pets the dog, the host first, and can move again afterwards.
    ("host", "epiapt-pet"),
    ("client", "epiapt-pet"),
    # The bed: made draggable in both games; the host drags it, and it moves in the client's game.
    ("host", "epiapt-bed-ready"),
    ("client", "epiapt-bed-ready"),
    ("host", "drag", "Bed_epilog_01", "70", "0"),
    ("client", "epiapt-bed-moved", "70", "0"),
    # The floorboard under it: the host breaks it, and it's broken (the hole open) in the client's game.
    ("host", "epiapt-floor-ready"),
    ("client", "epiapt-floor-ready"),
    ("host", "epiapt-floor"),
    ("client", "epiapt-floor-broken"),
]
