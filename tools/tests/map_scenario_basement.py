"""Map test steps for the village cellar's dream in chapter 2; same format as map_scenario.py. The steps are in
src/DarkwoodMP.MapTests/World/MapScenarioBasement*.cs.

The cellar's first dive (its trigger asks for a full oxygen tank) starts dream_village_cellar, a story step of the
diver's own. Every player must go into the same dream, the host's (DreamSync.Shared).

B1: the client dives: it goes in at once and asks the host, which shares the dream and follows. Inside, the dream's
doors and triggers are listed (basement-scan; the same order in both games). The client opens a door and the host
sees it open; the host opens another and the client sees it; the host sets off a once-only area trigger (the dream's
"event_dream_village_cellar") and it fires in the client's game too; the client sets one off (the first room's
"area_exit", which closes doors behind; asked of the host) and it fires in the host's. The client's end (outcome "hole") takes both out.

B2: the host starts the dream (the dive's own step, as the host's dive runs it): the client follows, and the host's
end takes both out.
"""

PRE_JOIN = []
IN_MAPS = False

CELLAR = "outside_village_ch2_cellar_01"
DIVE = "startDream_outside_village_ch2_cellar_01"

# doors: indexes in basement-scan's list of active doors (by name, then by spot in the dream); triggers: NAME#K, the K-th of that name
DOOR_A, DOOR_B = "0", "1"
HOST_TRIGGER = "event_dream_village_cellar"  # area, once: shows and hides things, a message
CLIENT_TRIGGER = "area_exit#0"  # area, once: closes the first room's doors behind (modifyDoor)

STEPS = [
    ("client", "basement-presets"),
    # B1
    ("client", "loc-go", CELLAR, {"settle": 3}),
    ("client", "basement-tank"),
    ("client", "chapter-fire", DIVE, {"settle": 0}),
    ("client", "basement-in"),
    ("host", "basement-in"),
    ("host", "basement-scan"),
    ("client", "basement-scan"),
    ("client", "basement-door", DOOR_A),
    ("host", "basement-door-is", DOOR_A, "open"),
    ("host", "basement-door", DOOR_B),
    ("client", "basement-door-is", DOOR_B, "open"),
    ("host", "basement-fire", HOST_TRIGGER),
    ("client", "basement-fired", HOST_TRIGGER),
    ("client", "basement-fire", CLIENT_TRIGGER),
    ("host", "basement-fired", CLIENT_TRIGGER),
    ("client", "basement-end", "hole"),
    ("host", "basement-out"),
    ("client", "loc-back"),
    # B2
    ("host", "basement-start"),
    ("host", "basement-in"),
    ("client", "basement-in"),
    ("host", "basement-end", "hole"),
    ("client", "basement-out"),
]
REQUIRE = []
