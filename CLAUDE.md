# DarkwoodMP

A BepInEx 5 / HarmonyX mod that adds online co-op to Darkwood (Unity 2021.3.30f1, Mono). One build (`DarkwoodMP.dll` and `DarkwoodMP.Core.dll`) works on Windows, Linux, Steam and GOG. This file covers how to work on it; see "Docs" below for where everything else is written down.

## Decisions the user made (don't re-open them)

- Full co-op, **host-authoritative** (star topology: clients only talk to the host, which relays).
- Two connection types: **Steam** (lobbies and invites, P2P relay) and **Direct**. Direct follows the user's own Go engine goasis (`git@github.com:roopezero/goasis.git`, private): framed TCP, Hello/Refused/Welcome, player keys, a UDP info query in goasis's exact `INFO` format, and a server browser (Favorites, History, LAN).
- **No master server, ever.** Don't propose or document a Public browser tab.
- Enemy AI picks targets by **threat**, never "the closest player".
- It must work on **Windows** and on the **GOG** version (no Steam).
- **Code architecture (`docs/ARCHITECTURE.md`):** a functional core and a thin shell. Decisions live in `src/DarkwoodMP.Core` (no Unity or game references), as `...Rules` taking a `...Facts` struct and returning a `Verdict` with a named `Refusal`; the plugin's shell reads the game (`FactsOf`) and does what the core decided. Every subsystem is an instance owned by `CoopSession` (no new statics; patches reach it through `Coop.Current`). Thin Harmony patches. Feature folders mirrored in both projects; map test steps in `src/DarkwoodMP.MapTests`, not shipped. Unit tests: xUnit + Shouldly, `<Class>Tests` with a nested class per tested method and verb-first sentence names (`DoorRulesTests+MayOpen.Refuses_a_locked_door`), rules and the network in one process (`LoopbackBackend`). The code follows this layout; new code must too. Process state that outlives a session lives on `ModHost` (reached as `Plugin.Instance.Host`).

## Layout

```
src/DarkwoodMP/                 the plugin (the shell): reads the game, does what the core decided
  Plugin*.cs, ModConfig.cs      entry point (partial files: startup, test arguments, shots, solo and join tests), settings
  ModHost.cs                    process state that outlives a session (backends, menus, loading, saves, the test harness)
  CoopSession.cs, Coop.cs       one session's subsystems; Coop.Current is how patches reach it (null when playing alone)
  CoreCheck.cs                  logs a fatal line if DarkwoodMP.Core.dll is missing or older
  Net/                          the core's adapters (UnityClock, BepInLog, PacketVectors), Presence/
  Net/Steam/                    SteamBackend, Lobby, SteamTransport, SteamStartup, IGuiSection (the only Steamworks code)
  Menus/                        the Multiplayer window (CoopPanel), DirectPanel, options and start game windows, menu skin and fonts, cursor
  Hosting/                      HostStart
  Testing/                      in-game test harness (TestControl, TestWorld, StateDump, WorldTest, ...) for --dwmp-* tests
  Sync/                         MoveCheck; Players/: player puppets (PlayerSnapshot, RemotePlayer, PlayerSync, PlayerFx)
  Combat/<feature>/             Enemies (NetEnemies), Targeting (Threat), Joiners (NetPlayerProxy, JoinerAi), Damage
                                (PlayerDamage), Hostiles, Nights, Noise, Effects, Cues, Beams, Landings, Motion
  World/                        Authority, Guards, WorldActions, ObjRef, InventoryWatch, shared ids;
  World/<feature>/              Joining (WorldShare), Saves (SaveRedirect), Inventories (Containers, SlotShare), Doors,
                                Windows, Traps, Story (FlagsSync, Chapters), KeptCharacters, Loading, Clock, ... (one per feature)
src/DarkwoodMP.Core/            rules, facts and small types with no Unity or game references (netstandard2.0); ships as a
                                second DLL; the same feature folders as the plugin (Combat/, World/, Sync/, Menus/, Testing/, ...)
src/DarkwoodMP.Core/Net/        Session (handshake, relay), TcpBackend + PortMapper (UPnP/NAT-PMP), MsgType (the message list),
                                Protocol (Version), PacketWriter/PacketReader, ServerBrowser/ServerInfo, PlayerKey, Steam/ (rules)
src/DarkwoodMP.MapTests/        the in-game map test steps (World/MapScenario*), a second plugin that needs --dwmp-test-control;
                                deployed to the test game folders only, never in the zips
src/DarkwoodMP.Analyzers/       the build's formatting rules, DW0001 to DW0007
src/GameReferences.props        the game's paths and DLL references
tests/DarkwoodMP.Core.Tests/    unit tests of the core (net8.0, xUnit, Shouldly)
tests/DarkwoodMP.Analyzers.Tests/ unit tests of the formatting rules on small pieces of code (net8.0, xUnit, Shouldly)
Directory.Build.props           the mod's version (<Version>), language settings, the analyzer reference
.editorconfig                   style rules, errors in every project
tools/run-test.sh, tools/tests/ in-game tests with scripted Python peers
scripts/darkwood_mp.sh         Linux launcher (Steam's runtime container safe)
scripts/README-*.txt           DarkwoodMP-README.txt inside the zips (install and play steps)
pack.sh                        builds dist/DarkwoodMP-win.zip, -win_x86.zip and -linux.zip
container/                     `box`: Claude Code in a rootless podman container (container/README.md)
README.md, CHANGELOG.md        for players (see "Docs")
LICENSE, LICENSES/             MIT for the mod; LICENSES/ has BepInEx's bundled parts' licenses, which pack.sh puts in the zips
docs/                          GUIDE, TECHNICAL, DEVELOPMENT, INTERACTIONS (gaps), MULTIPLAYER_* (audits, test guide)
```

