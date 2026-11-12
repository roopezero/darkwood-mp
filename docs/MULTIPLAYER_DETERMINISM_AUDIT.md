# Multiplayer determinism audit

Started 2026-11-11 on 0.20.0 (protocol 25), agent C of the reliability sweep.

DarkwoodMP doesn't run the game in lockstep. The host's world is the real one. Each client runs its own copy of
the game, predicts its own player's actions, and the host corrects it (CLAUDE.md, `World/Authority.cs`). So a
random roll or a clock only causes trouble when **two games each roll their own and the result changes the shared
world**. For every source of chance below, the question is who rolls it and whether the result is sent.

Classes:

- **a**: rolled on the host only (or only in the game of the player whose own thing it is), and the result is sent.
- **b**: cosmetic, or the local player's own: nobody else sees it, or it doesn't matter if it differs.
- **c**: every game rolls its own, and the roll changes shared state. This is a divergence risk.
- **d**: unknown. It depends on level data that isn't in the decompile, or it wasn't checked.

Evidence: everything here is **read** in the decompile (`/tmp/darkwood-decomp`) and the mod's code. Nothing is
verified in game by the table itself; the in-game probe (`map:dice`) checks three of the suspected risks, and its results
are under "In-game probe results" below.

## Inventory

| Source | Where (game) | Effect | Who decides now (mod) | Replicated? | Class | Risk |
|---|---|---|---|---|---|---|
| Container loot roll | `InventoryRandom.randomize` (at worldgen, when a location is first loaded, when a prefab is made while playing) | what crates and cupboards hold | Saved in the save for the generated world. Otherwise each game rolls its own, but a client asks the host what a container holds every time it opens one (`SlotShare` ContainerSlotsAsk/All), and the host settles each slot change | yes, when opened | a | low. If the host hasn't loaded that location yet (it answers ""), the client sees its own roll until its first change is refused, slot by slot |
| Enemy corpse loot | the enemy prefab's `InventoryRandom`; `Lure.cs:65` (gps_ear/finger/nose) | what a corpse holds | host. Client copies are frozen, and `NetEnemies` sends the corpse's contents | yes | a | |
| Trader stock | `NPC.randomizeTraderInv` | stock | host only (`TraderSync`) | yes | a | |
| Trap disarm, crate lockpick | `Player.cs:4287`, `Item.disarmChance` | trap gone or set off, crate open | host rolls (`WorldActions.cs:177,188`, `Authority.Result`) | yes | a | |
| Night scenario, night events, day random events, shadows' lights, dawn random objects, night mushrooms, weather and fog | `NightScenario.cs:116,182`, `NightScenarios.cs:240`, `RandomEvent.cs:207`, `Events`, `Player.cs:1217`, `WorldGenerator.spawnRandomObjects`, `Rain.cs:357-403` | world events | host only. A joiner's own rolls are skipped (`WorldEvents`, `NightObjects`) | yes | a | |
| Lightning flashes, rain drops | `Lightning.cs`, `Rain.cs:569-595` | look only | each game | no | b | |
| Enemy AI (dodge, running away, waypoints, attack choice, projectile spread, cut in half, banshee babies) | `Character.cs` (34 rolls) | enemies | host only. Client copies are frozen (`NetEnemies`) | states sent 20 times a second | a | |
| Enemy spawns, respawns, night spawns, worms, ShadowArmor | `CharacterSpawner`, `CharacterSpawnPoint`, `WorldChunk.spawnFreeRoamingCharacters`, `NightSpawns.cs:126` | which enemies exist | host. A client's own enemies are frozen and hidden | yes | a | |
| A player's damage to enemies | the player's own game (bullet spread at `Player.cs:6307`) | enemy health | the client predicts; the host applies the hit, capped by `NetEnemies.DamageCap` and rate-limited | yes | a | Whether the hit lands is decided in the shooter's game |
| Enemy damage to players | the host's enemy hits the player's proxy | player health | host, sent as `PlayerDamage` | yes | a | |
| Durability, rot, drain and recharge of items in containers | `Inventory.refreshItems` | durability | each game, by its own clock. `SlotShare` allows it as `Drift`, and the client takes the host's durability when it opens the container | partly | a/b | small drift until the container is opened |
| A player's own items wearing out; which items go into their death bag (`Player.cs:7589`) | the player's game | their own items | the player's own game. The bag is sent as `ObjSpawn` with its contents | yes | a | |
| Infection spread | `Infection.cs:73`, `Infection.spread` | ground that hurts | host. A client's infection never spreads (`InfectionSync`) | yes | a | |
| Fire | `Burn`, `Flame`, `Liquid` delays | damage, breaking | the host's fire counts. A client's fire only shows (`FireSync`) | yes | a | |
| What thrown items leave (a molotov's gasoline) | `Explodes.spawnObjects` (`Explodes.cs:173`) | gasoline puddles | host, for thrown items (`ThrownLeavesPatch`) | yes | a | |
| Non-thrown explosions that spawn objects | `Explodes.spawnObjects` on a barrel or canister that goes off in every game (`BreakSync` Explode) | puddles at random spots | **each game** | no | c/d | `ThrownLeavesPatch` (`World/ObjectActions.cs:308`) returns early for anything not thrown. The `dice-events` step logs whether any level object has a `spawnObject` |
| Location set variants | `Location.Awake` (`Location.cs:679-689`) keeps one of `sets` at random and destroys the rest. The choice isn't saved | which furniture, containers and doors a location has | **each game, every time it loads** | no | c/d | high if a set holds saveable things: save ids would then name objects only one game has. The `dice-sets` step compares the host's and the client's choices |
| Targets of scripted-event steps | `Location.GetObject.returnObjects` / `returnObject` (`Location.cs:329,359`) pick at random unless `getAll` or `getClosest` is set. `getClosestToPlayer` uses each game's own `Player.Instance`. `GameEvent.fire` falls back to the own player's location | the object a shared step shows, hides, moves, removes or animates | **each game**, for steps in the shared part (`ScriptedEvents`) | no | c/d | The `dice-events` step counts these steps in the loaded world |
| `runFunction` steps (SendMessage) in the shared part | e.g. `Location.wolfmanStealEvent` (`Location.cs:856`: 10 random items from the workbench) | containers, objects | **each game** | containers heal when opened | c/d | Containers fix themselves when opened. Other effects are unknown. `dice-events` lists the method names |
| Where a location stands and how it's turned | `OutsideLocations.spawnLocation`, `Core.getRandomHalfRotation` | where a location stands | host (`Locations`) | yes | a | |
| Level dreams | `Dreams.getPreset` (`Dreams.cs:279`) | which dream | host (`DreamSync`) | yes | a | |
| Chapter world | `ChapterPreset`, `WorldGenerator` | the world | the host builds it; joiners reload its save | yes | a | |
| Padlock codes, journal numbers | the game's `RandomNumberGenerator` (the numbers are saved in `dynamicSave.numS`) | codes | in the host's save | yes | a | |
| Y layering offsets | `RandomNumberGenerator.yPositionOffsetDictionary`, `Core.getYpos` | draw order | each game | no | b | `ObjRef` Placed ignores y |
| Dialogue line choice, skill offers | `DialogueWindow.cs:564-655`, `SkillsMenu.cs:362` | the player's own talk and skills | that player's game. The outcomes go through `FlagsSync` and `ScriptedEvents` | the outcomes | b | |
| Crafting | workbench recipes | items made | no rolls found | n/a | b | |
| Item modifiers | `InvItemClass.addRandomModifiers` | weapon modifiers | nothing in the decompile calls it | n/a | b | |
| `QuestRandomizer`, `UniqueItemSpawner`, `RandomSpawnArea`, `Shooter` | prototypes, or only used without random generation | | | | d | not seen in use in the real game |
| The mod's `FindObjectsOfType` lookups | `TraderSync`, `Hideouts`, `WorldActions.DyingNpc`, `FlagsSync.KillNpc`, `ObjRef.NearestPlaced` | which copy of an object is meant | the copy nearest a sent position, or the one with the save id; never "the first found" | | a | `ObjRef.NearestPlaced` (`World/ObjRef.cs:168`) keeps the last of equal distances (`<=`), so two objects with the same name within about a unit of the key's spot could resolve differently in two games |
| The mod's own `System.Random` | `HostIds`, `Containers`, `Hideouts`, `NightObjects`, `InfectionSync`, `EnemyLandings`, `WorldEvents` | new ids | the game that makes the thing; the id is sent | yes | a | |

## What protects the shared world

- **Containers:** a client takes the host's contents every time it opens one (`SlotShare` ContainerSlotsAsk), and the host settles every slot change. This heals any container that a game rolled or emptied by itself (a loot roll in a location, a shared `runFunction` such as the wolfman's theft), at the latest when someone opens it.
- **Enemies and everything they do** run on the host only (`NetEnemies`, frozen client copies).
- **The clock and everything that depends on it** (night scenario, events, weather, random objects, restocks) is the host's (`TimeSync`, `WorldEvents`, `TraderSync`).
- **Chance in a player's action** (trap disarm, lockpick) is rolled on the host and sent as `Authority.Result`.
- **Damage from fire, infection and explosions:** the host's copy counts (`FireSync`, `InfectionSync`, `BreakSync`, `ThrowSync`).
- **The world itself:** everyone loads the host's save, so worldgen, padlock codes and saved loot match.

