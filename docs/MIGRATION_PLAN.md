# Migration plan

How the code moves from what it is today to the layout in
[ARCHITECTURE.md](ARCHITECTURE.md). That document says where the code is
going; this one says in what order, how each step is done, and how each step
is checked.

**Status:** Phases 0 to 3 are done, apart from their in-game gates, which
move to the final run, and the hand tests of a test build on Windows and on
GOG, which are still to do. The formatting pass over all the code is done, and
the style rules are errors in every project. The rules of Phases 4 to 8 are
written in the core with their unit tests; the plugin calls them as each
subsystem moves. Phase 4 is under way. See [Changes made
while carrying out the plan](#changes-made-while-carrying-out-the-plan). Tick
the boxes below as steps are merged, and delete this file when Phase 8 is
finished.

## Where the code is today

Measured on the code at release 0.28.0 (protocol 29). The static-state numbers
come from a script that reads the source, so treat them as close, not exact.

| What | Today |
|---|---|
| C# in one project | 163 files, 53,028 lines |
| In-game map test steps (`MapScenario*`) | 40 files, 13,766 lines |
| Static mutable fields and collections, outside the tests | about 460 |
| `OnSessionEnded` cleanup handlers | 40 |
| Uses of `Session.Instance` | 138, in 59 files |
| `Authority.Refuse` calls | 120, in 19 files |
| Files over 600 lines, not counting `MapScenario*` | 15 |
| Lines over 80 columns | about 14,500 |
| Unit tests | none |

## Ground rules

These hold for every step.

1. **Nothing changes for players.** A migration step moves code and doesn't
   change what it does. A bug found on the way is fixed in its own commit,
   with a test that fails before the fix.
2. **`Protocol.Version` stays at 29.** The scripted Python peers aren't
   changed during the migration, so they prove the bytes are still the same.
3. **Refusal texts stay word for word.** They travel in `Verdict`, and
   `tools/tests/authtest.py` reads them.
4. **`main` always builds and plays.** Each step is its own branch, merged
   when its test gate passes. Old and new forms live side by side until the
   last subsystem has moved.
5. **One kind of change per commit:** rules, then state, then formatting. A
   regression can then be traced to one of the three, and formatting commits
   go in `.git-blame-ignore-revs`.
6. **Feature work can continue.** New code follows `ARCHITECTURE.md` from the
   start. A change to a subsystem that hasn't moved yet follows that
   subsystem's old form, unless the subsystem is moved first.

7. **Fixes for players come from the last release.** Nothing from the
   migration is released until Phase 8, so an urgent fix goes on a branch from
   the `v0.28.0` tag and is then brought into `main`.

## Decisions

The code survey found three things `ARCHITECTURE.md` didn't settle. All three
are decided; Phase 0 writes the first two into `ARCHITECTURE.md`.

### 1. State that doesn't live as long as a session

`ARCHITECTURE.md` puts every subsystem in a `CoopSession`, created when a
session starts and dropped when it ends. The code has three lifetimes, though:

- **The process:** settings, windows, the `Session` object and its backends.
  These are needed in the main menu, when there's no session.
- **The session:** who is connected, requests waiting for an answer.
- **The world:** what's tied to the loaded scene. A chapter change loads a new
  world while the session goes on. Hosting from the pause menu does the
  opposite: the session starts in a world that's already loaded.

**Decided:** three homes. Process state goes on the `Plugin` instance,
session state on `CoopSession`, and world state on a `WorldScope` that
`CoopSession` replaces at each world load. Anything gathered before hosting
starts is either process state or is rebuilt by scanning the world when the
session starts.

### 2. Refusals that carry a detail

`ARCHITECTURE.md` gives a verdict a named reason and nothing else, but some of
today's texts include a value: `nobody has the key (...)` and
`a hit of 1500 is more than any weapon does`.

**Decided:** `Verdict.Refuse(Refusal.NoKey, detail)`, with an optional
detail string that `Refusals.Text` puts into the sentence. Tests still compare
the named reason.

### 3. When to release

**Decided:** one release, after Phase 8. The in-game tests only run the Linux
Steam build, so a test build is tried by hand on Windows and on GOG at the end
of Phase 1 and of Phase 3. Those builds aren't published. Without them, a
loading problem with the second DLL would only show up at the very end.

## The phases

Lines are today's count of the code each step moves.

| Phase | What | Lines |
|---|---|---:|
| 0 | Baseline, doc fixes | none |
| 1 | New projects, build, deploy, packaging | none moved |
| 2 | Map test steps into their own project | 13,766 |
| 3 | Network into the core, loopback tests | 2,539 |
| 4 | `CoopSession`, then what every subsystem uses | 1,031 |
| 5 | Shared world objects | 8,076 |
| 6 | Players and combat | 7,295 |
| 7 | Story, places, progress, saves | 10,718 |
| 8 | Menus, remaining test code, `Plugin.cs`, strict build, the release | 9,603 |

The order follows the dependencies. Almost everything uses the network, then
`Authority`, `ObjRef` and the replay guards, so those go first. Saves and
`Plugin.cs` use almost everything, so they go last.

## Moving one subsystem

Phases 4 to 7 repeat these steps for each subsystem.

1. **List what's there:** its statics and how long each must live, its
   messages, its patches, and the decisions buried in them.
2. **Write the rules.** Copy each decision into a `...Rules` class in the core
   with its `...Facts`, keeping limits and refusal texts as they are. Write
   the unit tests from what the code does today.
3. **Move the message bytes.** Put the subsystem's message reading and writing
   in the core, with a test of the exact bytes.
4. **Call the rules from the shell.** Add one `FactsOf(...)` per game type.
   Commit, and run the step's test gate.
5. **Move the state.** Make the subsystem an instance owned by `CoopSession`
   (or `WorldScope`), update its callers and the map tests, and delete its
   `OnSessionEnded` cleanup. Commit.
6. **Thin the patches.** Each patch hands its arguments to one shell method. A
   patch must do nothing when `Coop.Current` is null, so playing alone is
   unaffected.
7. **Tidy the files.** Use the feature folders, keep files to about 600 lines,
   apply the formatting rules, and turn the style warnings into errors for
   that folder in `.editorconfig`. Commit.
8. **Run the test gate again** and tick the box here.

A subsystem is done when no static state is left in it, every decision has a
unit test, its folder builds with style errors on, and its gate passes.

## Phase 0: baseline

- [x] Run `tools/run-test.sh full` on the current code and keep the results,
      so an old failure isn't blamed on the migration.
- [x] Record a `--dwmp-profile-mod` run, to compare the per-frame cost later.
- [x] Update `ARCHITECTURE.md`:
  - the state lifetimes and refusal details, as decided;
  - the core also takes `PortMapper`, `ServerInfo`, `ServerBrowser` and
    `PlayerKey`, because `TcpBackend` and the browser need them and they only
    touch Unity through the log;
  - how the map tests are loaded (Phase 2);
  - style errors are switched on folder by folder in the plugin.

**Gate:** the baseline results are saved. They are listed under
[Baseline](#baseline).

## Phase 1: projects and build

Nothing moves yet. The aim is to prove that a second DLL loads in the game and
ships in the zips.

- [x] Create `src/DarkwoodMP.Core` (netstandard2.0) with `Vec2`, `Vec3`,
      `Seconds`, `Verdict`, `Refusal`, `IClock` and `ILog`.
- [x] Add the `IsExternalInit` definition netstandard2.0 lacks, which the
      `init` properties in facts structs need.
- [x] Test that `Vec2` takes Unity's `x` and `z`: the game's ground plane is
      x-z, not x-y.
- [x] Create `tests/DarkwoodMP.Core.Tests` (net8.0, xUnit, Shouldly).
- [x] Create `src/DarkwoodMP.Analyzers` and `.editorconfig`, with the Roslyn
      package pinned to a version the installed SDK can load.
- [x] Make the plugin reference the core and call one core method at startup.
- [x] Deploy `DarkwoodMP.Core.dll` to the game folder and every client game
      folder. The `DeployPlugin` target copies only the plugin today.
- [x] Add it to `pack.sh`, which also copies only the plugin today. Without
      this, the players' zips wouldn't load.
- [x] Make `tools/run-test.sh` run the unit tests first and stop on a failure.
- [x] Change the release check from "no warnings" to "no warnings except style
      warnings in folders that haven't moved yet". The plugin will show
      thousands of those until Phase 8.
- [x] Update `docs/DEVELOPMENT.md`, and the `darkwood-release`,
      `darkwood-review`, `darkwood-sync-feature` and `darkwood-test` skills.
- [ ] By hand, still to do: a test build starts on Windows.
- [ ] By hand, still to do: a test build starts on GOG.

**Gate:** `dotnet test`; `tools/run-test.sh handshake host-world map:core`;
all three zips contain both DLLs; the log shows `All N patches applied`; a test
build starts on Windows and on GOG.

**Where the gate stands:** the build and `dotnet test` pass. The in-game tests
and building the real zips move to the final run, and the two hand tests above
are still to do.

## Phase 2: map tests into their own project

The map test steps are 26% of the code, and players don't need them. They move
first so that later steps don't have to move them too.

- [x] Cut the plugin's references to the map steps. Only three files have any:
      `TestControl.cs`, `Plugin.cs` and `Combat/CombatPatches.cs`. They go
      through a small `ITestSteps` interface in the plugin.
- [x] Create `src/DarkwoodMP.MapTests` as a second BepInEx plugin that depends
      on the main one and does nothing without `--dwmp-test-control`.
- [x] Give it access with `InternalsVisibleTo`, since most of the plugin's
      classes are internal.
- [x] Move the 40 `MapScenario*` files and apply their test-only patches from
      the new project. `TestOnlyPatch` in `Plugin.cs` goes away.
- [x] Deploy the DLL to test game folders only, and check that it's in no zip.
- [x] Format the moved files, splitting the five over 600 lines, then switch
      this project's style warnings to errors. The files over 600 lines
      became `static partial` classes split by topic
      (`MapScenarioWorld.Doors.cs`); `MapScenarioEnemies.Fights.cs` is 608.

**Gate:** `tools/run-test.sh maps`, with the same results as the baseline.

## Phase 3: network into the core

Moves `Packets`, `Session`, `INetBackend`, `TcpBackend`, `PortMapper`,
`ServerInfo`, `ServerBrowser` and `PlayerKey`. Only the Steam files and
`IGuiSection` stay in the plugin.

- [x] Replace `UnityEngine.Time` in `Session` with `IClock`. It uses two
      clocks today, real time and unscaled time, and both must stay distinct.
- [x] Replace `Plugin.Log` with `ILog`, passed to each constructor. The five
      `new PacketWriter` sites pass it too.
- [x] Pass in the mod's version instead of reading `PluginInfo.Version`.
- [x] Replace `Characters.Instance.SaveNow()` in `TcpBackend.Leave` with a
      callback that runs before leaving.
- [x] Use `Vec3` in `PacketWriter` and `PacketReader`, and add plugin extension
      methods for `Write(Vector3)` and `ReadVector3()` so callers don't change.
- [x] Update the path to `Packets.cs` in `tools/tests/worldtest.py`, and in the
      skills that name it.
- [x] Split `Packets.cs` into one file per main type.
- [x] Add `LoopbackBackend` and the unit tests: the handshake and each refusal,
      relaying to everyone but the sender, requests and verdicts, packet bytes,
      goasis's `INFO` format, and TCP framing on localhost. `Authority` is
      still in the plugin, so the request tests cover what `Session` does
      with one (`Handle`, `AllowBare`); the rest comes with Phase 4.

Phase 3 is done apart from its gate's in-game runs and the joins by hand.

**Gate:** `dotnet test`; `tools/run-test.sh net map:core map:join`; then
`tools/run-test.sh full`; with a test build, a real join on Windows and a Direct
join on the GOG build.

## Phase 4: the foundation

| Step | Moves | Lines | Gate |
|---|---|---:|---|
| 4.1 | New: `CoopSession`, `Coop.Current`, `WorldScope` | new | `net solo map:core` |
| 4.2 | `MoveCheck` (the pilot) | 128 | `host-authority map:dive` |
| 4.3 | `Authority` | 302 | `authority map:traprefuse` |
| 4.4 | `ObjRef`, `SharedIds`, `HostIds` | 601 | `world map:world` |
| 4.5 | The guards in `WorldActions` | part of 5.2 | `world authority solo map:core` |

- [x] **4.1 `CoopSession`.** Created before the first message of a session can
      arrive. `Dispose` removes its message handlers and event subscriptions:
      `Session.Register` has no way to remove a handler today, so that's added.
      Subsystems that haven't moved stay wired up in `Plugin.Awake`.

  Where it lives: `ModHost` (made in `Plugin.Load`, the part of `Awake` after
  the startup check) subscribes to the core's `Session.OnSessionStarted`,
  which fires on the host once it's hosting and on a client before its Hello
  goes out. Its handler ends any running `CoopSession` (a backend can take
  over from another without the old session ending) and makes a new one,
  which `Coop.Begin` sets as `Coop.Current`. `Session.OnSessionEnded` and
  `ModHost.Dispose` (from `Plugin.OnDestroy`) call `Coop.End`, which sets
  `Coop.Current` to null and then disposes the session. `ModHost` subscribes
  first, so the `CoopSession` exists before any other start handler runs and
  is gone before the old `OnSessionEnded` cleanups run.

  `WorldScope` is replaced in `CoopSession.WatchWorld`, which the constructor
  calls (hosting from the pause menu starts in a loaded world) and
  `Plugin.Update` calls each frame before `Session.Update` handles the
  messages. While `WorldShare.InWorld`, a `WorldScope` is kept for the
  `Player.Instance` and active scene it was made for; when either changes the
  old one is disposed and a new one made, and when the world is left it's
  disposed and `World` is null.

  **How to move a subsystem into `CoopSession`** (4.2 on):

  1. Give `CoopSession` a `public readonly` field for it, made in the
     constructor where the comment says, before `WatchWorld()`. Its
     constructor takes the `CoopSession` (and the subsystems it uses, made
     before it).
  2. Register its messages with `coop.Handle(MsgType.X, OnX)`, not
     `Session.Register`, so `Dispose` unregisters them (`Session.Unregister`
     only removes a handler that's still the registered one).
  3. Subscribe to an event and record the undo straight after:
     `coop.Net.OnPeerConnected += OnPeer;`
     `coop.Hook(() => coop.Net.OnPeerConnected -= OnPeer);`.
     `Dispose` runs the undos newest first. Game events (`SceneManager`,
     Unity callbacks) are hooked the same way.
  4. State tied to the loaded world goes on `WorldScope`, read through
     `coop.World` each time (null while no world is loaded), never kept.
  5. Delete its line from `Plugin.Load` and its `OnSessionEnded` cleanup.
     Patches reach it through `Coop.Current` and do nothing when it's null.
  6. Make the subsystem's class `internal`: its constructor takes the
     internal `CoopSession`. The map tests still see it.
  7. Methods on another class that only passed calls on to it (as
     `PlayerSync.TryChecked` did) go; their callers read
     `Coop.Current.X` and check it for null. A static counter the map tests
     read becomes a property of the instance, counted per session.
  8. A message's bytes go in a static class in the core named after its
     `MsgType`, in the subsystem's folder (`Sync/PlayerCorrection`):
     `Write(PacketWriter, ...)` writes the payload after `Begin`, and
     `Read(PacketReader)` reads it. Its unit test checks the bytes with
     the type byte.
- [x] **4.2 `MoveCheck`.** It's small, already an instance, and has clear rules,
      so it tests the recipe. `Good` becomes `Accepted`. Adjust the recipe here
      if it doesn't fit. Done: the recipe gained items 6 to 8 above. It has
      no Harmony patches, so recipe step 6 had nothing to do, and its files
      were already mirrored (`Sync/MoveCheck` and the core's `Sync/Move*`).
      The core's `MoveTracker` keeps the state, so `MoveCheck` only converts
      vectors and needs no `FactsOf`.
- [x] **4.3 `Authority`.** Change all callers in one mechanical commit.
      Done: the callers read `Coop.Current.Authority`, and its refused
      requests' rollback (`InventoryWatch`) is its `Inventory`. Handlers
      that don't call their rules yet still refuse in words, through
      `Refuse(string)` and the core's `Refusal.Words`, which goes once the
      last of them has moved. The message classes are `RequestMessage` and
      `VerdictMessage`, since `Request` and `Verdict` were taken. No patches.
- [x] **4.4 Object names.** `ObjRef` is used by about 50 files, and its lookup
      order becomes a unit-tested rule. Done: `ObjRef` keeps its static
      methods, so its callers didn't change; they reach the session's
      tables (`ObjNames`, `CoopSession.Names`) through `Coop.Current`, and
      with no session an empty `ModHost.AloneNames` that keeps nothing.
      `HostIds` is a session instance; `SharedIds` keeps no state: the save
      counters are on `ModHost` (`SaveIds`, the host saves before it hosts)
      and the joiners' on `CoopSession` (`Joiners`). The `HostIds` message's
      bytes are the core's `HostIdsMessage`, named apart from the plugin's
      class. The files stay at `World/`, the same path in both projects,
      since a `World/Names` folder would change the namespace of every
      caller.
- [x] **4.5 Guards.** Move `Depth`, `Outermost()`, `Replaying` and `Sharing` out
      of `WorldActions` into one instance, since every later subsystem uses
      them. `Sharing` is false when there's no session.
      Done: they are `World.Guards`, made first in each `CoopSession`, and
      decided by the core's `GuardRules` (new in this step: Phase 4's rules
      had none for them). Patches read `Coop.Guards`, which with no session
      is `Guards.Alone`: `Sharing` false, `Depth` and `Replaying` dropped.
      `ByLocalPlayer` and `TestAsPlayer` stay on `WorldActions`: they say
      whose call it is, not how deep or whether it's a replay, and the solo
      tests set `TestAsPlayer` with no session.

Finish with `tools/run-test.sh full`, and compare the `--dwmp-profile-mod` run
with the baseline.

## Phase 5: shared world objects

| Step | Moves | Lines | Gate |
|---|---|---:|---|
| 5.1 | `EggSync`, `InfectionSync`, `FireSync`, `FuelSync`, `BreakSync`, `Construction`, `SharedTank`, `StackSizes`, `TimeSync`, `NpcSync`, `NpcFollowers`, `TalkLock`, `DialogueGuard` | 2,179 | `world map:world map:npcs map:repair` |
| 5.2 | `WorldActions`, `ObjectActions`, `DoorLeaf`, `DisarmAnimation` | 2,211 | `world authority map:world map:traprefuse map:core` |
| 5.3 | `Containers`, `SlotShare`, `SlotShare.Sort`, `WorkbenchStorage`, `UiRefresh`, `TraderSync`, `ThrowSync` | 2,828 | `world map:items1 map:items2 map:wbrows map:trade map:ui` |
| 5.4 | `DragSync` | 858 | `host-drag map:knock map4` |

- [ ] 5.1 Small subsystems, one branch each.
- [ ] 5.2 Doors, windows and traps. `WorldActions` holds 43 of the 120 refusals
      and splits into `Doors`, `Windows` and `Traps`.
- [ ] 5.3 Items.
  - [x] Part a: `Containers`, `SlotShare` (with `SlotShare.Sort`) and
        `WorkbenchStorage`. Done: the containers and the slot sharing are
        `CoopSession.Containers` and `CoopSession.Slots` in
        `World/Inventories`; the workbench storage works alone too, so it's
        `ModHost.Workbenches` in `World/Workbenches`, and so is the
        placeables cache (`ModHost.Placeables`). Sort & Stack still works
        alone, through a static `SortAlone` that sends nothing.
- [ ] 5.4 Dragging.

Finish with `tools/run-test.sh full`.

## Phase 6: players and combat

| Step | Moves | Lines | Gate |
|---|---|---:|---|
| 6.1 | `PlayerSnapshot`, `RemotePlayer`, `PlayerSync`, `PlayerFx`, `PlayerSeparation`, `SpawnSpot`, `MapMarkers`, `LightGrid`, `ViewWatch` | 1,623 | `host-elsewhere map:view map:lights` |
| 6.2 | `Threat`, `NetPlayerProxy`, `JoinerAiPatches`, `JoinerSight`, `WardPatches`, `ShadowArmorLight`, `PlayerCues`, `ViewRects` | 1,414 | `combat map:enemies map:far` |
| 6.3 | `PlayerDamage`, `EnemyAreaHits`, `CombatPatches`, `EnemyFx` | 1,583 | `combat map:enemies map:hostile map:savage` |
| 6.4 | `NetEnemies`, `EnemyMotion`, `HostileNpcs`, `NightSpawns`, `EnemyLandings` | 2,675 | `combat map:night map:enemies map:hostile map:far map4` |

- [ ] 6.1 Player puppets.
- [ ] 6.2 Threat. Unit tests pin down that targets are picked by threat, never
      by the closest player.
- [ ] 6.3 Damage. `DamageCap` becomes a rule.
- [ ] 6.4 Enemies. `NetEnemies` is the largest file and splits by host and
      client. A client's frozen copies must still never act.

Finish with `tools/run-test.sh full`.

## Phase 7: story, places, progress and saves

| Step | Moves | Lines | Gate |
|---|---|---:|---|
| 7.1 | `FlagsSync`, `WorldEvents`, `ScriptedEvents`, `OwedParts`, `FarEvents` | 3,116 | `story map:story map:story2 map:later map:whispers map:farevents` |
| 7.2 | `Locations`, `Hideouts`, `NightObjects`, `AfterNight`, `CoopPause`, `DeathPatches` | 1,828 | `host-death host-elsewhere map:locations map:night` |
| 7.3 | `Progression`, `SkillTree`, `SkillsPanel`, `DreamSync` | 1,799 | `host-progress solo map:skills map:dreams` |
| 7.4 | `Chapters`, `Prologue`, `EpilogueSync`, `Spectator` | 1,588 | `story map:chapter map:prologue map:epilogue` |
| 7.5 | `Characters`, `WorldShare`, `WorldFiles`, `SnapshotSave`, `SaveBook`, `SaveRedirect`, `ScanWait`, `WorldTeardown` | 2,387 | `chars net world map:join map:corpse` |

- [ ] 7.1 Flags and scripted events.
- [ ] 7.2 Places and nights.
- [ ] 7.3 Skills and progress.
- [ ] 7.4 Chapters and endings. These load new worlds in a running session, so
      they're the hardest test of `WorldScope`.
- [ ] 7.5 Characters and saves. After each change here, confirm that the run's
      save check still reports the real saves as unchanged.

Finish with `tools/run-test.sh full`.

## Phase 8: the rest of the shell

| Step | Moves | Lines | Gate |
|---|---|---:|---|
| 8.1 | `LoadingScreen`, `JoinScreen`, `LoadSpeed`, `LoadProfiler`, `Tooltips`, `UiState`, `MenuEntry`, `MenuList`, `MenuSkin`, `PixelFont`, `UiWindow`, `ConfirmDialog`, `OptionsWindow`, `StartGameWindow`, `DirectPanel`, `GameCursor`, `CursorLock`, `RealMouse`, `WideScreen`, `ModProfiler` | 5,114 | `menus solo` |
| 8.2 | `TestControl`, `TestWorld`, `StateDump`, `WorldTest`, `AwayTest`, `OptionsTest`, `DragTest` | 2,429 | `world options drag map:core` |
| 8.3 | `Plugin`, `SteamBackend`, `Lobby`, `SteamTransport`, `IGuiSection` | 2,060 | `net menus solo` |

- [ ] 8.1 Menus and loading. This is mostly process state on `Plugin`.
- [ ] 8.2 The in-game test code left in the plugin.
- [ ] 8.3 Split `Plugin.cs` into startup, settings, test arguments and the
      panel. Remove `Session.Instance` and the other `Instance` singletons.
- [x] Turn style warnings into errors for the whole plugin.
- [ ] Update the Status line in `ARCHITECTURE.md`, `TECHNICAL.md` and
      `CLAUDE.md`, then delete this plan.

**Gate:** `tools/run-test.sh full` and `map4`, with results matching the
baseline; a real co-op session on Windows, on Linux and on GOG. Then release
it with the `darkwood-release` skill.

## Risks

| Risk | What would happen | How the plan handles it |
|---|---|---|
| The second DLL doesn't load on Windows or GOG | The mod doesn't start for those players | Test builds are tried by hand on both in Phases 1 and 3 |
| One large release at the end | A bug that only real players find is hard to trace to one step | Small commits by kind of change, the full suite after each phase, and a real co-op session before the release |
| Players update only `DarkwoodMP.dll` | The mod fails to load | Both DLLs ship in the zips, and the changelog and install steps say so |
| State is reset at the wrong time | Things leak between sessions, or are lost on a chapter change | Each static's lifetime is listed first (recipe step 1), and the chapter and rejoin tests are in the gates |
| A patch runs with no session | Errors while playing alone | Patches do nothing when `Coop.Current` is null, and `solo` is in the gates |
| Handlers stay registered after a session | Messages handled twice or by dead objects | `CoopSession.Dispose` removes them (step 4.1), covered by a loopback test |
| Rules differ from the old code in a small way | The host refuses what it used to allow | Rules are copied with their limits and texts, and the Python peers stay unchanged |
| The migration runs alongside new features | Merge conflicts in large files | Small branches, and the largest files are split early in their step |
| Allocation in hot paths | Lower frame rate | Facts and verdicts are structs, and `--dwmp-profile-mod` is compared with the baseline |

## Changes made while carrying out the plan

The plan above is kept as it was decided. Where it differs from this section,
this section holds.

### In-game tests run once, at the end

The maintainer decided that the in-game tests are not run after each step.
They run once, when the whole refactor is finished, and every in-game gate in
this plan moves to that final run. Until then a step is checked by the build,
the unit tests, proofs for mechanical changes and reading the diff against the
old code. The hand tests on Windows and GOG (Phases 1 and 3) and the real
co-op session (Phase 8) are still to do.

### Baseline

Measured on commit `3e9c79c`, the last one before the migration. A later run is
compared with these results, so a failure that was already here isn't blamed
on the migration.

- `tools/run-test.sh full`: 27 of the 29 items showed no failure (5 PASS, 22
  RAN, where RAN means no failure was seen and the printed lines are still to
  be read), the `steam` test was skipped (no Steam client running) and the
  full `map` test failed 24 steps:
  - `host alive` and `client alive`, as a pair at steps 268-269, 480-481,
    534-535, 697-698, 734-735 and 814-815 (12 steps)
  - 137 `host fx-hurt`
  - 203 `host bag-check here` and 205 `host bag-check gone`
  - 265 `client whistle`, 266 `host porter-came`, 267 `client porter-came`
  - 348 `both compare client enemies`, 505 `both compare client doors`
  - 705 `host fe-check-host`, 716 `host fe-hostin`
  - 768 `host host-copy here`, 769 `client trade`
- Map batteries: 15 of the 39 were run before the run was stopped. 12 passed,
  and `far`, `gaps` and `issues` failed.
- A `--dwmp-profile-mod` run recorded the per-frame cost.

### What differs from the plan

| The plan says | What is done instead | Why |
|---|---|---|
| Recipe step 7 formats each folder as it moves | One formatting pass over all the code, after Phase 1 and before Phase 2 | Later moves carry code that is already formatted, and the proof is simpler |
| Phase 2 splits five map test files over 600 lines | Fifteen files | Formatting makes them longer |
| Phase 2 switches the map tests' style warnings to errors | They were errors from the project's first build | The files were formatted before they moved, and the style rules were already errors everywhere |
| Phase 8 turns style errors on for the plugin | They went on when the formatting pass left no finding | Nothing is left to wait for |
| Recipe step 2 writes each subsystem's rules as it moves | The rules of Phases 4 to 8 were all written first, each with its unit tests, and the plugin calls them as each subsystem moves | The rules only add code to the core, so they could be written side by side without touching the plugin |
| Process state goes on the `Plugin` instance | On `ModHost`, which `Plugin` owns | `Plugin` must stay loadable when the core is missing |
| The risk "Players update only `DarkwoodMP.dll`" is covered by the zips and the install steps | The startup check (`CoreCheck`) also reports it in the log | It is handled in code |
| Phase 1's gate | Also starts the game with the core DLL removed and with an older one: both must log the fatal line and leave the game playable | Only the real engine can prove the check. These two runs are part of the final run |