## Build and run

- Tools are in the user's home, not on PATH: `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:~/.dotnet/tools:$PATH`.
- `dotnet build src/DarkwoodMP -c Release` builds and deploys both DLLs (`DarkwoodMP.dll` and `DarkwoodMP.Core.dll`) to `<game>/BepInEx/plugins/DarkwoodMP/`. The game is at `~/.local/share/Steam/steamapps/common/Darkwood`. Restart the game to load a new build. For the in-game tests build `src/DarkwoodMP.MapTests` instead: it builds the mod too and also deploys `DarkwoodMP.MapTests.dll` (the map test steps) there and to the client game folders.
- The mod's version is `<Version>` in `Directory.Build.props`. The style rules are errors, so a build with any finding fails.
- The mod's log is `<game>/BepInEx/LogOutput.log`. Unity's is `~/.config/unity3d/Acid Wizard Studio/Darkwood/Player.log`, which also has the game's exceptions. A good start logs `All N patches applied`.
- To launch it yourself: `cd <game> && setsid timeout 300 ./darkwood_mp.sh --inner <--dwmp-* args> &`. Without `--inner` the script expects Steam's command line. Stop it with `kill $(pgrep -x Darkwood.x86_64)`; never `pkill -f Darkwood`, which matches your own shell.
- **In the box** (`container/box`), `$DWMP_BOX` says the mode: `dev` has a read-only game folder, builds without deploying and can't run the game (no in-game tests; ask the user to restart the box with `--game`); `game` can, on a copy of the saves.
- Only **one copy of the game** runs per machine, so "the other player" in tests is always a Python script. Use the `darkwood-test` skill, and run tests after every change to game-facing code.

## Tests

