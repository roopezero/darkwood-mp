"""Map test steps for story NPCs' corpses; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioCorpse.cs. A story NPC killed by one player must lie dead and searchable in every
game, also after the players' areas were switched off and on again, and what it carries is taken once.

  1. The chicken lady (Characters/NPC/Baba, one hit kills her), in the save the client loads (the same save id in
     both games): the client kills her, then loots her; the host's corpse is empty after it.
  2. The mushroom granny, made by each game for itself after the join (a save id of each game's own, as for a
     location each game loads while playing together; the chicken lady's house is one): the client kills her, the
     host loots her.
  3. The same with the doctor, killed by the host.

Alone: tools/run-test.sh map:corpse."""

SETTLE = 1.5
LADY = "Baba"  # the chicken lady (story name chickenLady)
GRANNY = "MushroomGranny"
DOCTOR = "Doctor_idle"

PRE_JOIN = [
    ("host", "npc-spawn", LADY, "600", "-500"),
]


def _corpse_everywhere(npc):
    """Its corpse in both games, then again after the client's area and the host's were switched off and on."""
    return [
        ("host", "sn-corpse", npc),
        ("client", "sn-corpse", npc),
        ("client", "sn-away", npc, "5000"),
        ("host", "sn-away", npc, "5000"),
        ("client", "sn-corpse", npc),
        ("host", "sn-corpse", npc),
    ]


STEPS = [
    ("host", "i2-safe", "on", {"settle": 0}),
    ("client", "i2-safe", "on", {"settle": 0}),
    # 1. From the save, killed and looted by the client.
    ("host", "sn-goto", LADY, "600", {"settle": 3}),
    ("client", "sn-goto", LADY, "300", {"settle": 3}),
    ("host", "sn-info", LADY),
    ("client", "sn-info", LADY),
    ("client", "sn-kill", LADY, {"settle": 3}),
    ("host", "sn-info", LADY),
    ("client", "sn-info", LADY),
    *_corpse_everywhere(LADY),
    ("client", "sn-loot", LADY, {"settle": 3}),
    ("host", "sn-looted", LADY),
    # 2. Each game's own, killed by the client, looted by the host.
    ("host", "sn-spawn-own", GRANNY, "-600", "-500"),
    ("client", "sn-spawn-own", GRANNY, "-600", "-500", {"settle": 3}),
    ("host", "sn-goto", GRANNY, "600", {"settle": 3}),
    ("client", "sn-goto", GRANNY, "300", {"settle": 3}),
    ("client", "sn-kill", GRANNY, {"settle": 3}),
    ("host", "sn-info", GRANNY),
    ("client", "sn-info", GRANNY),
    *_corpse_everywhere(GRANNY),
    ("host", "sn-loot", GRANNY, {"settle": 3}),
    ("client", "sn-looted", GRANNY),
    # 3. Each game's own, killed by the host.
    ("host", "sn-spawn-own", DOCTOR, "0", "-900"),
    ("client", "sn-spawn-own", DOCTOR, "0", "-900", {"settle": 3}),
    ("client", "sn-goto", DOCTOR, "600", {"settle": 3}),
    ("host", "sn-goto", DOCTOR, "300", {"settle": 3}),
    ("host", "sn-kill", DOCTOR, {"settle": 3}),
    ("host", "sn-info", DOCTOR),
    ("client", "sn-info", DOCTOR),
    *_corpse_everywhere(DOCTOR),
    ("host", "heal"),
    ("client", "heal"),
    ("host", "alive"),
    ("client", "alive"),
    ("host", "i2-safe", "off", {"settle": 0}),
    ("client", "i2-safe", "off", {"settle": 0}),
]

REQUIRE = []
