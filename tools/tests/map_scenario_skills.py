"""Map test steps for skill points (the Skills panel) and the morning skill reset; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioSkills.cs. Run alone: MAP_SCENARIO=map_scenario_skills."""

from mapsteps import TRADER_AWAY, TRADER_BACK

STEPS = [
    # The host's levels raise the shared level, so the client's points can be spent.
    ("host", "skills-give", "3"),
    # Several points spent one after another on the panel (the host's own: no request).
    ("host", "skills-learn-all"),
    # The key opens the panel without the backpack; the skills take the mouse over their whole cells; K closes it.
    ("client", "skills-panel"),
    ("client", "skills-give", "2"),
    # A point spent: the host checks it (SkillSpend) and allows it.
    ("client", "skills-learn"),
    # Two more points (three levels), spent one after another: each choice goes to the host and is allowed.
    ("client", "skills-give", "1"),
    ("client", "skills-learn-all"),
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
    *TRADER_AWAY,
    ("host", "an-dawn", {"settle": 0}),  # a new day's morning (a walk-out ends the day's morning for everyone)
    ("client", "an-start", "noreward", {"settle": 4}),
    ("client", "skills-reset", "on"),
    ("client", "an-end"),
] + TRADER_BACK
