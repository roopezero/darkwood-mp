"""Map test steps for the lights a player carries, as the other game shows them on their puppet (a torch in hand, the
flashlight's beam, a hotbar lantern), and for each game's own view: every light on our player and on a puppet draws
into a mesh of its own, so the view cone and light dot around our player are worked out where our player stands
(a puppet's lantern once drew the host's surroundings, walls' shadows and all, into the joiner's own light dot).
Same format as map_scenario.py; the steps are in src/DarkwoodMP/World/MapScenarioLights.cs. Run alone with
tools/run-test.sh map:lights."""

from mapsteps import SAFE_ON, SAFE_OFF

SETTLE = 1.0

PRE_JOIN = []


def carried(who, other):
    """WHO holds a torch, then a flashlight, then nothing, then wears a lantern and takes it off; OTHER checks each."""
    return [
        (other, "lt-check", "none"),
        (who, "lt-hold", "torch"),
        (other, "lt-check", "torch"),
        (who, "lt-hold", "flashlight"),
        (other, "lt-check", "flashlight"),
        (who, "lt-hold", "fists"),
        (other, "lt-check", "none"),
        (who, "i2-lantern", "on"),
        (other, "lt-check", "lantern"),
        (who, "lt-check", "none"),       # the carrier's own view and light dot stay its own
        (who, "i2-lantern", "off"),
        (other, "lt-check", "none"),     # the puppet's lantern gone, our own light dot still ours
    ]


STEPS = SAFE_ON + carried("host", "client") + carried("client", "host") + SAFE_OFF

POST = []

REQUIRE = []
