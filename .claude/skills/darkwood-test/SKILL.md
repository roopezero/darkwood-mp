---
name: darkwood-test
description: Run DarkwoodMP's in-game tests against the real game with scripted Python peers (host and client roles), safely on a copy of the user's save. Use after any change to game-facing mod code, or when asked to test or verify the mod.
---

# Testing DarkwoodMP in the real game

The `map` test runs two real games (below). The others pair the real game with a Python script playing the other player (`tools/tests/*.py`). `tools/run-test.sh` runs a whole list of tests in **one game**:
1. Backs up the user's saves, and starts the game once with `--dwmp-test-control 27799` (see `TestControl.cs`).
1. Before each test, sends it the title (shown along the top of the screen: "Test (X/Y): name - what it checks"), `reset` (leave the session and world, back to the main menu) and the test's `--dwmp-*` arguments (`tools/tests/ctl.py`). Host tests play a **fresh copy** of profile 1 (`--dwmp-sandbox`); client tests join a scripted host, and those saves go to the mod's cache.
1. Runs the script and prints what it saw.
1. Reports the new exceptions in `Player.log` (since the test began), and fails if the user's saves changed. The game is started again if it died.

Don't edit `tools/run-test.sh` while it runs: bash reads the script as it goes.

## Running

```
tools/run-test.sh --list                         # every test, group and map battery
tools/run-test.sh ITEM...                        # tests, groups and batteries, mixed, any order
tools/run-test.sh map:enemies map:world host-world
tools/run-test.sh net combat                     # groups: net combat world chars story menus authority
tools/run-test.sh all                            # the 25 scripted-peer tests;  full = all + map
tools/run-test.sh maps                           # every map battery, each in its own fresh world
tools/run-test.sh --plan client-world            # what would run (prerequisites added), without running
```

Run the smallest set that covers the change: a map battery (`map:NAME` = `tools/tests/map_scenario_NAME.py` alone, from a fresh test world and a fresh client join) instead of the whole `map`, a group instead of `all`. Missing inputs are added (`capture` before client tests, `host-world` before `client-world`). The run ends with a table: each item's PASS / RAN / FAIL, seconds, first failure, its log (`$WORK/logs/ITEM.out`) and the command that reruns it alone. PASS means the test has a verdict of its own; RAN means nothing failed, but its printed lines are the result. `docs/MULTIPLAYER_TESTING_GUIDE.md` has the batteries, isolation, cleanup and measured timings. A new `map_scenario_NAME.py` is a battery by itself; `IN_MAPS = False` keeps it out of `maps`.

A test takes about 20 seconds to a minute and a half. The runner waits for the game's log lines (`Main menu ready`, `World ready`, a hook's "done") rather than fixed times, and the scripted players stop as soon as what they're there for has happened: `combattest.py` takes a list of events to wait for (`verdict,bitten,blood,sound`, `reload`), and `bg_until PATTERN SECS CMD` in the runner stops a scripted player (SIGTERM, which still prints its summary) once the game logs PATTERN. Keep new tests that way: wait on an event, with a fixed time only as the limit. Run them in the background with a generous timeout (10 minutes for several). Never run two at once: they share the game.

