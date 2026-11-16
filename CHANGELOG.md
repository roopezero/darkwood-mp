# Changelog

Newest first. "Protocol N" is the network version: players can only play together when their mod versions have the same protocol. Each entry says how the change was tested; unless it says real players tried it, it was tested with scripted players and the two-game map test only.

## 0.28.0

*Protocol 29: plays with 0.27.0 (a push from a 0.27.0 player is shown as a jump, as before).*

- **Pushed things stay where they were pushed, for everyone.** Something a player knocks with a hit or a shot from further away now moves for everyone; it used to be put back in the shooter's game. Over a real connection, a client pushing something on had it jump back under them for a moment, when the host's answer to its last push arrived. An open door's leaf pushed past 105 degrees now ends at the same angle for everyone. Others now see a pushed or hit thing slide, and a door's leaf swing, rather than jump to where it stopped. A leaf pushed slowly, or by the host's enemies, now ends at the same angle for everyone.
- **Soft collision between players.** Two players standing in each other are eased apart, gently; walking into someone only slows you down. `SoftPlayerCollision` in the settings turns it off.

How it was tested: with the four-game map test (`map4`, the host and three client games) and a new pushing scenario: bodies pushing furniture, lamps and door leaves, other players bumping the same things after them, a hit from 150 away, a push while one client's copy was switched off far away, a house far from the host, and players standing in each other, on localhost and with 120 ms of lag added to every message. Nobody has played it together yet.

## 0.27.0

*Protocol 29: doesn't play with 0.26.0 or older. Everyone needs to update.*

Fixes from the 0.25.1 playtest:
- **Only the player who dives, dives.** In the chapter 2 village cellar, one player diving made every player dive wherever they were, and could leave them unable to move. Any scripted step that acts on "the player" now acts only on the player who set it off.
- **Anyone can dive once the oxygen tank has been filled.** Once any player has a full tank, every player can dive in the cellar, as often as they like, without carrying it. This is saved with the host's world. Each player gets the first dive's dream once.
- **Short scripted trips aren't undone.** The host took a joiner's short scripted teleport (1000-3000 units) for cheating and put them back.
- **Any player can move the story on.** A story event set off by opening a door, flipping a switch, disarming a trap or hitting a story character now goes to the player who did it. Before, the host's player got it, and the player who did it was refused. The same goes for what happens when a joiner kills a story character or switches on a machine. A player who joins later, or after the host reloads, still gets their own part of a one-time event someone else set off before, such as a trip down a hole.
- **Shadows.** A crate's shadow that was burnt off before a player joined is gone for them too. Night enemies' black shadow armour now shows in every game, and goes when the host's armour is burnt off.
- **Container windows close when the container goes.** A corpse or container removed while a player had it open left its window on screen until they left the game.
- **Name tags are hidden under menus and cutscenes**, and so are the HUD and any open inventory under the pause menu. They come back as they were when it closes. Tags aren't hidden for good after the ending or credits were seen and another save was loaded in the same session.
- **Descriptions for items and map locations that had none**, such as the Tubular magazine, with stats from the game's data. They're in English whatever the game's language.

How it was tested: with scripted players and the two-game map test only; nobody has played this build together yet. New map batteries cover each fix: `map:dive` (a player-targeted step, the trip check, the shared tank and the once-per-player dream), `map:story` (door, switch, trap and hit triggers set off by the joiner and by the host) and `map:story2` (a joiner switching on a machine; a once-only trigger owed after a join and after the host's record is reloaded). New steps were also added to `map:gaps` (a dead shadow in a loaded save), `map:enemies` (a copy's armour shown and removed), `map:corpse` (a corpse removed while its window is open) and `map:ui` (HUD under the pause menu). Two more tests are new or extended: `tooltips` (no item or chapter 1 map location without a description) and `shots` (name tags and the HUD under the pause menu). Each new step failed without its fix and passed with it, except `map:gaps`, `map:enemies` and `map:ui`, which weren't run without their fixes. Diving was tested with the cellar loaded into the chapter 1 test world, and the armour on a dog given it, not a red chomper. The full suite (`full` plus the new batteries and `shots`/`tooltips`) was run once at the end: all passed except `shots` and `map:ui`, which found tags hidden after the pause menu when an earlier test in the same game had played the ending (fixed below, both rerun: PASS), and one crate position after the `drag-race` step in the full `map`, which passed when `map:world` was rerun alone.

