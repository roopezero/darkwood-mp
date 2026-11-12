"""The join alone, for its timings: the host packs and offers its world, the client game downloads, unpacks and loads
it, then both games are dumped and compared. No steps. Read the times from the [join] lines and the host's
"sent as" line in the two logs. Run with MAP_SCENARIO=map_scenario_join tools/run-test.sh map."""

STEPS = [
    # The host's snapshot for the joiner: how long the host's game stopped while it was made.
    ("host", "join-stop"),
]