| Test | Role | What it checks |
|---|---|---|
| `map` | host + client, both real | two games: the host (test world, `--dwmp-host`) and a client from `~/.cache/darkwood-mp/client-game` (`tools/setup-client-game.sh`; own HOME, no Steam) joined over Direct. `tools/tests/mapdriver.py` runs `map_scenario*.py` (steps in `World/MapScenario*.cs`, sent with the test-control `do` command), then both `dump` their shared world and `mapcompare.py` diffs them. About 8 minutes. Not part of `all`; run it as well |
| `map4` | host + 3 (or 4) clients, all real | the host and client games c1..c3 (the mod takes 5 players, host included); `MAP_CLIENTS=4` adds c4, but five games need ~6 GB of free RAM. Client N: `tools/setup-client-game.sh N` (`client-game`, `client-game-2..4`, homes `client-home`, `client-home-2..4`, names Client1..4, controls on 27798..27795). They join one after another over Direct, in small windows (clients at the lowest quality, 30 fps). Default scenario `tools/tests/map4_scenario.py`: each client does a door, take and drop in the test area, then c3 (and c4) go to spots of their own 6000+ away (`far-spot`: switched off in the other clients' games; the host's switches the world on around every player) and c1 and c2 to another, and every game's dump around each far player is compared with the host's. Results in `$WORK/map4/` (`host.json`, `c1.json`.., `cN-LogOutput.log`, `memory.log`: RSS every 10 s). Each game takes about 1.2 GB. Not part of `all` |
| `handshake` | host | refusals (password, version, game) with their reasons; a valid join is offered the world |
| `capture` | host | downloads the host's world into `$WORK/world.pkl`. **Run before any client test.** |
| `host-combat` | host | spawned `DogMutated` plus a test dummy plus a scripted fighter: threat decisions, bites reaching the fighter (`PlayerDamage`), its hit and noise reaching the host, enemy spawn/state/sound/blood; `--dwmp-test-night` sends a night worm and a night attacker at the dummy's proxy (`[night test]` lines: its hideout, where each spawned); with `--dwmp-test-blast`, an enemy bomb, damage aura and flame reaching the fighter (`blast`, `aura`, `flame`) and the bomb's `EnemyExplosion` (`boom`); player fx both ways (`--dwmp-test-fx`: the host's player bleeds and burns; the fighter's sounds, shot, blood and burning shown on its puppet) |
| `host-world` | host | the scripted world test (`--dwmp-test-world`): door, container loot, drop, flags/NPC reputation, trap, lamp, trader restock, NPC death, container lock, dragging a stool, a thrown molotov, a door on fire, the workbench's storage slot by slot (the observer's change accepted, a late one refused) and its level, a door lockpicked (DoorUnlock), a padlock's code entered (PadlockUnlock), a locked crate picked open (ItemSearched). Records `$WORK/world_rec.pkl` |
| `host-chars` | host | a new character, then the same player gets theirs back after the host saved |
| `host-death` | host | the host dies with a player in: death bag shared, enemies not respawned, respawn at home |
| `host-elsewhere` | host | the host is away as in a separate location (stand-in grid): enemies near the dummy keep running, its puppet is hidden; a knocked object near the host is sent where it came to rest (ObjMove); the observer asks where `outside_bunker_underground_part2_01` stands (`LocationAsk`), gets the host's spot (`LocationSlot`) and the host loads it there (`[locations] loaded ... (promised ...)`: the two must match) |
| `host-reload` | host | the host reloads its world: the player gets `WorldReload` |
| `host-menu` | host | the host goes back to the main menu: the player gets `HostAway` and leaves, the server stops |
| `host-chapter` | host | `storytest.py` asks for chapter 2 (`StoryRequest`) from a stand-in chapter event (`--dwmp-test-chapter`): refused from 4000 away, for chapter 3, from an event the host hasn't got while not away, and for the epilogue; allowed beside it. The host really builds chapter 2 in the sandbox: `ChapterStart`, `WorldReload`, the new world offered, the character back as kind 3 |
| `host-end` | host | `--dwmp-test-end 5`: the host starts the epilogue; the player gets `GameEnd` and the server stops |
| `host-authority` | host | `authtest.py` asks for things the host must allow or refuse: a door from beside it and from 4000 away, impossible door actions, a container from too far, a trap disarm (the host's roll, `result 1/0`) and again, a lockpick on a door with no lock, a padlock from too far, with a wrong code and with its code, a locked crate picked from too far and from beside it (`result 1`), hits a second from beside the dog (30 with a pump-action shotgun and 28 with a submachine gun in one second all allowed, 30 with bare hands: 6 refused, NetEnemies.MaxHits), a 99999 bare-hands hit (capped to 40), a hit on no enemy, a sustained speed hack (`PlayerCorrection`) and a normal walk (none) |
| `menu-entry` | main menu, then host | the menu list takes a click (Join Game in the main menu, Multiplayer in the pause menu) and opens the panel; screenshots of both panels |
| `shots` | host | screenshots into `$WORK/shots/` (view them with Read): the world, the pause menu over it (with a scripted player in), its Back to main menu question, the co-op panel, the world after. Logs the time scale and clock while the pause menu is open (the world must go on). For the main menu alone, launch with `--dwmp-shots DIR` and no world (the menu, its Quit question, Join Game, the Multiplayer window, the server browser and the connect dialog) |
| `drag` | main menu | `--dwmp-test-drag` (`DragTest.cs`): a test window moved by its title bar and resized by its grip with made-up mouse events (200 steps of 1 px, 300 of 0.3 px, a few of 30 to 40 px), one event a frame through `UiWindow.OnGUI`; each must move or grow exactly as far as the mouse did (`[drag] PASS/FAIL` lines; issue #18). The press on the title bar is started with `UiWindow.BeginDrag`, since a made-up event put in place inside a window function arrives as an Ignore event. Windows follow the mouse from `Input` each frame (Unity sends no MouseDrag for slow real movements); the test feeds its made-up mouse through `UiWindow.TestMouse`. With a real mouse: launch with `--dwmp-test-drag-real` (the window at 200,200 for two minutes, `[drag-real]` lines each held frame: mouse, window, offset) and move the pointer with `YDOTOOL_SOCKET=/tmp/.ydotool_socket ydotool` (relative moves; the pointer accelerates, so aim from the logged position) |
| `options` | main menu, then host | `--dwmp-test-options DIR` (`OptionsTest.cs`): the Options window from the menu's list in both menus; a music volume applied and found in the game's store (PlayerPrefs and the prefs file), a key listened for and cancelled with Escape, a free key put on an action and read back from Rewired's store; screenshots of every tab, an open list, a key being listened for and both questions at 1920x1080 and 1280x720 into `$WORK/options/`. These are the user's real game settings: the test puts every setting in `~/.config/unity3d/Acid Wizard Studio/Darkwood/prefs` back and compares the file (`[options] PASS/FAIL` lines) |
| `steam` | host | creates a Steam lobby with the user's account and leaves (joining needs a second account) |
| `client-world` | client | replays `world_rec.pkl`: everything found and applied (needs `host-world`) |
| `client-enemies` | client | the host's enemies shown, moved and despawned; the client's own frozen; a killed enemy's corpse settled and searchable (the log says `its corpse can be searched here`, not `couldn't be made searchable`) |
| `client-chars` | client | new character (empty bags), then a changed one applied (position, health, items, fewer slots) |
| `client-reload` | client | `WorldReload` makes the game rejoin |
| `client-chapter` | client | `ChapterStart` from the scripted host: the game sends its character and waits; the `WorldReload` after it makes it rejoin |
| `client-end` | client | `GameEnd` (the end screen): the game leaves, shows it, then the credits and the main menu (`--dwmp-test-quick-end` skips the credits) |
| `client-epilogue` | client | `GameEnd` (the epilogue): the game leaves and starts the epilogue (skipped after 5 s in the test), then the credits and the main menu |
| `client-away` | client | `HostAway`: the game leaves and goes back to the main menu |
| `client-drop` | client | the host's connection just closes: the game goes back to the main menu |
| `client-authority` | client | `--dwmp-test-client-actions`: our player uses a rope and opens a door; the scripted host refuses it (`ObjState` + `Verdict`); the door is closed again and the rope is back |

Playing alone (not in the runner): launch by hand with `--dwmp-continue 1 --dwmp-sandbox DIR --dwmp-solo-test SHOTS` (a fresh copy of the saves in DIR, no `--dwmp-host`). It screenshots the world and the pause menu over it, logs the game's keys and skill tiers, gives the player two levels, opens the Skills panel with its key (without the backpack) and spends a point there, screenshots it with the panel's test mouse on a learned icon's edge (`solo-skills-hover-edge.png`) and logs a `[solo] real-mouse X Y` line for a tool outside the game to move the real mouse there (xdotool can't warp the pointer under KWin's Xwayland), opens the stove's menu and resets outside the morning (refused) and in it (allowed once, then refused), sweeps the test mouse over every skill (`[skills] hover sweep: ... PASS nothing moved`: the window, its scrollbar and the grid must not move), then fills the bar for a level without the game's skill choice (`[solo]` lines in the log; screenshots `solo-skills*.png`, `solo-stove*.png`). Skill points with a real client: `MAP_SCENARIO=map_scenario_skills tools/run-test.sh map`; the host's checks with a scripted player: `host-progress`. Check your saves' hashes yourself before and after.

The loading screen: run any host or client test with `LOAD_SHOTS=DIR` (e.g. `LOAD_SHOTS=/tmp/ls tools/run-test.sh capture client-world`) for a screenshot of each step the line above the bar shows, into `DIR/host` and `DIR/client` (`--dwmp-load-shots DIR`). `DWMP_ARGS="--dwmp-window 1280x720"` plays those tests in a window of that size; the next test without it puts the size back. The log has `[loading] screen WxH: bar centre y=..., message y=..., hint y=...` (screen pixels from the bottom).

The test world isn't in the runner. To check it, launch the game by hand with `--dwmp-testworld --dwmp-reset --dwmp-shots DIR`: it copies your saves into `BepInEx/cache/DarkwoodMP/testworld` (your real saves are only read), loads that copy, builds the test area and screenshots it into DIR. The log says `Test world: built N of 19 test stations`. `--dwmp-sandbox` isn't needed for this, since the test world plays its copy through the save redirect.

A full pass is `tools/run-test.sh full` (`all`, 25 tests, about 12 minutes, then `map`). Batteries: `docs/MULTIPLAYER_TESTING_GUIDE.md`.

## Reading results

- Every test ends with `no exceptions` and `your saves: unchanged`. Anything else is a failure: read the printed exception (it comes from `Player.log`).
- The mod's log for the last game run is `<game>/BepInEx/LogOutput.log`. `--dwmp-debug-world` and `--dwmp-debug-threat` (set by the runner where useful) log every shared-world message and threat decision.
- Scripted peers that send an outdated message format cause `EndOfStreamException`s on the host. If you changed a message's bytes, update the matching script in `tools/tests/` too. The protocol version itself is read from `Net/Packets.cs` automatically.
- Timing matters. The first `PlayerState` of a joining client comes from a loading position (x = 75000) before the world is ready, so anything that waits on the client's world should wait for `WorldShare.WorldReady`.

## The map test

- Build first: `dotnet build` deploys to both game folders. Results go to `$WORK/map/` (`host.json`, `client.json`, `client-LogOutput.log`); the host's log is `<game>/BepInEx/LogOutput.log`.
- A step: `MapScenario.Add("name", (args, r) => Step(args, r), timeout)`; wrap the player's action in `AsPlayer(...)`, `yield return Walk(pos)` first so the host's reach checks pass, `r.Fail(...)` when it didn't happen locally. Then list it in `tools/tests/map_scenario_world.py` or `_issues.py` as `(who, step, args...)`. Run `heal` after anything that hurts the player: a death changes story flags and fails the comparison.
- **Who:** `host`, `c1`..`c4` (`client` is `c1`, so the two-game scenario runs in `map4` too), or `all-clients` (each in turn). `("all", "compare", WHO, SECTIONS)` (or `"both"`) dumps every game around WHO's player and compares each client with the host.
- **A subset:** `MAP_SCENARIO=module tools/run-test.sh map` (or `map4`) runs `tools/tests/module.py` instead (`STEPS`, and optionally `PRE_JOIN`, `POST`, `REQUIRE`, `SETTLE`), e.g. `MAP_SCENARIO=map_scenario_world` for the world steps alone (without `REQUIRE`, mapcompare's own required sections apply). A quick custom one: copy `map4_scenario.py` to a new module with only the steps you need.
- **Far from the host:** `far-spot MIN INDEX` walks to the INDEXth house (a plain door with a container) MIN or more from the test area and makes it the centre that later steps pick from; `centre-me` makes our player's spot the centre. WorldGrid switches on only about (screen width + 500) / zoom around a player (the host's game around every player: `SharedCullingPatch`), so 6000 away is switched off in the other clients' games. An enemy only in the host's dump 3300 or more from the client's player is not a difference (the host sends each client the enemies within `NetEnemies.RelevantRange`, 3000).
- A dump section: `StateDump.AddSection("name", ctx => ...)`, using `StateDump.All<T>()` (inactive objects too: each game switches off different things far from its player).
- A difference in the dumps is a sync bug until shown otherwise; don't loosen `mapcompare.py` to get a pass.

## Adding a test

- **Host side:** add a step to `World/WorldTest.cs` (it runs once a player has joined) or a `--dwmp-test-*` argument in `Plugin.cs`. Then decode the new message type in `tools/tests/worldobs.py` (or `combattest.py`), and add a case to `tools/run-test.sh`.
- **Client side:** teach `worldtest.py serve` to send it (or replay it from a recording), and check the game's `[world]` debug lines.
- Remember the scripts are a second implementation of the protocol, so keep them in step with `Net/Packets.cs`.
