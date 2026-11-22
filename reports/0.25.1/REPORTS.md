# Playtest 0.25.1 reports

Reports from the 0.25.1 playtest, as received (some translated from Finnish). "Joining players" are the players who joined the host's session (clients). Triage and progress: [TRIAGE.md](TRIAGE.md).

### R01. Bug: Shadow on top of the crate doesn't disappear for joining players

Location: Chapter 2, hideout.

The shadow on top of the crate doesn't disappear for players who joined the host's session. (Presumably it works for the host.)

### R02. Bug: Black shadow barrier not visible to other players

Location: Chapter 2, hideout.

When the red chomper came to the base, the black shadow barrier (the one that is destroyed with light) was not visible to the other players.

### R03. Bug: Container window sometimes gets stuck open

Sometimes, when a player opens a corpse or another container, the window gets stuck open. The only way to get rid of it is to leave the game and rejoin.

Intermittent, no known reliable repro. Worth checking: the container being destroyed, emptied or despawned while the window is open; two players opening the same container; another UI opening on top of it; the player dying or a cutscene starting while it's open.

### R04. Bug: Diving doesn't work for joining players

Players who joined the host's session (clients) can't dive.

### R05. Bug: When one player dives, every player dives

If one player dives, every player goes into the diving state, wherever they are in the world.

### R06. Request: Oxygen tank as a persistent flag, and anyone can dive

Make the oxygen tank a persistent, static flag: once it has been filled, it stays filled for good (saved with the game) and players can dive without limit. Every player must be able to dive, not only the player who filled it.

(R04, R05 and R06 come from the same original report.)

### R07. Request: Tooltips for all items and map locations

Some items have no tooltip, for example "Tubular magazine". ALL items and map locations should have tooltips that give factual information about them.

- List every item and map location that is missing a tooltip.
- Base the text on the actual game data (what it is, what it does, stats where relevant). Don't invent stats or facts.
- Where something can't be derived from the data, write a draft and mark it in the triage file for my review.
- If practical, add a test or check that fails when an item or map location has no tooltip.

### R08. Request: Hide HUD and inventory screens when the pause menu is open

When the pause menu is opened, the HUD, any inventory screens and other open UI should be hidden, and restored as they were when the pause menu closes.

### R09. Bug: Some story progression still requires the host

Some story progression still requires the host to trigger something. Any player must be able to advance the story, not only the host.

Go through every story progression trigger, not just the ones that are known to be broken, and list in the triage file which ones are host-only.

### R10. Bug: Player names show through cutscenes and menus

Player name labels are drawn on top of cutscenes and menus. They should be hidden while a cutscene or menu is showing.
