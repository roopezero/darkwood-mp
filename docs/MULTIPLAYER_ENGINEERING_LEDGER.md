# Multiplayer engineering ledger

The running record of the reliability sweep that started 2026-11-11 (after 0.20.0, protocol 25). Read this first in a
new session, so you don't repeat an investigation that's already done. Status words: **confirmed**, **likely**,
**suspected**, **not reproduced**, **not tested**, **blocked**.

## Starting point (reconnaissance, 2026-11-11)

- `main` = `origin/main` = f29a9a8 (0.20.0, protocol 25), working tree clean.
- Tests: `tools/run-test.sh all` (25 scripted-peer tests, ~12 min), `map` (two real games, 254 steps, ~17-20 min),
  `map4` (host + 3 client games). `MAP_SCENARIO=module` already runs one `tools/tests/map_scenario_*.py` part alone.
  The "40+ minutes" is `all` + the full `map`. `all` can't be combined with other test names (known).
- Known flaky part of the full `map`: an awake save dog (Dog#14) patrols near the test centre and sometimes kills
  the client at the rot wardrobe / egg station (test-world artifact, seen on 0.19.0 code too).
- Save transfer is already packed: `SaveOffer` packing 1 = raw Deflate (`World/WorldFiles.cs`), "about a quarter".
  So Task F is about measuring it, not adding compression.
- Nothing in the code, the git history or the GitHub issues mentions savages. In the game, the "savages" are the
  `Villager*` kinds (and possibly `Redneck*`); `map_scenario_enemies.py` fights each kind once.
- Open GitHub issues 16 (spectator can't look around), 17 (skills panel hover/marking, inventory opening with it),
  18 (dragged menus stick on slow mouse moves). Not part of this sweep's tasks; take them up if time is left.

## Workstreams

Game runs are serialised by one lock: `flock <scratchpad>/game.lock`. Each agent works in its own git worktree and
its own `WORK` folder. MsgType numbers handed out: A 222-228, B 243-248, C 91-99. The coordinator bumps
`Protocol.Version` once, at merge.

| Id | Task | Owner | Owns (files) | Status |
|---|---|---|---|---|
| A | Open UI windows kept up to date (workbench, containers, generator, saw, ...) | agent A, A2 | new `World/UiRefresh*.cs`, its map steps; minimal hooks in SlotShare/Containers/FuelSync | merged, map:ui PASS |
| B | Savage attack/animation sync, compared with dogs | agent B | `Combat/NetEnemies.cs` and enemy anim code, its map steps | merged; stuck loop not reproduced; copy catch-up fixed |
| C | Determinism audit | agent C | `docs/MULTIPLAYER_DETERMINISM_AUDIT.md`; fixes only outside A's and B's files | merged; knocked items sent to G |
| D | Test batteries: run any part alone, in any order, timed | agent D | `tools/run-test.sh`, `tools/tests/mapdriver.py`, battery list, `docs/MULTIPLAYER_TESTING_GUIDE.md` | merged (D, D2); verified in game |
| E | Entity and mechanic coverage audit | agent E | `docs/MULTIPLAYER_TEST_COVERAGE.md` (read-only otherwise) | merged |
| F | Save transfer size and speed | agent F | `docs/SAVE_TRANSFER_BENCHMARKS.md` | merged; Deflate kept |
| G | Knocked items come to rest apart | agent G | physics of dropped items, `map:knock`; MsgType 91-99 | merged; cause was the join snapshot, not knocks |

## Findings

- **E (coverage, from reading the code; no tests run):** "all entities tested" is false. Details and the top 10 gaps
  are in `docs/MULTIPLAYER_TEST_COVERAGE.md`.
  - The default map test runs 21 core kinds; 14 more only run with `EE_FULL=1`, and no run of that is on record.
  - 9 `CharacterType` kinds are never spawned by any test.
  - Enemy animation and facing are never compared in any test.
  - Joining mid-fight is untested, and so are fire and a night attack during a join.
  - The scripted tests mostly pass on "no exceptions": only host-progress and host-chars check results in code.
  - The forest spirit and shadow armour are tested with Dog stand-ins, not the real creatures.

- **C (determinism, read in code; probe not yet run in game):** most random outcomes that change the shared world
  are rolled on the host and sent: traders, disarms, night, weather, AI, spawns, loot, fire, locations. Containers
  are corrected when they're opened. Suspected risks, none confirmed:
  1. `Location.Awake` picks a random furniture set on each load and doesn't save the choice.
  2. Scripted-event steps that run in every game pick a random object, or the one closest to that game's own player.
  3. Explosions that aren't thrown items may place their leftovers differently in each game.
  4. `ObjRef.NearestPlaced` breaks ties with `<=`.
  The `map_scenario_dice` probe checks 1-3. Its first run failed before the mod loaded (`Loading in SingleInstance
  mode`, the game never reached the menu): most likely a game from the previous lock holder was still running
  (infrastructure, not the mod). The coordinator queued a rerun that first waits for no game to be running.
  - **Probe result (22:13, two real games, chapter 1 test world):** `map:dice` PASS, dumps SAME. `dice-sets`:
    0 loaded locations with more than one set, both games, same digest. `dice-events`: 419 steps that run in every
    game; 0 pick a random object, 0 pick the one nearest the own player; 0 kinds of explosion that isn't thrown and
    spawns things. So risks 1-3 are **not reproduced** in chapter 1. Chapter 2's world was not checked, and
    `runFunction` steps (e.g. `wolf_stealSister`, `transportAllItemsToCurrentHideout`) were not checked one by one.

- **Runner bug (confirmed, fixed in ccdaa75):** the new runner's `( run_body ) | tee` never ended. The shells that
  start the games kept the pipe open, so the map:dice run held the game lock 26 minutes after `map: PASS`.
- **Infrastructure (confirmed, 22:10):** every game launch in this session until 22:10 hung at startup: the Claude
  Code process has no `DISPLAY`. Signs: Player.log `Desktop is 0 x 0 @ 0 Hz`, game.out ends at `Loading in
  SingleInstance mode`, no `BepInEx/LogOutput.log`, the game at 100% CPU, "the game didn't reach its main menu".
  Fix: `export DISPLAY=:0 XAUTHORITY=/run/user/1000/xauth_*` (KDE/KWin Xwayland). So no in-game result before 22:10
  counts. Agent D was asked to make `tools/run-test.sh` find the display itself, or fail at once and say why.

- **F (save transfer, measured; two real games over loopback):**
  - Only sav.dat, savs.dat and savch.dat are sent (11.25 MB), as one Deflate Optimal stream: 2.57 MB, 23%.
  - Packing takes 111-122 ms in game on a worker thread; the host's game stops for 113 ms for the snapshot.
  - On loopback the transfer took 48 ms. Unpacking and checking took 236 ms (SHA-256 checked before anything is
    written). The world was ready 15 s after leaving the menu.
  - The game's own world load (about 13 s) dominates. Kept as it is: Fastest sends 11% more for 50 ms of
    worker-thread time. Base64-decoding the A* `graph` string would save 14% (a new packing 2), about 0.3 s at
    10 Mbit/s.
  - Not measured: the Steam relay or a real WAN.
  - Side finding: the client log never shows `[join] loading screen: the host's world is up` (not looked into).

- **A (UI windows, two real games; before/after with the fix switched off and on):**
  - Without the fix, 7 `map:ui` steps fail. Workbench recipe tints and upgrade state are drawn stale (a cached
    `CraftingRequirement.met` that only `Player.refreshRecipes` recomputes). The saw's "Fuel: N" label and Convert
    button are stale. A remote change closed any open container: trader restock, roll-back, corpse loot.
  - With the fix, every ui step passes (152 s).
  - Slot icons were already live (`InvSlot.createItem`/`removeItem`).
  - The fix is `World/UiRefresh.cs` (the game's own redraws, only while the window is open, with Replaying set) and
    `Containers.ApplyOpen` (load into the open window through SlotShare's per-slot rules).
  - Found, not fixed: `Item.searched` is set only in the opener's game; it only changes the "(searched)" hover text.
    It is a dump difference in `map:ui`, so `map:ui` is FAIL until it is shared or excluded deliberately.
  - Not tested: the fuel bar under the cursor, the upgrade button and item upgrade menu, ApplyOpen's roll-back and
    corpse paths, a trade window opened through dialogue, the tooltip over a changed slot, an item held on the
    cursor during a refresh.
  - `map:world` alone fails 3 alcohol steps: the module isn't self-contained (sent to D2).

- **B (savages, two real games):** the reported stuck-animation bug is **not reproduced** on 0.20.0.
  - The savages are the Villager kinds: one root animator each, like dogs. Their attack clips are WrapMode.Once and
    end through OnAniFinish -> startRecoveringFromAttack.
  - The difference from dogs: during recovery a villager shows IdleAggressive, a **1-frame** clip, for ~1.1 s. A
    dog shows an 18-frame Idle. This is the game's own design (single player too), and the likeliest thing a player
    would call "stuck".
  - The host doesn't stall when its target is a remote proxy: full Attack -> IdleAggressive -> Attack cycles.
  - Every savage kind, Redneck03, DogMutated (incl. Legs) and Dog matched the host clip-for-clip 96-100%: repeated
    attacks, the target walking off, the target switching to the host, three at once.
  - **Bug fixed:** `NetEnemies.ApplyAnim` corrected a copy that was off by more than 2 frames with `SetFrame`, which
    changes only the sprite, so the copy never caught up (a rabbit sat 3 frames ahead, 51% match). It now uses
    `PlayFromFrame`: 97-100%.
  - Not tested: real latency, night, water attacks, barricades. Villager_pistol has no prefab.

- **Knocked items (confirmed, two real games, from map:savage):** four of the save's dropped items near the fight
  moved 10-20 units in both games, but came to rest 2-5 units apart. A physics divergence; sent to agent G.

- **A2 (two real games):** `Item.searched` (and `Character.searched` on a corpse) is shared on open through the
  existing WorldAction `ItemSearched`. Clients may now send it as a request. The host refuses it for an object it
  doesn't have, a crate not yet picked open, something without an inventory, or out of reach. No byte change, but
  the meaning changed: bump the protocol at release. `map:ui` **PASS**, dumps SAME (145 s). `map:world`: only the
  3 known alcohol steps fail; dumps SAME otherwise (389 s).
  - Not tested: the host refusing a client's open (and its rollback), a real corpse open, a third player.

- **D2 (in game, the new runner):** `net` and `all` show no failures (all: 3 PASS, 21 RAN, steam SKIP, 1213 s).
  `map:far` and `map:sweep` pass in either order; one sweep FAIL (a client door push never sent) was
  intermittent, not the order. Runner fixes: a watchdog stops the run's games if run-test.sh is killed (tested only
  with a fake game); log reads wait out BepInEx's 2 s flush (host-reload's FAIL was a line not yet written); steam
  is SKIP without the Steam client. `maps` in one run passed 7 of 22. Causes found:
  - A kept client game joined while the test kit's dropped overflow was still rolling (dumps 2-16 apart): mapdriver
    now waits until the host's dropped items are at rest before the join (7-12 s). Real play can still have a
    client join while things roll; G's work covers that case.
  - **Real mod bug:** `Characters.Records()` kept the last world's players' characters in memory when a new world
    was loaded from the same folder; after map:status's permadeath, the next item's client was a dead spectator.
    The same happens to a real host who starts a new game in a used slot, and there the old records stayed on disk
    too. Fixed in 3883041: `Characters.Forget` on a new game, a deleted slot and a reset test world; a new game also
    deletes the slot's `darkwoodmp_players`.
  - Unexplained (ran after the leak or in a kept game): issues (`wb-use` no free slot), items2 (`i2-hurt gun` no
    damage), night (journal: 2 differences), npcs (`npc-died` living night traders 0), status (join slow, 33 s).

- **G (two real games):** the `dropped` difference in map:savage wasn't knocks. Dropped items are on layer 25,
  which bodies don't collide with: a host dog, the client's player and the host's player each walked through one,
  0.0 units moved in both games. The test kit's overflow is dropped with a random speed (100-250) and slides for a
  second or two; the host's snapshot for a joiner caught them mid-slide, and the rest position never reached the
  joiner (ObjMove is dropped while loading). Fix: the host notes every loose object in the frame the snapshot reads
  the world, and when the joiner asks for its character, sends it (ObjMove, last) each one that has come to rest
  elsewhere; one still moving is sent to everyone at rest. No wire change. `map:knock` FAIL without the resend
  (`Saved:21381` 83 units apart), PASS with it; map:savage PASS (559 s).
  - Not tested: something still moving when the joiner arrives, a second joiner, a reused snapshot, LiveWorld off,
    explosions during a join, furniture.
- **Three failures found on main and fixed (two real games):**
  - Door leaves after an open (a real sync bug, intermittent): `DoorPush.Look` found open doors only once a second
    and took their angle then as shared, so a leaf that swung open and stopped within that second was never sent,
    and each game's own swing left the leaves apart (client 41, host 65). A door this game opens is now watched at
    once from shut. map:sweep PASS; the client's log has `sent DoorLeaf ... pushed to -41`.
  - Test: in-game log reads (`MapScenarioIssues.LogLines`) didn't flush BepInEx's 2 s buffer: map:status failed
    `join-waited` on a quick join. Fixed; map:status PASS.
  - Test: map:far's dog went for the host's player and died in the test world's bear trap before the client could
    look for it. It's made tough now.
- **Characters leak fix proven:** map:status (permadeath in its POST) then map:sweep in one run, kept client:
  sweep PASS, no "died for good".

- **H (two real games):**
  - map:night, a real bug: a night object made before the client joined arrives as "made before" (not armed yet)
    and then the host's "armed now", within the second `NightObjects.Arm` waits to arm it again after its Start;
    both stayed queued and the list is walked backwards, so the stale "not armed" was applied last. Only the newest
    state is kept now. The race can't be forced; proof is the failing log (`armed True again` then `armed False
    again`) and two passing runs.
  - map:npcs, a test bug: the refused `Characters/NPC/Porter` spawn is items1's forged spawn, refused on purpose;
    `npc-died` read the whole kept host log. It reads only from its own module's mark now (stricter: an earlier
    module's `got NpcDied` can't count either). `map:items1 map:npcs map:night` twice: all PASS.

- **I (two real games): all three were test mistakes; no mod bug found.**
  - `i2-hurt gun`: the host stood behind a wall; a ray cast like `Player.spawnBullet`'s showed every shot hitting
    `Wall_upDown`. i2-fire now steps round the dog until the dog is the first thing on the line. FAIL before
    (482 s, the wall quoted), PASS twice after.
  - `i2-eats`: the game's `Character.checkForLure` skips lures while chasing a target unless `veryHungry`; the
    dog was chasing the client. The meat dog is made very hungry now. No controlled FAIL before (intermittent).
  - `sweep-loop-stop`: `Character.setBehaviour` stops the playing loop when the dog goes idle; the step read it a
    second after seeing it. It now waits for a loop that plays a full second. Also `TestWorld.SpawnEnemy` asks the
    game up to 5 times (`Could not get free spot around Player`, seen once). map:sweep PASS 3 times.

- **J (two real games), a real bug in `ObjRef.Resolve` (Kind.Placed):** the host refused a client's trap as gone
  (`refused ... the trap is gone; rolled back`), and the client's rollback left the chain trap its own trap had
  already spawned. Two causes: (a) one name table for every component type, refilled for one type per scan, so a
  Trigger asked for just after an EventTriggers wasn't found; (b) the nearest object of that name within 50 units
  was taken without a rescan, so a swamper trap made since the last scan was taken for its neighbour 31 units away.
  Fixed: a table per type, and a rescan when the match isn't on its spot (at most every 0.25 s per type; pinned
  objects such as dragged ones are found before this). Every earlier map:enemies run had 2-3 such refusals. After:
  `EE_ONLY=Swamper1,Villager` 3 PASS with 0 refusals, full map:enemies 2 PASS (675 s each).
  - Open: when the host rightly refuses a client's TrapFired, the rollback doesn't remove what that trap spawned
    in the client's game (two players on one trap would leave the same difference).
  - Villager never hurting the client: not reproduced in 9 runs (it always hit, 17). Diagnostics added (the
    villager's attack state, proxy hits or line-of-sight blocks, closest approach) and the runner now keeps the
    host's log beside the client's for every map item.

## Changes merged

- b9e566e: `docs/MULTIPLAYER_TEST_COVERAGE.md` (agent E).
- 27a75e3: `docs/MULTIPLAYER_DETERMINISM_AUDIT.md`, probe steps `dice-sets` / `dice-events` (agent C, test code only).
- 87ec492: test batteries (agent D): groups, `map:NAME`, `maps`, `full`, prerequisites, results table, cleanup, display guard. Static checks only; in-game verification given to agent D2.
- (merge after 87ec492): `docs/SAVE_TRANSFER_BENCHMARKS.md`, join timing log lines, `map:join` battery (agent F).
- (merge after ccdaa75): live UI windows, `UiRefresh.cs`, `Containers.ApplyOpen`, `map:ui` (agent A). No wire change.
- 4eef748: savage test `map:savage`, the ApplyAnim fix (agent B). No wire change. Regression run: map:enemies PASS.
- (merge after 4eef748): Item.searched shared on open (agent A2). Same bytes, new meaning.
- ed129f1: `Protocol.Version` 26 (for A2).
- (merge after ed129f1): runner watchdog, log flush wait, steam SKIP, rest wait before the join, map:world alone (agent D2).
- 3883041: Characters forgets the last world in the same folder.
- 8d7a9ae: loose objects resent to a joiner after its snapshot, `map:knock` (agent G). No wire change.
- 5dcc8f8: DoorPush watches a door this game opens at once.
- (merge, agent H): night objects keep the newest trap state; npc-died reads only its own log part.
- 2f2834b (merge, agent I): items2 line of fire and hungry dog, sweep loop wait, spawn retry. Test code only.
- (merge, agent J): ObjRef placed lookup per type and rescan off its spot; enemy fight diagnostics; host log kept per map item.
- a88b59f: in-game log reads flush first. 3caf891: map:far's dog can't die on the way.
- 19a040b (merge 95aef95, agent K): a client's trap waits for the host's yes before spawning what it spawns (a vine trap's vine, a mushroom's splat); a refused trap's bite and bear-trap hold are undone; `map:traprefuse` (forced refusals: trap gone, host set it off first). No wire change.
- map:far holds its dog on the spot (a bear trap kills a character outright); sv-compare counts only host clips inside the client's sampling window. Test code only.

## Test runs

| When | Commit | Command | Result | Time |
|---|---|---|---|---|
| 22:12 | 87ec492 | `MAP_SCENARIO=map_scenario_dice tools/run-test.sh map` | PASS, dumps SAME, no exceptions, saves unchanged | 54 s of test (+26 min hang, runner bug) |
| 23:44 | 4eef748 | `tools/run-test.sh map:enemies map:savage` | map:enemies PASS (B's fix doesn't regress the enemy fights); map:savage FAIL: every animator 99-100% match, no stuck clip, but `dropped` differs (4 knocked items 2-5 units apart) | 663 s + 556 s |
| ~21:55 (agent F) | 3ba43f7 | `MAP_SCENARIO=map_scenario_join tools/run-test.sh map` | PASS, dumps SAME | 56 s |
| (agent D2) | D2 branch | `net` | all RAN or PASS but host-reload (false FAIL: log flush, then fixed) and steam (no Steam client, now SKIP) | 295 s |
| (agent D2) | D2 branch | `all` | no FAIL: 3 PASS, 21 RAN, steam SKIP | 1213 s |
| (agent D2) | D2 branch | `map:far map:sweep`, `map:sweep map:far`, again | all PASS but one intermittent sweep FAIL | ~270 s each |
| (agent D2) | D2 branch | `maps` | 7 PASS, 15 FAIL (rolling items, Characters leak, 5 unexplained) | 4859 s |
| (agent D2) | D2 branch + rest wait | `map:dice map:dreams map:skills map:fixchecks` (kept client) | all PASS | 440 s |
| (agent G) | 5fdc556 | `map:knock` without / with the resend; `map:savage` | FAIL (dumps DIFFER) / PASS; savage PASS | 90 s, 90 s, 559 s |
| 03:20 | 3883041 + D2 | `map:status map:sweep map:world` | world PASS (alone: self-contained now); status FAIL (log flush); sweep FAIL (door leaf) | 648 s |
| 04:00 | + door, flush fixes | `map:status map:sweep map:far` | status PASS, sweep PASS (after the permadeath: leak fixed), far FAIL (dog died in a bear trap: test) | 385 s |
| 04:20 | 3caf891 | `maps` (23 items, kept client) | **19 PASS**. FAIL: items2 (dog didn't eat the soporific meat; gun did no damage), night (a placed trap armed on the host, not the client), npcs (no living night trader after a death; a client ObjSpawn of Characters/NPC/Porter refused), sweep (dog's aggressive sound loop didn't start; intermittent). Sent to agents H (night, npcs) and I (items2, sweep) | 4645 s |
| 06:10 | 2f2834b | `maps` (23 items, kept client) | **22 PASS**. FAIL: enemies (a swamper's `chainTrap_swamper` only in the client's game, health 0; a Villager's 2 hits never hurt the client, intermittent). Sent to agent J | 4628 s |
| 07:30 | 2f2834b | `all` (25 scripted-peer tests, protocol 26) | no FAIL: 3 PASS, 21 RAN (no FAIL line, time-out or exception), steam SKIP (no Steam client) | 1216 s |
| 09:00 | ce671e8 (J merged) | `maps` (23 items, kept client) | **23 PASS**, saves unchanged after each item. First run with every map battery passing together | 4611 s |
| (agent K) | 19a040b, fix off / on | `map:traprefuse`; then `map:world`, `map:enemies` | off: FAIL (`chainTrap_chain` only in the client's game, vines differ, client still held at health 70 after a refused bear trap). On: traprefuse PASS (3 compares SAME), world PASS, enemies PASS, saves unchanged | 112 s, 417 s, 679 s |
| 12:20 | 0ae3983 (K merged) | `maps` (24 items, kept client) | **22 PASS**, saves unchanged, traprefuse PASS. FAIL: far (the dog walked into the save's bear trap, which calls `die()`: health doesn't help; test), savage (a bystander dog's `RotateLeft_End` began on the host after the client's last sample; test's window) | 4714 s |
| 12:40 | + both test fixes | `map:far map:savage` | **both PASS**, saves unchanged | 735 s |

## Open risks and next actions

What is still open after the last runs, most important first.

1. **Nobody has played any of this.** Every result above is the two-game map test or scripted peers on one Linux machine over Direct. Steam joining, Windows and GOG have not been run. Next: a real two-player session on the unreleased build, then a release.
2. **Traps, after K's fix:** a client's allowed vine trap holds the client in its own game, but the host's vine is untargeted (`vines attached: host False client True`; older than the fix, its allowed step is out of `map:traprefuse` with a comment). A refused trap's status effects and a used-up careful step aren't given back; a trap refused as gone keeps a destroyed Item on the client until `Kind.Gone` removes it. Not tested: a trap set off by a thrown item, Steam.
3. **A Villager's hits on the client didn't land, once** (maps2, map:enemies). Not reproduced in three later runs; J added logs of each enemy's attacks and hits on the stand-in (`MapScenarioDogWatch`, `CombatPatches`), so the next failure says where the hit was lost.
4. **The host's refusal of `ItemSearched` is untested.** The happy path is covered by `map:ui`; a client marking a container searched out of reach has no test.
5. **Coverage gaps 2 to 10** in `docs/MULTIPLAYER_TEST_COVERAGE.md` (joining during a fight, a real night with a joiner, and the rest). Gap 1, enemy animation, is covered for savages by `map:savage` only.
6. **Determinism:** the `map:dice` probe found no location sets, no random or nearest-player targets in shared scripted steps and no spawning non-thrown explosions in the loaded test world; unloaded locations and later chapters are not covered, but the knocked-object physics (`map:knock`) only matches because the host resends positions, not because the games agree.
7. **GitHub issues #16 (Spectating), #17 (Skill menu), #18 (Menus)** are open and weren't worked on in this run.
8. **Run time:** `maps` takes about 77 minutes. `map:enemies` (671 s), `map:savage` (569 s) and `map:items2` (484 s) are most of it; splitting them would let a quick battery run before each commit.
