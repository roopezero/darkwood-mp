"""The join alone, for its timings: the host packs and offers its world, the client game downloads, unpacks and loads
it, then both games are dumped and compared. Read the times from the [join] lines and the host's
"sent as" line in the two logs. Run with MAP_SCENARIO=map_scenario_join tools/run-test.sh map (or map:join).

It also checks that story changes the host makes while the joiner loads reach the joiner: once the snapshot is read,
the host changes an NPC's reputation and sets a flag; the joiner is sent them when it's in (FlagsSync.JoinerInWorld),
and the dumps' story sections must match."""

PRE_JOIN = [
    ("host", "story-at-join"),
]

STEPS = [
    # The host's snapshot for the joiner: how long the host's game stopped while it was made.
    ("host", "join-stop"),
    # The story changed after the snapshot was sent to the joiner once it was in.
    ("host", "join-story"),
]