## 0.26.0

*Protocol 28: doesn't play with 0.25.x or older. Everyone needs to update.*

Fixed:
- **The prologue's house door opens when a joiner unlocks it.** The key from the wardrobe is found in a dream, and keys, notes and items found in dreams weren't shared. So the host refused the joiner's unlock with "nobody has the key", rolled the door back to locked, and couldn't open it either: nobody could get through. Now anything found in a dream that everyone dreams together (the prologue, the level dreams) reaches everyone's journal and goes again when the dream ends, and the key reaches the host before the unlock does.
- **A joiner dropped out of the game whenever the host's world changed.** Since 0.25.1, a joiner left the session every time the host's world reloaded or a new chapter started, because the mod's own trip into the host's new world counted as going back to the main menu. Joiners now stay in.
- **A dream that only a joiner is in works as in single player.** In a story dream of their own, or a place the host hasn't got, the host refused everything the joiner did (triggers, doors, chests), so the dream's way out could be stuck, and a refusal could remove the chest or trigger from the joiner's game. What a joiner does where only they are now stays in their game.
- **A chapter or the ending can't be lost.** When a joiner reached the end of a chapter while the host was in a dream or still loading, the host refused to start the next chapter, and the story couldn't go on. The host now waits until it can and starts it then, once. The joiner is told it's coming.
- **Every player gets their own part of a one-time event.** When one player set off an event that happens once, such as a trip down a hole or into a dream, the next player to reach it got nothing and could be stuck. Each player now gets their own part (the trip, its screen effects, journal entries, ordinary items); the shared part still happens once, and quest items and keys aren't handed out twice.
- **An event set off just after arriving somewhere isn't refused as too far away.** The host now knows where the joiner is before it checks.
- **A trade right after earning reputation isn't refused.** The new reputation reaches the host before the trade.
- **Story changes made while a player loads in now reach them** (a quest flag, an NPC's reputation). They used to be missed until changed again.
- **One-time hints are each player's own** (secondary attack, map, active skill, reload).
- **Loose objects stand where the host has them.** Things that each game makes for itself in a place the host loaded, such as a wardrobe, could settle a few steps apart in each game. A joiner's copy is now moved to the host's spot.
- A journal entry that two games had in slightly different forms is no longer sent back and forth between them for good.
- The game's pathfinding could throw an error when a place loaded while a door or window was changing its paths (single player can hit this too). The mod now lets those changes finish first.

How it was tested: with scripted players and the two-game map test only; nobody has played this build together yet. Each fix has a map test step: the prologue door in the real forest stage, with the host taking the key and the joiner unlocking it, then the other way round (`map:prologue`); a joiner alone in the church dream opening its wardrobe and door (`map:dreams`); the host in a dream of its own while the joiner sets off chapter 2, which then started once for both (`map:chapter`); a trade right after earning reputation (`map:trade`); two players each getting their part of a one-time trigger, and a trigger set off on arrival after a teleport (`map:later`); story changes just after a joiner's snapshot (`map:join`); a chair settling apart in the two games (`map:world`). The old behaviour was reproduced in a test run for the story catch-up and the journal loop; the key door was traced in the logs and the game code, and the dream, chapter, trigger and trade fixes come from reading the code. On the final build the full set ran: all 26 scripted-peer tests (ran clean or passed; Steam skipped, no Steam client) and all 38 map batteries passed, except one battery the test runner itself stopped at its two-hour limit; the runner was fixed and that battery passed again. Several test steps were fixed along the way that failed for reasons of their own (reading earlier tests' logs, walking through slow frames, a dog lured from too far).

## 0.25.1

*Protocol 27, plays with 0.25.0, 0.24.x and 0.23.x (no message changed).*

- Opening the port on the router now finds routers that it missed before. The mod looked for the router with a search sent to the whole local network, and on a computer whose firewall drops the router's replies to that kind of search (Fedora's default firewall does), no router was found and the port wasn't opened. The search now also goes straight to the router, whose reply gets through.

How it was tested: on one real router. Before the fix, a test script that searched the same way the mod did found no router; asked directly, the router answered. With the script, the router opened TCP and UDP port 33333 to this computer, someone on another internet connection reached it over TCP and got an answer once this computer's own firewall allowed the port, and stopping removed both forwards. The fixed mod's own search, run on its own outside the game, found the router. A hosted game opening its port on that router wasn't tried. `host-upnp` still passes against the scripted router. On the final build: capture, handshake, host-combat, client-enemies, client-chars and host-upnp passed.

