# Changelog

Newest first. "Protocol N" is the network version: players can only play together when their mod versions have the same protocol. Each entry says how the change was tested; unless it says real players tried it, it was tested with scripted players and the two-game map test only.

## 0.22.1

*Protocol 26, plays with 0.21.x and 0.22.0.*

Fixes what was left of issues #17 and #18, found by moving the real mouse with ydotool: Unity sends no drag events for slow mouse movements, so the windows now follow the mouse itself each frame while moved or resized (they stayed on the cursor in every frame, slow and fast); the game's cursor sprite, which the windows covered, is hidden while one is open and the system cursor shows the game's cursor picture on top; and hovering skills no longer shifts the Skills panel (the room for descriptions was 8 px short, and a scrollbar flashed on for a frame and narrowed everything). The last is checked by a hover sweep over every skill in `--dwmp-solo-test`, which the old code fails; all of it at 1920x1080 only.

## 0.22.0

*Protocol 26, plays with 0.21.x.*

Sets Steam rich presence: the mod version, chapter, day, map area and players, shown in a friend's *View game info*. Its text was checked in the log with `--dwmp-test-presence` (main menu, hosting a world, the map area from the game's own names); it hasn't been tried with the Steam client running, so what friends actually see is untested.

## 0.21.1

*Protocol 26, plays with 0.21.0.*

Fixes GitHub issues #16 to #18: a spectator sees all around the player they watch, the Skills panel takes the mouse over each skill's whole cell, marks learned skills clearly and no longer opens the backpack, and the co-op windows follow a slow drag exactly. It's tested with the map test and in-game hooks (a test mouse position, made-up drag events); nobody has tried it with a real mouse yet.

## 0.21.0

*Protocol 26, so everyone must update.*

Open container, trader and workbench windows update while another player changes them, and a container's "searched" mark is shared; a savage's attack animations play the same in both games; a door a player opens keeps its leaf in step; things still sliding when someone joins are sent to the joiner; a new game or a deleted slot no longer gives a joiner the last world's character; night objects keep their newest trap state; a newly placed trap is no longer taken for the trap next to it; and a trap a client sets off spawns its vine only after the host agrees, so a refused trap leaves nothing behind and its bite is undone. The test batteries were rebuilt (`tools/run-test.sh maps`, `--list`, `--plan`, see `docs/MULTIPLAYER_TESTING_GUIDE.md`): all 23 map parts passed in one run; the next run, with the trap refusal test added (24 parts), failed two parts on test faults, which were fixed and then passed, and `all` (25 scripted tests) has no failure. None of it has been played by real players yet.

## 0.20.0

*Protocol 25, so everyone must update.*

Replaces the game's main and pause menu buttons with a Half-Life style list (Start Game, Join Game, Options, Credits, Quit; Back to main menu and Quit ask first), adds the Start Game window (save, new-game options, a host box, who played each save with you, save names) and an Options window with all the game's settings and key bindings (your name moved there), hides the loading screen's tip, adds a Skills panel (K) where skill points are spent anywhere while the stove only gives levels, allows a skill reset only in the morning before you leave your hideout, shows the objects night events make (lights, mushrooms and the rest) in every player's game, late joiners included, and draws the host's enemies between the host's last two states at the host's present instead of jumping between arrivals (stalls in a chase test went from 32.5% of frames to about 2.5%). It passes `all` (25 tests), `options`, `host-progress`, and the night and skills map scenarios. The full map run fails steps that follow from one death: an awake dog from the save patrols past the test stations and kills the client now and then. The 0.19.0 code does the same, with more bites in the same run. The new menus and windows were checked in screenshots and with test hooks; nobody has clicked through them or played 0.20.0 together yet.

## 0.19.0

*Protocol 24, so everyone must update.*