- Unit tests: `dotnet test tests/DarkwoodMP.Core.Tests -c Release --blame-hang-timeout 60s --blame-hang-dump-type none`. They need neither the game nor a display, and `tools/run-test.sh` runs them first and stops if one fails.
- Analyzer tests: `dotnet test tests/DarkwoodMP.Analyzers.Tests -c Release --blame-hang-timeout 60s --blame-hang-dump-type none`, after any change to `src/DarkwoodMP.Analyzers`. `tools/run-test.sh` doesn't run them.
- One test or battery: `tools/run-test.sh NAME...` (e.g. `tools/run-test.sh host-world`, `tools/run-test.sh map:ui`; `map:NAME` runs `tools/tests/map_scenario_NAME.py` alone in a fresh test world). `tools/run-test.sh --list` lists them all; `--plan NAME` shows what would run.
- A custom subset of map steps: `MAP_SCENARIO=module tools/run-test.sh map` runs `tools/tests/module.py`.
- The full suite (`tools/run-test.sh full` = `all` + `map`) takes well over an hour: run it once at the end, in the background, not after each change.
- The game needs a display: check `echo $DISPLAY` before a run (see the `darkwood-test` skill).

## Safety: the user's saves

- The user's real saves are in `~/.config/unity3d/Acid Wizard Studio/Darkwood/1_4Save`. **Never let a test write there.** Host tests play a copy (`--dwmp-sandbox DIR`, which `tools/run-test.sh` sets up). Client tests are redirected into the mod's cache by design.
- `tools/run-test.sh` backs the saves up, compares their hashes after each test, and fails loudly if they changed. If you launch the game by hand with a world, use `--dwmp-sandbox` too.
- Don't commit anything decompiled from the game (`Assembly-CSharp.dll`). The decompile lives outside the repo; the `darkwood-game-code` skill regenerates it.

## Rules the code depends on

