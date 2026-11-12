"""Map test steps that fight each kind of enemy and creature in the game; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioEnemies.cs. Run only these with
MAP_SCENARIO=map_scenario_enemies tools/run-test.sh map (a core set, one of each kind of behaviour);
EE_FULL=1 fights every kind.

The host holds its clock at noon for the fights (ee-day), so no night comes with its worms and attackers.
For each kind: the host spawns it beside the client's player (sent after "the player": the client, by threat),
the client waits for it to come (--moves) and hurt it (--hurts), then hits it and anything it spawned until all
are dead (a real swing first, then hits for the most the pitchfork does, through the same path); the host
checks they're dead there too and whom it went for (--targets: the client's player); the client searches the
corpse; both games' enemies are compared; the host clears the corpses away.
"""

import os

SETTLE = 1.0

# (prefab under Characters/, what the client must see it do, in the core set). Left out: Villager1Granny,
# clone_villager_01, Bride_running, larva_big_01, centipede_hole (NPCs or props here: ee-spawn skips them) and
# LocationChars/RedneckBury (a location's buried redneck: it stays in the ground, not an enemy to fight).
KINDS = [
    ("ChomperRed", "--moves --hurts --cut", True),  # cut in half: ChomperHalf, spawned on the host
    ("ChomperRed_small", "--moves --hurts", False),
    ("ChomperBlack", "--moves --hurts", False),
    ("ChomperBride", "--moves --hurts --twice", False),
    ("Banshee", "--minions", True),  # its babies come a few seconds after it dies
    ("Spider01", "--moves", True),  # comes at the player but never bites, nor the host alone (ee-solo)
    ("Spider02", "--moves", False),
    ("Spider03_day", "--moves", False),
    ("HumanSpider", "--moves --hurts --throws", True),  # throws (rangedAttack: EnemySensorThrown); what lands is EnemyLanded
    # its clone, after it: the same pooled fist sensor must hurt the same player again (--twice; gap 74)
    ("HumanSpiderMinion", "--moves --hurts --twice", True),  # mostly fists up close: it may not throw
    ("Kamikaze", "--targets", True),  # sleeps where it's put; it doesn't come at the host alone either (ee-solo)
    ("Swamper1", "--moves --hurts", True),
    ("Centipede", "--targets", True),  # sleeps; it doesn't hurt the host alone in 15 s either (ee-solo)
    ("Redneck", "--moves --hurts", False),
    ("Redneck02", "--moves --hurts", False),
    ("Redneck03", "--moves --hurts", True),
    # immobile, with no attacks (view 0): they stand where they are; whom they face is the nearest player
    ("Villagers/Villager_infected1", "--targets", True),
    ("Villagers/Villager_infected2", "--targets", False),
    ("Villagers/Villager_infected3", "--targets", False),
    ("Villagers/Villager_infected4", "--targets", False),
    ("Villagers/Villager_infected1_ch2", "--targets", False),
    ("Villager", "--moves --hurts", True),
    ("Villager1_Burning", "--moves --hurts", False),
    ("Villager3_plank", "--moves --hurts", False),
    ("VillagerTorch", "--moves --hurts", True),
    ("VillagerPitchfork", "--moves --hurts", False),
    ("DogMutated", "--moves --hurts --twice", True),
    ("FakeChars/Worms_enemy_01", "", True),
    ("FakeChars/pig_big_mutant", "", True),
    # animals
    ("Raven", "", True),
    ("Pig", "", True),
    ("Chicken", "", True),
    ("Deer", "", True),
    ("Rabbit", "", True),
    ("Brat_babykury", "", True),
]
FULL = os.environ.get("EE_FULL", "") not in ("", "0")
# EE_ONLY=Kind1,Kind2: fight only these kinds (and skip the shadow, flee and solo steps)
ONLY = [k for k in os.environ.get("EE_ONLY", "").split(",") if k]

PRE_JOIN = []

# Both players healed whenever they're below half health (i2-safe): since a pooled melee sensor hits a player's
# stand-in again (Combat/CombatPatches.cs), an enemy lands every bite, and a test player dying changes the story
# flags the dumps compare. A fight still sees its first hit, which comes at full health.
SAFE = [("host", "dw-on", {"settle": 0}), ("host", "i2-safe", "on", {"settle": 0}), ("client", "i2-safe", "on", {"settle": 0})]

STEPS = SAFE + [
    ("host", "ee-day"),
    ("client", "ee-arena"), ("host", "heal"),
    # First, as the fights below take most of the map test's time:
    # a joiner's flashlight against shadow armour (P9),
    ("host", "ee-shadow-spawn"),
    ("client", "ee-flashlight", {"settle": 0.5}),
    ("host", "ee-shadow-check"),
    # a scripted run-away activity runs from the nearest player (P8),
    ("host", "ee-flee-activity"),
    ("client", "ee-arena"),
]
if ONLY:
    STEPS = STEPS[:len(SAFE) + 3]
# Does the centipede hurt the host's own player, in the game's single-player way? (In the fight it goes for the
# client but never hurts it.)
if not ONLY:
    STEPS += [("host", "ee-solo", "Centipede"), ("host", "heal"), ("host", "ee-solo", "Kamikaze"), ("host", "heal")]
if FULL:
    # do spiders bite at all? (In the fights they come at the client but never hurt it.)
    STEPS += [("host", "ee-solo", "Spider01"), ("host", "heal")]
for kind, wants, core in KINDS:
    if (kind not in ONLY) if ONLY else not (core or FULL):
        continue
    flags = wants.split()
    STEPS += [
        ("host", "ee-spawn", kind, {"settle": 0.5}),
        ("client", "ee-fight", kind, *[f for f in flags if f != "--targets"], {"settle": 1.0}),
        ("host", "ee-check", *(["--targets"] if "--hurts" in flags or "--targets" in flags else [])),
        ("client", "ee-corpse"),
        ("both", "compare", "client", "enemies"),
        ("host", "ee-clear"),
        ("client", "heal"),
        ("host", "heal"),
        ("client", "ee-arena", {"settle": 0.5}),
    ]
STEPS += [("client", "heal"), ("host", "alive"), ("client", "alive"), ("host", "ee-day", "off"),
          ("host", "i2-safe", "off", {"settle": 0}), ("client", "i2-safe", "off", {"settle": 0})]

POST = []
REQUIRE = []
