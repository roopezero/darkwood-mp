"""Map test steps for a generator's running sound; same format as map_scenario.py. The steps are in
src/DarkwoodMP.MapTests/World/MapScenarioGenerator.cs. Run alone with tools/run-test.sh map:generator.

The game's Item.turnOn starts the generator's loop each time it's called, also when it's running already, and
forgets the older one, which then plays on after the generator is switched off. The morning after a night calls
it again (Controller.startAfterNight -> Location.enableAllLights -> Item.enable on every powered item, the
generator's own among them). A joiner switching the generator off after the night left it humming.
"""

from mapsteps import SAFE_ON, SAFE_OFF

GEN = "Generator_02@-480,-320"

PRE_JOIN = []


def both(*step):
    return [("host", *step), ("client", *step)]


STEPS = SAFE_ON + [
    ("client", "fuel", GEN, "50"),
    ("client", "switch", GEN, "on", {"settle": 2}),
    *both("gen-sound", GEN, "1"),
    # The morning, in each game.
    *both("gen-dawn", GEN),
    *both("gen-sound", GEN, "1"),
    ("client", "switch", GEN, "off", {"settle": 2}),
    *both("gen-sound", GEN, "0"),
    # The host's own: on, a morning, off.
    ("host", "switch", GEN, "on", {"settle": 2}),
    *both("gen-dawn", GEN),
    *both("gen-sound", GEN, "1"),
    ("host", "switch", GEN, "off", {"settle": 2}),
    *both("gen-sound", GEN, "0"),
] + SAFE_OFF

POST = []

REQUIRE = []
