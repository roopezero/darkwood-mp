"""Map test steps for skill points (the Skills panel) and the morning skill reset; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioSkills.cs. Run alone: MAP_SCENARIO=map_scenario_skills."""

STEPS = [
    # The host's levels raise the shared level, so the client's points can be spent.
    ("host", "skills-give", "3"),
    # The key opens the panel without the backpack; the skills take the mouse over their whole cells; K closes it.
    ("client", "skills-panel"),
    ("client", "skills-give", "2"),
    # A point spent: the host checks it (SkillSpend) and allows it.
    ("client", "skills-learn"),
    # Far more levels than the shared essence gives: the host refuses the next point and it's taken back.
    ("client", "skills-give", "20"),
    ("client", "skills-learn", "--refused"),
    # Back to the levels the stove knows (it reads Player.levelRequirements at our level).
    ("client", "skills-give", "-20"),
    # A reset outside the morning is refused by the host.
    ("client", "skills-reset", "off"),
]

# After the dumps: the client's morning is its own (its trader, its flags), so it would differ from the host's game.
POST = [
    # In the client's morning (after dawn, in its hideout) the reset is allowed.
    ("host", "trader-away", {"settle": 0}),
    ("client", "trader-away"),
    ("client", "an-start", "noreward", {"settle": 4}),
    ("client", "skills-reset", "on"),
    ("client", "an-end"),
    ("host", "trader-back", {"settle": 0}),
    ("client", "trader-back"),
]
