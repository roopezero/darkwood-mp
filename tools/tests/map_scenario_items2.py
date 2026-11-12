"""Map test steps for items used through the game's own code: a hotbar lantern's light, map items, throwables thrown
(Player.throwItem) and firearms fired and reloaded (Player.attack, fireWeapon, reload) at a spawned dog; same format
as map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioItems2.cs. Run alone with
MAP_SCENARIO=map_scenario_items2 tools/run-test.sh map."""

SETTLE = 1.5

PRE_JOIN = []

LANTERN = [
    # The client's lantern lights the area round its stand-in in the host's game (Core.isInLight), and the host's
    # lantern its puppet in the client's.
    ("client", "i2-goto", "dark"),
    ("host", "i2-lit", "no"),
    ("client", "i2-lantern", "on"),
    ("host", "i2-lit", "yes"),
    ("client", "i2-lantern", "off"),
    ("host", "i2-lit", "no"),
    ("client", "i2-goto", "centre"),
    ("host", "i2-goto", "dark"),
    ("client", "i2-lit", "no"),
    ("host", "i2-lantern", "on"),
    ("client", "i2-lit", "yes"),
    ("host", "i2-lantern", "off"),
    ("client", "i2-lit", "no"),
    ("host", "i2-goto", "centre"),
]

MAPS = [
    # A map read by one player puts its places in the other's journal and on their map.
    ("host", "i2-map-pick", {"settle": 0}),
    ("client", "i2-map-pick", {"settle": 0}),
    ("client", "i2-map-use"),
    ("host", "i2-map-check"),
    ("host", "i2-map-pick", {"settle": 0}),
    ("client", "i2-map-pick", {"settle": 0}),
    ("host", "i2-map-use"),
    ("client", "i2-map-check"),
]

THROWS = [
    # very hungry: the noise lure brings it within sight of the client, and a dog chasing someone passes over meat
    # unless it is very hungry (Character.checkForLure)
    ("host", "i2-dog", "meat", "--hungry"),
    # a noise lure the client throws: the host's copy lands and its noise reaches the host's dog
    ("host", "i2-note", {"settle": 0}),
    ("client", "i2-throw", "noiseLure", "meat", {"settle": 0}),
    ("host", "i2-seen", "noiseLure", "meat", "lands"),
    ("host", "i2-heard", "meat"),
    # meat the client throws: the host's dog eats the host's copy, and the client's goes too
    ("host", "i2-note", {"settle": 0}),
    ("client", "i2-throw", "meat_soporific", "meat", {"settle": 0}),
    ("host", "i2-seen", "meat_soporific", "meat", "lands"),
    ("host", "i2-eats", "meat"),
    ("client", "i2-gone"),
    ("client", "heal"),                 # (the dog bit while it was hunting)
    # a gas bomb the client throws goes off on the host's dog
    ("client", "heal"),
    ("host", "i2-note", "meat", {"settle": 0}),
    ("client", "i2-throw", "gasBomb", "meat", {"settle": 0}),
    ("host", "i2-seen", "gasBomb", "meat", "off"),
    ("host", "i2-gassed", "meat"),
    ("client", "heal"),
    # a flare, a stone and molotovs on open ground, each way
    ("client", "i2-note", {"settle": 0}),
    ("host", "i2-throw", "flare", "fire", {"settle": 0}),
    ("client", "i2-seen", "flare", "fire", "lands"),
    ("host", "i2-note", {"settle": 0}),
    ("client", "i2-throw", "stone", "fire", {"settle": 0}),
    ("host", "i2-seen", "stone", "fire", "lands"),
    ("host", "i2-note", {"settle": 0}),
    ("client", "i2-throw", "molotov", "fire", {"settle": 0}),
    ("host", "i2-seen", "molotov", "fire", "fire"),
    ("client", "i2-note", {"settle": 0}),
    ("host", "i2-throw", "molotov", "fire", {"settle": 0}),
    ("client", "i2-seen", "molotov", "fire", "fire"),
    ("host", "i2-kill", "meat"),
    ("host", "heal"),
    ("client", "heal"),
]

# Each gun, fired by the client and by the host: every hit on the host's dog accepted (a shotgun's pellets each
# send one), the damage lands on the host, and the shots show on the shooter's puppet in the other game.
GUNS = [("weapon_pistol_02_semi", 6), ("weapon_shotgun_02_pumpAction", 3), ("weapon_rifle_02_boltAction", 3),
        ("weapon_shotgun_04_fullAuto_ZePSUTY", 10), ("weapon_submachine_01_full", 12), ("weapon_assault_01_burst", 4, "--loaded"),
        ("weapon_pistol_01_pellet", 3)]
# A fresh dog for each gun: a dog that's been shot chases the shooter and may run into the stations' traps.
FIRE = []
# A gun marked --loaded starts with a full clip: the dog's bites interrupt the burst rifle's long reload every time.
for gun, n, *flags in GUNS:
    FIRE += [
        ("host", "i2-kill", "gun", {"settle": 0}),
        ("host", "i2-dog", "gun", "100000"),
        ("host", "i2-note", "gun", {"settle": 0}),
        ("client", "i2-note", {"settle": 0}),
        ("client", "i2-fire", gun, "gun", str(n), *flags),
        ("host", "i2-hurt", "gun"),
        ("host", "i2-shots-seen", str(n)),
        ("host", "i2-note", "gun", {"settle": 0}),
        ("client", "i2-note", {"settle": 0}),
        ("host", "i2-fire", gun, "gun", str(n), *flags),
        ("host", "i2-hurt", "gun"),
        ("client", "i2-shots-seen", str(n)),
        ("host", "heal", {"settle": 0}),
        ("client", "heal"),
    ]
FIRE += [("host", "i2-kill", "gun"), ("host", "heal"), ("client", "heal")]

# Both players unhurtable while these run (i2-safe): a death changes the story flags the dumps compare.
SAFE = [("host", "i2-safe", "on", {"settle": 0}), ("client", "i2-safe", "on", {"settle": 0})]
# The clock held at noon for the throws and the guns (ee-day): a night's worms bite the client the whole time it
# reloads, and each bite stops a reload starting.
DAY = [("host", "ee-day")]
STEPS = (SAFE + LANTERN + MAPS + DAY + THROWS + FIRE
         + [("host", "alive"), ("client", "alive"), ("host", "ee-day", "off"), ("host", "i2-safe", "off"), ("client", "i2-safe", "off")])

POST = []

REQUIRE = []