## Fixes made

No change to game behaviour yet. Only the probe was added:

- `src/DarkwoodMP/World/MapScenarioDice.cs` adds two map steps:
  - `dice-sets`: a Harmony patch on `Location.Awake` notes which set each location kept. The reply is a count and an FNV digest, and every choice is logged as `[dice] set ...`.
  - `dice-events`: scans the loaded world's `GameEvents` for shared-part steps that pick a random object, or the object nearest the own player, out of 2 or more. It also lists the method names of shared `runFunction` steps, and non-thrown `Explodes` that spawn objects.
- `ScriptedEvents.IsHostStep`: a new one-line public method, used by the scan.
- `tools/tests/map_scenario_dice.py`: run it with `MAP_SCENARIO=map_scenario_dice tools/run-test.sh map`. The host's and the client's `dice-sets` digests must be the same.

Status: it has run in game alone at 87ec492 and as `map:dice` in the last three full `maps` runs (3caf891, 2f2834b, ce671e8), PASS each time.

## In-game probe results

From the `maps` run on ce671e8 (`/tmp/dwmp-work-main/maps3/map-dice/`, both games' logs), the same in the earlier runs:

- `dice-sets`: **0 locations with 2 or more sets** in either game (digest `811c9dc5`, FNV's empty value). The patch counts every `Location.Awake` after the mod loads, so in the test world, among the locations that were loaded, no location rolls a set. Locations the test never loaded (separate locations far from the hideout, later chapters) are not covered.
- `dice-events`: 419 shared-part steps, **0 pick a random object, 0 pick the one nearest the own player**. 28 `runFunction` steps of 17 kinds (attack, die, doctorRunAway, fakeDeathAni, lieDown, pause/resumeAnimation, prologue_endCutscene, removeAttachedGameObjects, removeCollider, removeDisplayingMessages, resume/stopEmission, rigidbodyFreezeAll, spawnWolfInCurrentHideout, transportAllItemsToCurrentHideout, wolf_stealSister). The ones that move items (`transportAllItemsToCurrentHideout`, `wolf_stealSister`, `spawnWolfInCurrentHideout`) run in each game; containers fix themselves when opened, other effects aren't checked.
- **0 kinds of non-thrown explosions that spawn objects** in the loaded world.

So risks 1 to 3 below were not seen in the test world. They stay listed because the probe only sees what was loaded.

## Remaining risks

1. **Location sets (`Location.Awake`)**, suspected high. If `dice-sets` shows different choices, the fix: the host sends its choices (name and position -> name of the set it kept) before the joiner loads, as a new message in 91-99 next to the save offer, and with `LocationSlot` for locations loaded later. A `Location.Awake` prefix on the client destroys the other sets itself and leaves only the host's choice in `sets`, so the game's own code keeps that one.
2. **Random targets of shared scripted-event steps**, suspected. If `dice-events` finds any, the fix is in `ScriptedEvents`: the host notes each choice made by `returnObjects`/`returnObject` while a shared part runs, and sends it with the step as ObjRefs. The joiner uses the host's choice (its step waits for it, or runs once it arrives). `getClosestToPlayer` should mean the player who set the event off, which is that player's proxy on the host.
3. **Non-thrown explosions that spawn objects:** if any exist, extend `ThrownLeavesPatch` (`World/ObjectActions.cs:308`) from thrown items to every `Explodes` that the host also sets off.
4. **A location the host hasn't loaded yet:** the client's own loot roll shows until its first change is refused. It heals itself after one refused take.
5. **`ObjRef.NearestPlaced` ties** (`<=`): see the table.
6. **Not audited line by line:** `Dictionary` and `HashSet` order where it feeds a message. Sets keyed by instance id have a different order in each game, but no decision was found that takes "the first" item of one.

## Floating point and platforms

- Positions are replicated as f32 (exact), not simulated. Nothing is re-simulated from inputs. So if Windows and Linux Mono get different float results, that changes only each game's own physics, which `DragSync` and the enemy states then overwrite with the host's.
- The host's checks on floats a client sends have wide margins: `MoveCheck.MaxSpeed` is 2000 units a second (about 4 times a run) over 0.5 s windows, and `Teleport` is 3000. `Authority.InReach` uses reach distances. `ObjRef.PlacedSlack` is 50, and positions are rounded to whole units for the key. None of these is fragile at float precision. When `PlayerState` packets arrive bunched up on the network, the player can look faster than they are; the margin covers ordinary jitter. This was not measured under heavy packet loss.
- The map test's `mapcompare.py` allows positions within 1.5 (60 for enemies), the hour within 0.26, durability within 0.051 of its maximum and fuel within 1.01. Everything else must be exact.
- Numbers in strings: `Authority.Result` uses `InvariantCulture`. JSON (Newtonsoft) doesn't depend on the culture. The `int.Parse` calls in `FlagsSync` are safe for plain integers. No culture-dependent float parsing was found in shared code.

## Physics: knocked objects

- **Who moves loose things.** Every loose object (an `Item` with a rigidbody: furniture, placed objects, dropped
  items) is watched by `World/DragSync.cs` in every game. The host sends where one came to rest (ObjMove, last),
  whatever moved it. A client sends only what its own player pushed and puts its copy of anything else back, so the
  host's rest position is the last word. Drags send their end, and again once settled.
- **Dropped items don't get knocked.** They are on layer 25, which players' and enemies' bodies don't collide with.
  In `map:knock` a dog driven through one (on the host, with its frozen copy on the client), the client's player and
  the host's player each went straight through a dropped item and none of them moved (0.0 units in both games). What
  moves them is the game's drop itself: `Player.spawnDroppedInvItemm` gives a new one a random velocity of 100-250 at
  a random safe angle (drag 3, so it slides for a second or two), and explosions (`Explodes`: AddExplosionForce).
- **The map:savage finding was a join snapshot, not the fight.** The test world hands the host its kit, and what
  doesn't fit is dropped (chainTrap, bandages, wire, rope by the centre). When the client game was kept between
  battery items it joined at once, and the host read its world for the snapshot while those were still sliding. The
  joiner loaded them mid-slide and kept them there. Where the host's came to rest was either noted as the start
  (sharing began after they stopped: nothing moved, nothing sent) or sent while the joiner was still loading (ObjMove
  is taken only in the world). A full map:savage with kn-info before and after showed the four items didn't move at
  all during the fights, and their starting spots differ from run to run (the drop is random).
- **Fix:** the host notes where every loose object is in the frame it reads the world for a snapshot
  (`DragSync.NoteSnapshot`, called from `WorldShare.SaveSnapshot`). When the joiner asks for its character (it's in
  the world then: `Characters.OnRequest`), `DragSync.JoinerInWorld` sends that joiner (ObjMove, last) each one that
  is at rest away from its snapshot position (more than 0.5 units or 2 degrees). One still moving is noted at its
  snapshot position, so `WatchPushes` sends it to everyone once at rest. With LiveWorld off, the world is the last save
  and nothing is known of where things were in it: nothing is sent.
- **Test:** `tools/run-test.sh map:knock`. Before the join, `kn-slide` drops a rope the game's way and keeps it
  sliding until the snapshot is read, then pushes it once more. Then the enemy and player pushes, and a `compare` of
  `dropped` (mapcompare.py's position tolerance: 1.5 units).
- **Not covered:** an explosion's push during a joiner's load is covered by the same resend; a push during play is
  covered by WatchPushes. Neither was tested here with an explosion.
