"""Map test steps for players far apart; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioFar.cs. Spots are offsets from the test area's centre ("off:DX,DZ").

Run on their own with MAP_SCENARIO=map_scenario_far tools/run-test.sh map."""

SETTLE = 1.5

# A dog 2400 north of the centre, in the save the client loads: the client at the centre has its node off.
PRE_JOIN = [
    ("host", "far-dog", "off:0,2400"),
]

STEPS = [
    # The same dog put back there (or a new one, if it died): in the full map test it walks off before these steps.
    ("host", "far-dog", "off:0,2400", {"settle": 3}),
    # The host goes to the dog: it's sent to the client (2400 away, within 3000), whose copy was switched off by
    # its own culling. It must be switched on, and shown beside the client once the host's dog comes there.
    # The client walks back to the centre first: earlier steps of the full map test (attack-dog, ee-arena) leave it
    # 1000-1500 south of the dog, inside its own culling's rect, where there is no far copy to check.
    ("host", "far-goto", "off:0,2200"),
    ("client", "far-goto", "centre"),
    ("client", "far-dog-active"),
    ("host", "far-dog-to-client"),
    ("host", "heal"),
    ("client", "far-dog-shown"),
    # (Not compared here: the dog chasing the client changes its loop sound from moment to moment, and the two
    # dumps are a few seconds apart. The final dumps compare enemies.)
    ("host", "far-dog-gone"),
    ("client", "far-dog-hidden"),
    ("host", "heal"),
    ("client", "heal"),
    # The host goes 7000 east, where the client's game has everything switched off, and opens a door there. It
    # leaves the way it came and round by the north, so it doesn't push the door's leaf on its way back. The client
    # walks there later: the door must be drawn open there too.
    ("host", "far-goto", "off:7000,0"),
    ("host", "far-door", "off:7000,0", "open"),
    ("host", "far-doors-info", "off:7000,0"),
    ("host", "far-goto", "off:7000,0"),
    ("host", "far-goto", "off:7000,1500"),
    ("host", "far-goto", "centre"),
    ("client", "far-door-check", "off:7000,0", "open"),
    ("host", "far-doors-info", "off:7000,0"),
    ("client", "far-doors-info", "off:7000,0"),
    ("both", "compare", "client", "doors"),
    # Not run yet: "far-drag FROM TO" then the client's "far-obj-shown TO" (an object dragged where the client's
    # culling has it off, on the culling grid). Neither the test area nor the house 7000 east has a draggable thing
    # with a grid node of its own: their furniture is switched on and off with its location (Location.objects).
    ("client", "far-goto", "centre"),
    ("host", "far-goto", "centre"),
]

POST = []

REQUIRE = ["doors", "items", "story", "time"]
