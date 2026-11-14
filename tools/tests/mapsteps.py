"""Step lists that several map scenario modules use (not a battery itself: run-test.sh takes map_scenario_*.py)."""

# Both players unhurtable (i2-safe): a death changes the story flags the dumps compare.
SAFE_ON = [("host", "i2-safe", "on", {"settle": 0}), ("client", "i2-safe", "on", {"settle": 0})]
SAFE_OFF = [("host", "i2-safe", "off", {"settle": 0}), ("client", "i2-safe", "off", {"settle": 0})]

# The test area's trader out of both games, for a morning in the hideout, and back.
TRADER_AWAY = [("host", "trader-away", {"settle": 0}), ("client", "trader-away")]
TRADER_BACK = [("host", "trader-back", {"settle": 0}), ("client", "trader-back")]
