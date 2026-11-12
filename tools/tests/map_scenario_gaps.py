"""Map test steps for gaps in docs/INTERACTIONS.md that were fixed but not yet tested in game; same format as
map_scenario.py. The steps are in src/DarkwoodMP/World/MapScenarioGaps.cs."""

PRE_JOIN = [
    ("host", "gaps-setup"),             # 64: a wardrobe with rotting food; 66: a crate with a shadow on it
]

STEPS = [
    # 66: the client's two hits lower the host's shadow (not the crate); the host's kills it in both games
    ("host", "shadow-ready"),
    ("client", "shadow-ready"),
    ("client", "shadow-hit", "10"),
    ("client", "shadow-hit", "10"),
    ("host", "shadow-check", "alive", "10"),
    ("host", "shadow-hit", "20"),
    ("host", "shadow-check", "dead"),
    ("client", "shadow-check", "dead"),
    # 65: a vine each game made by itself; the client cuts half, the host the rest
    ("host", "vine-make", {"settle": 0}),
    ("client", "vine-make"),
    ("client", "vine-cut", "half"),
    ("host", "vine-check", "attached", "half"),
    ("host", "vine-cut", "all"),
    ("client", "vine-check", "cut"),
    ("host", "vine-check", "cut"),
    # 64: the client's open wardrobe rots its food; neither that nor taking the food is refused
    ("client", "rot-open"),
    ("host", "rot-host"),
    # 61: a key the host hands over leaves the client's journal; a quest item the client hands over the host's
    ("host", "journal-pick", "key", {"settle": 0}),
    ("client", "journal-pick", "key", {"settle": 0}),
    ("host", "journal-add", {"settle": 3}),
    ("client", "journal-has", "yes"),
    ("host", "journal-remove", {"settle": 3}),
    ("client", "journal-has", "no"),
    ("host", "journal-has", "no"),
    ("host", "journal-pick", "item", {"settle": 0}),
    ("client", "journal-pick", "item", {"settle": 0}),
    ("client", "journal-add", {"settle": 3}),
    ("host", "journal-has", "yes"),
    ("client", "journal-remove", {"settle": 3}),
    ("host", "journal-has", "no"),
    ("client", "journal-has", "no"),
    # 68: the client leaves a death bag as from a separate location; the host has its marker; taking it removes both
    ("client", "bag-drop"),
    ("host", "bag-check", "here"),
    ("client", "bag-take"),                    # (the host's bags are full of the test kit)
    ("host", "bag-check", "gone"),
    # 11: the host dreams: the client's clock waits; the client dreams: the host's clock doesn't move its dream's
    ("client", "clock-note", {"settle": 0}),
    ("host", "dream", "start"),
    ("client", "clock-check", "noted", "8"),  # the clock runs about a minute a second: 8 s would show it
    ("host", "dream", "end", {"settle": 3}),
    ("client", "dream", "start"),
    ("client", "clock-check", "dream", "4"),
    ("client", "dream", "end", {"settle": 4}),
    # 62, 63, 38: the morning in the hideout, in both games (without the test area's trader, see trader-away)
    ("host", "trader-away", {"settle": 0}),
    ("client", "trader-away"),
    ("host", "an-start"),
    ("client", "an-start", {"settle": 6}),
    ("host", "an-clock", "stopped"),            # 62: everyone is in a morning
    ("host", "talk"),                           # 63: one at a time, though each has his own copy
    ("client", "talk", "--refused"),
    ("host", "talk-end", {"settle": 2.5}),
    ("client", "talk"),
    ("client", "trade-open"),                   # issue 15: the client trades while the host walks out
    ("host", "an-end"),                         # 38: the host's morning ends: the client's too
    ("client", "talk-free"),                    # 15: the trade and dialogue closed, the controls are back
    ("client", "an-check", "off"),
    ("host", "an-start", "noreward"),
    ("client", "an-start", "noreward", {"settle": 6}),
    ("host", "an-clock", "stopped"),
    ("client", "an-end"),                       # 62: the client walks out: the host's clock runs on
    ("host", "an-clock", "running"),
    ("host", "an-end"),
    ("client", "an-check", "off"),
    ("host", "trader-back", {"settle": 0}),
    ("client", "trader-back"),
    ("host", "heal"),
    ("client", "heal"),
]

REQUIRE = ["shadows"]  # (a cut vine is gone by the time of the dump, in both games)