Has the host give ids to objects the world makes after a player joined, runs the world's own scripted events (a time of day, a death, something appearing) on the host and shares them, makes a joiner's scripted event or build wait for the host's yes instead of undoing it after a refusal, writes the joiner's world snapshot on a worker thread (the host's stop during a join went from 245 to 490 ms down to about 130 ms on the test world), shares the angle of a door leaf a player walks into, has a follower follow the joiner who recruited them, keeps a joiner who died for good dead when they rejoin, checks that a client carries what it places, shows the human spider's thrown projectile to joiners, and fixes a pooled enemy attack that hurt a joiner only once ever (so enemies such as the human spider's minion now land every hit). A full map run also found and fixed a story NPC's death from another game killing the wrong copy of that NPC (the wolfman's stock was lost), corpses the client kept after the host destroyed them, and a separate location's objects named differently in the two games. It passes `all` (25 tests) and the map test (one full run with 3 failed steps, all the burst rifle's reload in the test, then that scenario again after its fix); none of it has been played by real players yet.

## 0.18.1

*Protocol 23, so it plays with 0.18.0.*

Lays out the main menu and the pause menu as Half-Life and Counter-Strike 1.6 do, with the versions in the bottom right corner, shows the world behind the pause menu when playing alone too, has the skill reset at the stove playing alone, opens the co-op windows on the right and puts the loading line under the bar; checked in screenshots and the scripted tests, and the reset alone with `--dwmp-solo-test`.

## 0.18.0

*Protocol 23.*

Is for up to five players (the host and four friends) and is tested in a map test with a real host and three or four real client games: threat among several remote players, players far apart (each joiner's own culling, location events by who is there), the host's copies of joiners' morning traders and NPC positions, hostile NPCs run as the host's enemies, enemies' projectiles and their landings shared, placed traps checked by the host, a hotbar lantern's light on the wearer's puppet, map places shown on everyone's map, a molotov's gasoline spilled by the host only, night attackers budgeted per occupied hideout, containers watched from the moment they open, a door opened while switched off swinging open later, and story flags set during a death or a dream sent once sharing resumes. None of it has been played by real players yet.

## 0.17.0

*Protocol 22.*

Fixes what the 0.15.0 playtest reported on GitHub (issues #7 to #15): a held torch's flames on the torch, shared essence and levels with each player's own skills, a skill reset at the stove once a day and the host's level dreams dreamt together, a packed world made off the main thread (the host's longest frame during a join went from 1.3 s to about 0.3 s, the download from 11.3 MB to 2.6 MB, on the test world) with joins that can't hang and a "joining..." notice, Sort & Stack at the workbench, the co-op windows in Darkwood's look (movable and resizable, a Half-Life style server browser), joiners placed on a free spot and players that never push each other, no journal popups with raw names, and a joiner no longer stuck at the trader when the host's morning ends. It passes the map test and the scripted tests; none of it has been played by two real players yet, and the new download hasn't been tried over Steam or on Windows.

## 0.16.0

*Protocol 21.*

Has the host run a separate location's enemies for the joiners in it, as it runs the world's (gap 54), sends Steam messages over 512 KiB in pieces (Steam refuses longer ones), keeps a lobby refused for its version refused, no longer makes the game's SteamManager when checking for Steam, and covers in the map test about twenty fixes that hadn't been tried in game (gaps 11, 24, 28, 38, 40, 43 to 45, 50, 51, 58 and 61 to 68); what that found is fixed. The map test now also covers watching others after dying for good (the client dies on Nightmare and watches the host), the crouch while disarming (the host sees the client's puppet crouch over a chain trap, and the client stands up after), a joiner waiting while the host is in the prologue (the host answers as if it were, for 15 s; the real prologue, on a new game, hasn't been played with a joiner) and the LAN tab finding the host. Trying them fixed three things: the LAN search found nothing on Linux (it stopped at the first "port unreachable" and its wait ran out while it was still sending), a joiner could come in while the host watched the prologue's intro film, and a spectator following another player was put back by the host's movement check.

## 0.15.0

*Protocol 20.*

Closes the gaps a second read of the game's code found (gaps 41 to 68 in `docs/INTERACTIONS.md`: separate locations placed by the host, corpses for joiners, enemies reacting to joiners, the porter and hideout ovens, reputation, the morning clock, and more), makes connections sturdier (timeouts, keepalives, sends off the main thread, quick rejoin) and guards the GOG build against Steamworks references; joining now uses the game's own loading screen. It passes the map test and the scripted tests; most of it hasn't been played by two real players, and none of the Steam changes has been tried over Steam.

## 0.14.0

Fixes what the first players reported on GitHub (issues #1 to #6: a loading screen while joining, dead enemies that kept barking, the joiner's weapon, the workbench, larva eggs, burned infection) and the sync bugs the two-game map test found (dropped items copied on join, locked crates, barricades, trades, double damage from thrown things, hits inside explosions, pushed objects); all of it is tested in the map test and the scripted tests, not yet by two real players.

## 0.13.0

Everything added in this version (the gap fixes listed in `docs/INTERACTIONS.md`: night spawns, area damage, locks, events, weather, furniture, sounds, building, chapters and the ending, and more) is tested with scripted players only; nobody has played it together yet.

## Earlier

0.10.0 to 0.12.x built the base: connections over Steam and Direct, player puppets, the world sent to joiners, the host's enemies with threat, the shared world (time, doors, containers, items) and each player's own character. One real two-player session over a LAN with Direct happened in this period.

## Testing so far

Everything in the mod is built and tested against the real game, in both roles (host and client), with a scripted Python player on the other end, and in the two-game map test (`tools/run-test.sh map`): a real host and a real client on one machine, the client run from a second game folder (its own BepInEx and lock file, no Steam, so it takes the GOG path). Two people have played it together once, over a LAN with Direct; what they found is fixed here. The host's checks on what clients do (prediction and rollback, below) are tested with scripted players only. Steam hosting is tested. Steam joining was tried once by two real players: the world download stopped short (Steam's send queue was full and pieces were dropped), which 0.11.1 fixes. The scripted tests use Direct, so that fix hasn't been tried over Steam yet. The Windows and GOG builds haven't been run: the Windows zip's layout and the code's Windows and GOG paths (file paths, deleting folders while a virus scanner may have a file open, the LAN search's sockets, finding Steam without making the game's SteamManager, which on a build without steam_api would quit the game) were checked by reading the code and unpacking the zip only. 