## 0.25.0

*Protocol 27, plays with 0.24.x and 0.23.x (no message changed).*

- Hosting with Direct asks your router to open the port, so friends over the internet can join without you setting up port forwarding. The mod tries UPnP first, then NAT-PMP, opens the port for TCP (the game) and UDP (the server browser), keeps it open while you host and closes it again when you stop or quit. The Multiplayer window says what the router answered and shows your internet address. If the router refused, it says so (forward the port by hand then), and if the router's own internet address is a shared one (your provider's carrier-grade NAT, or a second router in front), it warns that opening the port won't be enough and suggests Steam or a VPN. It's on by default; untick *Open the port on my router* in the Multiplayer window, the Start Game window or Options (`[Host] OpenPort`) to leave the router alone. Steam hosting is unchanged and doesn't need it.

How it was tested: against a scripted router only (`host-upnp`, new). No real router has been tried yet, and the user didn't play it. The scripted router acted first as a UPnP router that takes only permanent forwards and sits behind carrier-grade NAT, then as a NAT-PMP router that hands out other ports than asked. Each time, hosting opened TCP and UDP 27720 to this computer, the warning or the router's ports showed up, and stopping removed both. Tests never contact the real router: a game started for testing only opens the port on the scripted one. The check box was looked at in the Options window's screenshots; the Multiplayer window's status line for a real router wasn't seen on screen. On the final build: capture, handshake, host-combat, client-enemies, client-chars, host-upnp, menu-entry and options passed.

## 0.24.0

*Protocol 27, plays with 0.23.x (no message changed).*

- Sort & Stack at the workbench pours gasoline cans into each other, as dropping one can on another does: you're left with full cans and at most one part-full one, and a can poured empty is gone. Lanterns are topped up from each other the same way, but an empty lantern is kept. Before, the cans were only put in order.
- Going back to the main menu from a host's game leaves it (your character is sent to the host first). The Multiplayer window used to still show that game, and you had to press Leave before joining it or another one again. While you're in someone's game, the window now also has *Find servers...* and *Connect to address...*, and joining another server leaves the one you're in.

