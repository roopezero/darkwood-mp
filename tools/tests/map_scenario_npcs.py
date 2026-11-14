"""Map test steps for NPCs across games; same format as map_scenario.py. The steps are in
src/DarkwoodMP/World/MapScenarioNpcs.cs (and some from MapScenarioGaps.cs, MapScenarioWorld.cs and
MapScenarioFixChecks.cs). Alone: MAP_SCENARIO=map_scenario_npcs tools/run-test.sh map."""

from mapsteps import TRADER_AWAY, TRADER_BACK

SETTLE = 1.5

PRE_JOIN = []

# P4: an NPC the host's world moves (as a scripted move does) is moved in the client's game too (NpcSync), so the
# client talks to him where the host has him, and the host's reach check (TalkLock, 1500) passes.
WALK = [
    ("host", "npc-move", "trader", "2600", "0"),
    ("client", "npc-talk", "trader", "2600", "0"),
    # A world event that a character's animation fires is the host's, and switches an NPC on for talking in every
    # game (the musician hiding under the tractor: musician_setUnderTractor switches on the tractor's NPC).
    ("host", "anim-setup", "trader", "2600", "0"),
    ("client", "anim-setup", "trader", "2600", "0"),
    ("client", "anim-fire", "left"),
    ("host", "anim-fire", "run"),
    ("client", "anim-check"),
    ("client", "npc-talk", "trader", "2600", "0"),
    ("host", "anim-end"),
    ("client", "anim-end"),
    ("host", "npc-move", "trader", "back", {"settle": 3}),
    ("client", "fx-goto", "centre"),
]

# P2: the client's morning, when the host has no trader (it isn't in a morning): the host puts its own copy in the
# client's hideout, rolls his stock, and takes the client's trade with him. When the client walks out, it goes.
MORNING = [
    ("host", "wolf-away", {"settle": 3}),  # the morning brings the trader, not the wolf
    *TRADER_AWAY,
    ("host", "an-dawn", {"settle": 0}),  # a new day's morning (a walk-out ends the day's morning for everyone)
    ("client", "an-start", {"settle": 4}),
    ("host", "host-copy", "here"),
    ("client", "trade", {"settle": 2}),
    ("client", "an-end"),
    ("host", "host-copy", "gone"),
    *TRADER_BACK,
    ("host", "wolf-back"),
    ("host", "heal"),
    ("client", "heal"),
]

# P5: the client kills that morning trader: the host's copy dies too (NpcDied by name and place), and neither the
# kill nor the client's dead state is refused. Last: the client is in the killer's dream after it.
KILL = [
    ("host", "wolf-away", {"settle": 3}),
    *TRADER_AWAY,
    ("host", "an-dawn", {"settle": 0}),
    ("client", "an-start", "noreward", {"settle": 4}),
    ("host", "host-copy", "here"),
    ("client", "kill-trader", {"settle": 4}),
    ("host", "npc-died"),
    *TRADER_BACK,
    ("host", "wolf-back"),
    ("host", "heal"),
]

# A follower the client recruited (a scripted step making the NPC a follower, run on the host as the client's event)
# follows the client, not the host: the host's copy goes after the client's stand-in (NpcFollowers), and the client's
# copy comes to the client's player with it (NpcSync).
FOLLOW = [
    ("client", "follow-go", "-450", "-400"),  # beside the hideout's night trader, as after talking to him
    ("host", "follow-recruit", "trader"),
    # The night trader never starts walking (it stays idle as a follower), so whether a follower walks after the
    # joiner is left out: follow-check and follow-near need an NPC that walks.
    ("host", "follow-end", {"settle": 3}),
    ("client", "fx-goto", "centre"),
]

STEPS = WALK + FOLLOW + MORNING + KILL

REQUIRE = ["npcpos", "traders"]

POST = []