- **Steamworks stays inside `SteamBackend`, `Lobby` and `SteamTransport`.** Nothing else may name a Steamworks type: on GOG, Steam isn't there, and a type reference in a shared class would stop the mod loading. Steam is detected through reflection on the game's `SteamManager` (see `SteamStartup.SteamReady` in `Net/Steam`).
- **Wire format:** messages are `u8 type` + payload, little-endian. Strings use `PacketWriter.WriteStr` (u16 length + UTF-8), never `BinaryWriter.Write(string)`. TCP adds `u32 length` framing. **Any change to a message's bytes bumps `Protocol.Version`** in `src/DarkwoodMP.Core/Net/Protocol.cs`; the Python tests read it from there. Keep each `MsgType`'s `///` comment (`src/DarkwoodMP.Core/Net/MsgType.cs`) as the authoritative description of its bytes.
- **Who decides:** the host runs enemies, the clock, trader restocks, world resets on death, and anything the world does by itself. A client sends only what **its own player** did: `WorldActions.ByLocalPlayer()`, `byPlayerKnown`, and a "fromPlayer" check for hits on enemies. A client's frozen copies of the host's enemies must never act (no AI, no animation events, no traps, no effects, no hearing).
- **The host decides, clients predict (`World/Authority.cs`, reached as `Coop.Current.Authority`; its rules are `AuthorityRules` in the core):** a client does its player's action at once and sends it as a `Request` (`Authority.Request(writer, what, done)`); the host's handler for that message checks it against the host's world (`Authority.InReach`, the thing's state, plausible numbers) and calls `Authority.Refuse(verdict or reason)` (after sending the state to roll back to, e.g. `WorldActions.SendState`) or `Authority.Result(...)` for what only the host decides. Anything left to chance that changes the shared world is rolled on the host (trap disarms), and damage to the host's enemies is capped by the weapon (`WeaponRules.DamageCap` in the core's `Combat/Damage`). A refused request is rolled back on the client: the host's `ObjState`, and `InventoryWatch` (`World/InventoryWatch.cs`, `Authority.Inventory`) undoes that frame's inventory change. Containers go slot by slot (`World/Inventories/SlotShare`), refused per slot. Movement is capped by `Sync/MoveCheck` (`CoopSession.Moves`; the core's `PlayerCorrection` writes the correction). It's server reconciliation, not frame re-simulation: Darkwood isn't deterministic.
- **Replays:** when applying something from another game, set `Coop.Guards.Replaying = true` (`World/Guards.cs`; `Guards.Alone` when there's no session) so it isn't sent back, and have the host relay a client's change to the others (`except: from`).
- **Nested calls:** patched world calls use the `Coop.Guards.Depth` / `Outermost()` pattern, so only the outermost call that changed something is sent. A hit that breaks a barricade must not also send the break.
- **Naming objects across games:** use `ObjRef` (`World/ObjRef.cs`, rules in the core's `ObjRefRules`; things spawned while playing, then enemy net IDs, then save IDs, then name+position). Everyone loaded the host's save, so save IDs match.
- **Harmony:** give argument types for overloaded methods (an ambiguous match used to stop the whole mod). Patch private methods and coroutine bodies by name. `Plugin.PatchAll` patches class by class and logs failures, so one patch that doesn't fit doesn't stop the rest.
- **Game quirks:** the game has its own global `RandomNumberGenerator` and `Button` types that shadow .NET's (qualify them). Darkwood counts a new day at dawn, not midnight. `Player` is a 9000-line singleton, so don't instantiate it.
- **Comments and docs:** plain, concrete sentences that say what happens and why; no hype.
- **Naming and size (`docs/ARCHITECTURE.md`):** short real words for variables and parameters (single letters only in short scopes); the type carries the unit (`Vec2`, `Seconds`) and the class says whose; say which version of a value it is (`Reported`, `Accepted`, never `Good`); at most three parameters, more become a facts struct; no `bool` that switches behaviour (split the method); a call that needs named arguments means the method should change; `PascalCase` public members, `_camelCase` private fields; files about 600 lines at most, one main type each.
- **Formatting (`docs/ARCHITECTURE.md`, enforced by the build):** braces on every `if`/`else`/loop, even around one statement, on their own lines (C# style); no one-line blocks (`=>` lambdas and expression-bodied members are allowed); a blank line after a statement, member or accessor that holds a `{ }` pair spread over lines, anywhere in it (so after `});`, `};` and `}).Max();` too, but a `{ }` on one line doesn't count), unless a `}` or the end of the file comes next; a comment or directive line isn't blank, so the blank line goes before it; at most 80 columns, comments included; no ternary `?:`, no `??`, `??=` or `?.` (use an `if`; on Unity objects they skip Unity's destroyed check). Errors in every project.

## Docs

The repo is meant to be public, so write every doc for a stranger reading it on GitHub. Don't link the private goasis repo, and don't put the user's paths or personal details in them.

- **`README.md` is for players**, the average gamer: what the mod is, its features, how to install it, how to host and join, a short troubleshooting list and the few limitations a player would notice. No version history, test details, protocol, file layouts, launch arguments or code names. Keep it short; add a feature there only as one line, if a player would care.
- **`CHANGELOG.md`:** one section per release, newest first, with the protocol version and whether it plays with the previous one, what changed, and **how it was tested** (scripted peers, the map test, or real players only when the user says they played).
- **`docs/GUIDE.md`:** everything the mod does in detail, for players: menus, hosting, joining, the "What's shared" table, settings, files, troubleshooting, the full known limitations.
- **`docs/TECHNICAL.md`:** the design (authority, prediction, backends, handshake) and the protocol table.
- **`docs/ARCHITECTURE.md`:** how the code is organised (projects, core and shell, rules, state, patches, naming, unit tests). Keep its "Status" line true as subsystems move.
- **`docs/DEVELOPMENT.md`:** building, the tests, the `--dwmp-*` launch arguments, the test world.
- **`scripts/README-win.txt` and `README-linux.txt`** go into the zips: keep their install and play steps in step with the README.
- Keep all of them in step with what the code does, and with what was actually tested. `docs/INTERACTIONS.md` and `docs/MULTIPLAYER_*.md` are working notes (gaps, audits, the test guide); keep them true, but they aren't for players.

## Skills

- `darkwood-test`: run the in-game tests (and which ones cover what).
- `darkwood-sync-feature`: add something new that's shared between the players' games.
- `darkwood-game-code`: decompile the game and find how it does something.
- `darkwood-release`: bump versions, build the zips, commit.
