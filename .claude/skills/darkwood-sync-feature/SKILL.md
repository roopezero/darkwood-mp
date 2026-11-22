---
name: darkwood-sync-feature
description: Add something new to DarkwoodMP that is shared between the players' games (a game object's state, an action, a system), following the mod's patterns for authority, replay, relaying, object identity and testing. Use when asked to sync or share anything new in Darkwood co-op.
---

# Sharing something new between the players' games

## 1. Find out how the game does it

Use the `darkwood-game-code` skill. Find:
- the methods that change the thing (the calls a player's action ends in, and the ones the world makes by itself)
- what state it keeps (fields, and its `SaveState` class if it's saveable)
- what else those methods call, since nested calls matter

## 2. Choose the mechanism

| The thing | Use | Examples |
|---|---|---|
| Changed by discrete calls whose visuals and sounds matter | **Replay the call** in the other games: a `WorldActions.Action` + Harmony patches | doors (`World/Doors`), windows, traps, lamps, NPC deaths |
| Changed in many small ways while a player has it open | **Send its saved state** (`X.SaveState` as JSON) when it changed | containers, traders' stock (`World/Inventories/Containers`) |
| Living things with AI | **The host runs it**; clients show copies driven by host states, and send their player's effects on it to the host | enemies (`Combat/Enemies/NetEnemies`) |
| Story or bookkeeping data | **Periodic diff** of entries, last writer wins, the host relays | flags, NPC states, dialogues (`World/Story/FlagsSync`) |
| Made while playing | `ObjSpawn` with a random ID (`coop.Containers.Made`; add the prefab to `SpawnRules.Shared` in the core or mark it placed) | dropped items, death bags, traps |
| Per player | Kept by the host by player key | characters (`World/KeptCharacters`) |

## 3. Rules every shared thing follows

- **Authority:** the world's own changes are the host's. A client sends only what its player did: `WorldActions.Send` checks `ByLocalPlayer()` (the stack contains player, input or UI code), or pass `byPlayerKnown: true` when you've checked otherwise (for example, the collider was the local `Player`). Ask "does the client's game also do this by itself, because it runs the same world scripts?" If so, only the host sends it.
- **The host decides:** a client's player's action goes to the host as a request (`coop.Authority.Request(writer, what, done)`, where `coop` is `Coop.Current` or the session your subsystem was given) instead of `SendToHost`. In the host's handler, check it can be so (`coop.Authority.InReach(pos, reach, name)`, the thing's state here, numbers a player could produce) and `coop.Authority.Refuse(verdict)` (or a reason string) if not, sending the requester the state to roll back to first (`WorldActions.SendState` or your own state message). Roll any chance on the host and say the outcome with `coop.Authority.Result(...)`; the client acts on it in `done(ok, reason, result)`. The client's inventory change in that frame is undone automatically on a refusal (`InventoryWatch`, `coop.Authority.Inventory`).
- **No echo:** apply received changes with `Coop.Guards.Replaying = true`, and check `Coop.Guards.Sharing` (which is false while replaying) before sending.
- **Relay:** in the receive handler, if `IsHost`, pass a client's message on to the others with `except: from`.
- **Only real changes, outermost only:** snapshot the state in a prefix (`Coop.Guards.Depth++`), compare it in the postfix, and send only if `Coop.Guards.Outermost()` and something changed.
- **Identity:** `ObjRef.Of(go)` and `ObjRef.Resolve<T>(ref)`. Resolving can fail when the other game doesn't have the object. Log that with `--dwmp-debug-world` and skip it.
- **Frozen copies on clients:** if the thing can be touched by characters (traps, effects, sounds, hits), make sure the client's frozen copies of the host's enemies (`coop.Enemies.Frozen`, `coop.Enemies.IsPuppet`) don't trigger it. Characters in areas only the client is in (`NetEnemies.LocalOnly`, a static check) run normally.
- **Harmony:** pass argument types for overloads, patch private methods by name, and never reference Steamworks types outside the Steam classes.

## 4. Where the code goes (`docs/ARCHITECTURE.md`)

Use the same feature folder in both projects (`World/<Feature>/`, `Combat/<Feature>/`), one main type per file, about 600 lines at most (split into `<Type>.<Part>.cs` partial files).

- **Rules and facts in the core** (`src/DarkwoodMP.Core/<Area>/<Feature>/`): every decision (may this request happen, did this call change anything worth sending, what to roll) is a static method on a `<Feature>Rules` class that takes a `<Feature>Facts` struct and returns a `Verdict` with a named `Refusal` (its sentence goes in `Refusals.Text`), or a plain value. No Unity or game types there. A message's bytes can go in the core too (a `Write`/`Read` pair), so the tests can check them.
- **An instance in the shell** (`src/DarkwoodMP/<Area>/<Feature>/<Feature>.cs`): a class owned by `CoopSession` (a `public readonly` field made in its constructor) if it lasts one session, or by `ModHost` if it must outlive one (the main menu, saves on disk); state tied to one loaded world goes on `WorldScope`. No new statics. It reads the game in one `FactsOf(...)` per game type, calls the rule, and does what it decided, with no `if` of its own that decides something. It registers its handlers with `coop.Handle(MsgType.X, OnX)` and undoes any event subscription with `coop.Hook(...)`, so `CoopSession.Dispose` removes them. If it needs per-frame work, call its `Update` from `Plugin.Update` beside the others (`coop.Fuel.Update()` and so on).
- **Thin patches** (`<Feature>Patches.cs`): one patch class per game method, named `<GameType><Method>` (`DoorOpen` for `Door.open`), no `Patch` suffix. A patch reads `Coop.Current`, does nothing when it is null (playing alone), and hands its typed arguments to one shell method. Process state outside a session is `Plugin.Instance.Host`.
- **Unit tests** (`tests/DarkwoodMP.Core.Tests/<Area>/<Feature>/<Feature>RulesTests.cs`): xUnit + Shouldly, a nested class per tested method, verb-first sentence names (`DoorRulesTests+MayOpen.Refuses_a_locked_door`). A message's handling between host and clients can be tested in one process with `LoopbackBackend`.


## 5. Protocol

- Add the message to `MsgType` in `src/DarkwoodMP.Core/Net/MsgType.cs` with a `///` comment that gives its exact bytes, then **bump `Protocol.Version`** (`src/DarkwoodMP.Core/Net/Protocol.cs`).
- Register its handler in the feature's constructor (`coop.Handle`).
- `Session.Dispatch` drops anything from a client that hasn't finished the handshake, except `Hello` and `NeedSave`.

## 6. Test it

- Unit-test every new rule in `tests/DarkwoodMP.Core.Tests` (a `<Class>Tests` class with a nested class per method, in the folder that mirrors the core's): `dotnet test tests/DarkwoodMP.Core.Tests -c Release --blame-hang-timeout 60s --blame-hang-dump-type none`. A test only counts once you have seen it fail, so break the rule by hand first.
- Add a step to `src/DarkwoodMP/Testing/WorldTest*.cs` (a partial class over several files) that does the thing on the host, and decode the message in `tools/tests/worldobs.py`.
- Run `tools/run-test.sh capture host-world client-world` (the `darkwood-test` skill). The host log should show `[world] sent ...`, and the client log `[world] got ... : <object name>`.
- Add the requests your feature makes to `tools/tests/authtest.py` (allowed and refused cases), and run `host-authority client-authority`.
- Run the regression tests too (`host-combat client-enemies client-chars`), since many patches share hot paths, or simply `tools/run-test.sh all`.

## 7. Document it

Update `docs/GUIDE.md` (the "What's shared" table, and the known limitations if any part isn't covered) and the protocol table in `docs/TECHNICAL.md`. Say how it was tested (that goes in `CHANGELOG.md` at release). Add a line to the README's feature list only if a player would notice it.
