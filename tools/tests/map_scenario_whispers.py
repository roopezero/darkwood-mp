"""Map test steps for an area's enter and exit events; same format as map_scenario.py.
The steps are in src/DarkwoodMP/World/MapScenarioWhispers.cs.

Players got stuck hearing whispers: entering an area trigger started a looping whisper in every game (its enter
events were shared), but its exit events, which stop it, ran only in the game of the player who walked out. A
repeatable area's sounds, colour fades and particles are now the player's own (only the game of the player who walks
in or out runs them), so the other game never hears them. Each player walks in and out in turn; then the host stands
in the area (a real collider, in the host's game only) while the client leaves, and the host's whispers must go on
until the host walks out too."""

PRE_JOIN = []

STEPS = [
    ("host", "whisper-setup"),
    ("client", "whisper-setup"),
    # the client walks in and out: the whispers are the client's own, the host never hears them
    ("client", "whisper-enter"),
    ("host", "whisper-check", "stopped"),
    ("client", "whisper-leave"),
    ("host", "whisper-check", "stopped"),
    # the same the other way round
    ("host", "whisper-enter"),
    ("client", "whisper-check", "stopped"),
    ("host", "whisper-leave"),
    ("client", "whisper-check", "stopped"),
    # the client walks in; the host walks in too (the game's own collider); the client's leaving doesn't stop the
    # host's whispers while the host is still in the area, the host's own leaving does, in both games
    ("client", "whisper-enter"),
    ("host", "whisper-stand"),
    ("host", "whisper-check", "playing"),
    ("client", "whisper-check", "playing"),
    ("client", "whisper-leave"),
    ("host", "whisper-check", "playing"),
    ("host", "whisper-out"),
    ("client", "whisper-check", "stopped"),
    ("host", "heal"),
    ("client", "heal"),
]