How it was tested: with scripted peers only, nobody has played it together yet. `host-world` sorted gasoline cans at 10%, 15% and 20% into one can at 45% (lanterns weren't tried). The new `client-menu` test sends a joiner back to the main menu as the pause menu does: its character reached the host, the session was over at the main menu, and joining the same server again from the Multiplayer window sent a new Hello. `client-reload`, `client-chapter`, `client-away`, `client-drop`, `client-end` and `host-menu` still pass (reloads and chapter changes keep the connection). Switching straight from one server to another, and joining over Steam, weren't tried. On the final build: capture, handshake, host-combat, client-enemies, client-chars, host-world and client-menu passed.

## 0.23.1

*Protocol 27, plays with 0.23.0 (no message changed).*

- Walking into the Dry Meadow's markings (the painted signs where savages roam) no longer blacks out everyone else's screen. The place's darkness, tint, particles and whispers are now only for the players standing in it; before, one player walking in made every other player's screen black wherever they were, until the last player inside walked out.

How it was tested: with the two-game map test and the four-game test, nobody has played it together yet. New map tests walk players up to the markings at running speed, together and 1500 and 6000 behind, and in and out while others stay inside: each game is dark only while its own player is in it, and each game's view is watched every frame on the way. The four-game test also checked each player's torch, flashlight and lantern in every other game, and four lanterns lit together at the Old Woods hideout (every game's view stays its own). The village shortcut tests now screenshot and check the view after each trip, with the host going first and with a joiner going first; the view matched single player each time, so the "can't see the left side" report isn't reproduced yet. On the final build: map:view, map:whispers, map:lights, map:shortcut, map:shortcut2, the four-game view test and the release's scripted tests (capture, handshake, host-combat, client-enemies, client-chars) passed.

## 0.23.0

*Protocol 27, so everyone must update (it doesn't play with 0.22.x).*

Fixes from co-op play and a few requested features:

- Dead dogs no longer bark for joiners: a joiner's game no longer spawns its own enemies where the host runs the world, and the host's characters it keeps hidden stay silent.
- A savage that dies outside its own area no longer vanishes on the host's screen when that area is left. Joiners' corpses end the same way as the host's.
- Only one player drags a thing at a time. The host decides who; a second player lets go and sees who is moving it, and the thing comes to rest in the same place everywhere. A container change made of several slots is taken whole or refused whole, so dragging the same item at once can't copy it.
- Every stackable item stacks to 50 (a host setting, `StackSize`; 0 keeps the game's own sizes). Joiners use the host's size. No separate gasoline stacking bug was found.
- Gasoline poured into a generator or saw by several players adds up: a joiner sends how much it poured and the host adds it. What didn't fit goes back to the can.
- Repairing a broken door as a joiner no longer breaks it again right after: the host refused the repair and the rollback broke the door. A refused repair gives the wood and nails back. Furniture wasn't affected.
- Doors the story keeps shut (Piotrek's) open for joiners once the story unblocks them.
- Each workbench upgrade adds a row of six storage slots (54 at level 1, 96 at level 8), also in single player. Old saves get their rows when loaded.
- The hole shortcut to the village opens for everyone, including players who have never been to the village.
- The church dream's creature is the same one for everyone: the shared dream's enemies and events are run by the host.
- Joiners can talk to the Musician once he is under the tractor: world events fired by a character's animation are run by the host and sent to everyone.
- The ending is played together: everyone goes into the epilogue with the host, whoever moves on takes everyone, the burn starts in every game, players who die in the crawl watch the others, the final pan and the credits run for everyone. A world already in the epilogue can't be hosted.
- A joiner's pushes inside caves, interiors and the shared dream now move things for everyone; they used to be put back.
- The stove's skill reset button looks like the mod's other buttons.
- The prologue can be played together: a player who joins while the host is in the prologue's forest or the dream after it loads straight into that part, both games go on when either player finishes a part, and both wake at the hideout. Joining still waits during the intro film.
- Spending several saved-up skill points in a row no longer asks for a negative skill with every choice: each tier gives one, with its first skill, as when the levels come one at a time.
- Players no longer get stuck hearing a place's whispers: leaving a place is shared too, not only entering it.
- Near a player wearing a lantern, the others no longer see that player's shadows around themselves, and other players' lanterns and flashlights now light up as torches did.
- The morning trader leaves every player's game when the first player walks out of their hideout. The three wolf traders in chapter 1 each keep their own stock.
- At a chapter change every joiner keeps their own hotbar and starts the new chapter beside the host, instead of in a black void off the map.
- When a joiner kills a story NPC such as the chicken lady, her corpse stays for everyone and can be looted once.
- In the home dream every player gets out of the bedroom, and everyone goes into a level dream at the same moment.
- Leaving a chapter 2 world (back to the main menu, joining again, or loading another save) no longer throws an error for every banshee loaded in the swamp, which also left a banshee's agitated sound playing. This was the game's own teardown order, in the host's game as well as the joiners'.
- The black screen near a Dry Meadow landmark is not fixed yet. The cause is known: the game switches the player's view off while a ray straight down through the player hits certain objects. The mod now logs which object did it (a `[view]` line), so a log from a session where it happens will show the culprit.

How it was tested: with scripted players and the two-game map test; nobody has played this build together yet. On the final build, `all` (every scripted test) and the four-game chapter test passed, and the full map run passed 31 of its 33 parts; the other two (sweep and whispers) passed when run again. Each failed once on a story flag the host had and the joiner didn't, so that difference isn't explained yet. New map tests cover door repair, workbench rows, the village shortcut, the ending (the epilogue through the burn, both crawl deaths, the pan and the credits), the prologue played together, lanterns and flashlights, whispers when leaving a place, a story NPC's corpse, and the chapter change. The chapter change was also run with four games (the host and three joiners, each with its own hotbar). The map dreams, items, locations, npcs, skills and world tests were extended for the church dream, the home dream and the level dream's start, a story-blocked door, the corpse, the Musician, dragging, several skill points at once and the three chapter-1 traders' stock. The skill window was also clicked with a real mouse. Several things were tested on stand-ins rather than the real places: the Musician's event on another NPC, Piotrek's door on another blocked door, the chicken lady's corpse on a story NPC in the test area rather than in her house, and the black screen only around the hideout. Not tested: a real flamethrower burn at the end, the host leaving mid-epilogue, a generator running for long on a joiner, the morning trader's walk-out in a real morning, a banshee's agitated sound at a chapter-2 world's end, and a player leaving the game while inside a place with whispers (they may go on for that player).

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
