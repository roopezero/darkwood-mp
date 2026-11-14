"""Map test steps for shared progression and the level dreams dreamt together; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioDreams.cs. They run after the dumps (a dream moves the players,
swaps their bags and sets the clock), before the client's death for good.

Three dreams: the level 2 one (the bunker); the church dream, whose enemy (a black chomper let out of a burnt
wardrobe) the host runs for both games, as it runs a separate location's: the client closes the wardrobe, the host's
chomper comes out and is shown in the client's game (none of the client's own dream characters runs), and both
games' enemies match; and the home dream, where everyone wakes in the bedroom and walking into the hall's knocking
area unlocks the bedroom door (its Door1 has a second Door1 on the same spot, which the host's ids must not mix up):
first the client walks in and the host opens the door, then the host walks in and the client opens it. Both games go
into each dream within a few seconds of each other (the host draws the dream as its transition starts)."""

DREAM = "dream_bunker_underground_01"
CHURCH = "dream_church_ruins_01"
HOME = "dream_home"


def home(knocker, opener):
    """The home dream: knocker walks into the knocking area, and the bedroom door opens for opener too."""
    return [
        ("host", "level-dream", HOME, {"settle": 0}),
        ("host+client", "dream-entered", {"max_spread": 3}),
        ("client", "in-host-dream", HOME),
        ("host", "home-door", "locked"),
        ("client", "home-door", "locked"),
        (knocker, "home-knock"),
        ("host", "home-door", "unlocked"),
        ("client", "home-door", "unlocked"),
        (opener, "home-open"),
        (knocker, "home-door", "open"),
        ("host", "end-level-dream", {"settle": 0}),
        ("client", "back-from-dream"),
        ("client", "heal"),
    ]


POST = [
    # The host sent the shared essence and level with the character; our bar shows it.
    ("client", "progress-shared"),
    # Out of the night trader's dream (the client's own, from the kill at the end of the steps).
    ("client", "dream-clear", {"settle": 0}),
    ("host", "dream-clear", {"settle": 0}),
    # The host's level brings a dream: the client goes into the same one by itself, at the same time.
    ("host", "level-dream", DREAM, {"settle": 0}),
    ("host+client", "dream-entered", {"max_spread": 3}),
    ("client", "in-host-dream", DREAM),
    # The host's dream ends: the client's ends with it.
    ("host", "end-level-dream", {"settle": 0}),
    ("client", "back-from-dream"),
    ("client", "heal"),
    # The church dream: its chomper is the host's.
    ("host", "level-dream", CHURCH, {"settle": 0}),
    ("client", "in-host-dream", CHURCH),
    ("client", "dream-wardrobe"),                    # the client lets the chomper out; the host runs it
    ("host", "dream-mob"),                           # running here, sent to the client, after someone; bites for 1
    ("client", "dream-mob-copies"),                  # the host's is shown, none of ours runs
    ("both", "compare", "client", "enemies"),
    ("host", "dream-chars"),
    ("client", "dream-chars"),
    ("client", "heal"),
    ("host", "end-level-dream", {"settle": 0}),
    ("client", "back-from-dream"),
    ("client", "heal"),
] + home("client", "host") + home("host", "client")
