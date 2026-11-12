"""Map test steps for separate locations; same format as map_scenario.py, plus ("both", "compare", WHO, SECTIONS):
both games dumped there and then around WHO's player and compared (mapdriver.py), SECTIONS required.
The steps are in src/DarkwoodMP/World/MapScenarioLocations.cs.

The client goes into a location first, so the host loads it for them and runs it without its own player there;
the host puts a dog beside them (it bites for 1), which sees the client's stand-in, the client hits it, and the
host's AI turns it on them. The host saves (the location is saved as its player left it). The client walks up to
one of the location's sleeping enemies and runs on the spot; it wakes and comes at them. The dog dies before
the wake (so the woken enemy goes for the client, not the dog), then the woken enemy, the client leaves (the host stops running the location) and comes back (both still dead). Then the host
comes in too, and both go back to the world.
"""

LOCATION = "outside_church_underground_01"

PRE_JOIN = []

STEPS = [
    ("client", "loc-go", LOCATION, {"settle": 3}),
    ("host", "loc-dog", {"settle": 1}),
    ("client", "loc-dog-check", "alive"),
    ("host", "loc-sight"),                           # the dog sees the client's stand-in before anyone hits it
    ("client", "loc-hit"),
    ("host", "loc-dog-target"),                      # the dog goes for the client's stand-in, by threat
    ("both", "compare", "client", "enemies"),        # the host's player isn't in the location
    ("host", "loc-save"),                            # saved as the host's player left it
    ("host", "loc-kill", {"settle": 1}),             # the dog dies first, so the woken one has only the client to go for
    ("host", "loc-tame"),
    ("client", "loc-approach"),                      # by one of the location's sleeping enemies
    ("client", "loc-noise"),
    ("host", "loc-wake"),                            # it wakes and comes at the client's stand-in
    ("both", "compare", "client", "enemies"),
    ("client", "heal"),
    ("host", "loc-kill", "woken", {"settle": 3}),    # one of the location's own
    ("client", "loc-dog-check", "dead"),
    ("client", "loc-back", {"settle": 3}),
    ("host", "loc-stopped", LOCATION),               # nobody's in it: the host stops running it
    ("client", "loc-go", LOCATION, {"settle": 3}),
    ("client", "loc-dog-check", "dead"),             # killed stays killed
    ("both", "compare", "client", "enemies"),        # the woken one too, and the sleepers still asleep
    ("client", "heal"),
    ("host", "loc-go", LOCATION, {"settle": 3}),
    ("both", "compare", "client", "enemies"),        # both are
    ("client", "heal"),
    ("host", "heal"),
    ("host", "loc-back"),
    ("client", "loc-back", {"settle": 3}),
]
