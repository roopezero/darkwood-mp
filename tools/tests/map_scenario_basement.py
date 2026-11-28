"""Map test steps for the village cellar's dream in chapter 2; same format as map_scenario.py. The steps are in
src/DarkwoodMP.MapTests/World/MapScenarioBasement*.cs.

The client dives in the cellar (its trigger asks for a full oxygen tank and starts dream_village_cellar, a story step
of the diver's own). Every player must go into the same dream, the host's (DreamSync.Shared): the client asks the host,
the host shares it and follows. Inside, the dream's doors and triggers are listed (basement-scan), the client opens a
door that the host sees opened, the host sets off a trigger that fires in the client's game too, and the client's
dream end takes everyone out.
"""

PRE_JOIN = []
IN_MAPS = False

CELLAR = "outside_village_ch2_cellar_01"
DIVE = "startDream_outside_village_ch2_cellar_01"

STEPS = [
    ("client", "basement-presets"),
    ("client", "loc-go", CELLAR, {"settle": 3}),
    ("client", "basement-tank"),
    ("client", "chapter-fire", DIVE, {"settle": 0}),
    ("client", "basement-in"),
    ("host", "basement-in"),
    ("host", "basement-scan"),
    ("client", "basement-scan"),
    ("client", "basement-end", "hole"),
    ("host", "basement-out"),
]
REQUIRE = []
