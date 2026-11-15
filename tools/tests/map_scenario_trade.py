"""Map test steps for a trade paid with reputation the client has only just earned; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioWorld.cs (trader-restock, trade, trade-seen). Run with
tools/run-test.sh map:trade.

The client's player earns reputation with the night trader (only its game has it: FlagsSync sends it every two
seconds) and in the same frame takes his cheapest thing for nothing, paid with that reputation. The host checks the
trade's reputation against its own copy, so the earned reputation must reach it first (TraderSync flushes the story
changes before the TraderTrade request, on the same ordered channel): the host takes the trade, and both games then
have the same stock and reputation. A trade of the host's follows (the client's bags are empty in the test world)."""

STEPS = [
    ("host", "trader-restock"),
    ("client", "trade", "--earn", {"settle": 2}),
    ("host", "trade-seen"),
    ("host", "trade", {"settle": 3}),
]
