"""Map test steps for the bugs reported on GitHub; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioIssues.cs."""

PRE_JOIN = [
    ("host", "hold", "pitchfork"),      # 3: the save has a weapon in the host's hands
    ("host", "issue-setup"),            # 5, 6: a second egg near the area, a third far off; two more infection patches
    ("host", "wb-fill"),                # 4: things in the workbench's storage, changed again while the client joins
]

# Run first, right after the join, before any step moves a player: later steps walk both players back to the
# test area's centre, where they stand close together.
AT_JOIN = [
    # 13: the joiner was placed off the host, not on top of them
    ("client", "join-apart"),
]

STEPS = [
    # 1: the join showed our loading screen and handed over to the game's
    ("client", "join-log"),
    # 3: holding the selected hotbar slot's item (bare hands for a new character), not the host's from the save
    ("client", "check-equip"),
    # 4: the storage is the host's, then a shift-click in and one out, nothing refused or dropped
    ("client", "wb-use"),
    ("host", "wb-host"),
    # 2 (and 3's attack): a dog the client hits, the host kills; the client's copy goes silent; its corpse searched
    ("host", "spawn-dog", {"settle": 0.5}),
    ("client", "attack-dog"),
    ("host", "dog-hurt"),
    ("host", "kill-dog", {"settle": 0.5}),
    ("client", "dead-dog"),
    ("client", "search-corpse"),
    # 5: the client breaks the station's egg, the host the near one and the far one (culled in the client's game)
    ("client", "egg-break", "station"),
    ("client", "egg-cull", "far", "on"),  # as the game culls things far off (it didn't cull this one by itself)
    ("host", "egg-break", "near"),
    ("host", "egg-break", "far", {"settle": 2}),
    ("client", "egg-check", "station,near,far", "--from-other", "near,far"),
    ("client", "egg-cull", "far", "off"),
    ("host", "egg-check", "station,near,far", "--from-other", "station"),
    # 6: the host burns the station's patch; the client clears another with a torch
    ("host", "infection-note"),
    ("client", "infection-note"),
    ("host", "burn-infection"),
    ("client", "infection-check", "station", "--from-other"),
    ("client", "swing-infection", "torch"),
    ("client", "heal"),                 # it stood beside (or on) the patch, which hurts
    ("host", "infection-check", "station,infection:client", "--from-other"),
    ("host", "flags", {"settle": 0}),
    ("client", "flags", {"settle": 0}),
]

# Dump sections that mustn't be empty (added to map_scenario.REQUIRE).
REQUIRE = ["enemies", "infection"]
