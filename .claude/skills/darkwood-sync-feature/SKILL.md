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
| Changed by discrete calls whose visuals and sounds matter | **Replay the call** in the other games: a `WorldActions.Action` + Harmony patches | doors, windows, traps, lamps, NPC deaths |
| Changed in many small ways while a player has it open | **Send its saved state** (`X.SaveState` as JSON) when it changed | containers, traders' stock (`Containers`) |
| Living things with AI | **The host runs it**; clients show copies driven by host states, and send their player's effects on it to the host | enemies (`NetEnemies`) |
| Story or bookkeeping data | **Periodic diff** of entries, last writer wins, the host relays | flags, NPC states, dialogues (`FlagsSync`) |
| Made while playing | `ObjSpawn` with a random ID (`Containers.Made`; add the prefab to `SharedPrefabs` or mark it placed) | dropped items, death bags, traps |
| Per player | Kept by the host by player key | characters (`Characters`) |

## 3. Rules every shared thing follows

- **Authority:** the world's own changes are the host's. A client sends only what its player did: `WorldActions.Send` checks `ByLocalPlayer()` (the stack contains player, input or UI code), or pass `byPlayerKnown: true` when you've checked otherwise (for example, the collider was the local `Player`). Ask "does the client's game also do this by itself, because it runs the same world scripts?" If so, only the host sends it.
- **The host decides:** a client's player's action goes to the host as a request (`Authority.Request(writer, what, done)`) instead of `SendToHost`. In the host's handler, check it can be so (`Authority.InReach(pos, reach, name)`, the thing's state here, numbers a player could produce) and `Authority.Refuse(reason)` if not, sending the requester the state to roll back to first (`WorldActions.SendState` or your own state message). Roll any chance on the host and say the outcome with `Authority.Result(...)`; the client acts on it in `done(ok, reason, result)`. The client's inventory change in that frame is undone automatically on a refusal (`InventoryWatch`).
- **No echo:** apply received changes with `WorldActions.Replaying = true`, and check `WorldActions.Sharing` (which is false while replaying) before sending.
- **Relay:** in the receive handler, if `IsHost`, pass a client's message on to the others with `except: from`.
- **Only real changes, outermost only:** snapshot the state in a prefix (`WorldActions.Depth++`), compare it in the postfix, and send only if `WorldActions.Outermost()` and something changed.
- **Identity:** `ObjRef.Of(go)` and `ObjRef.Resolve<T>(ref)`. Resolving can fail when the other game doesn't have the object. Log that with `--dwmp-debug-world` and skip it.
- **Frozen copies on clients:** if the thing can be touched by characters (traps, effects, sounds, hits), make sure the client's frozen copies of the host's enemies (`NetEnemies.Frozen`, `IsPuppet`) don't trigger it. Characters in areas only the client is in (`NetEnemies.LocalOnly`) run normally.
- **Harmony:** pass argument types for overloads, patch private methods by name, and never reference Steamworks types outside the Steam classes.

## 4. Protocol

- Add the message to `MsgType` in `Net/Packets.cs` with a `///` comment that gives its exact bytes, then **bump `Protocol.Version`**.
- Register its handler in the feature's constructor (`session.Register`). Wire it into `Plugin.Awake` and `Plugin.Update` if the feature needs per-frame work.
- `Session.Dispatch` drops anything from a client that hasn't finished the handshake, except `Hello` and `NeedSave`.

## 5. Test it

- Add a step to `World/WorldTest.cs` that does the thing on the host, and decode the message in `tools/tests/worldobs.py`.
- Run `tools/run-test.sh capture host-world client-world` (the `darkwood-test` skill). The host log should show `[world] sent ...`, and the client log `[world] got ... : <object name>`.
- Add the requests your feature makes to `tools/tests/authtest.py` (allowed and refused cases), and run `host-authority client-authority`.
- Run the regression tests too (`host-combat client-enemies client-chars`), since many patches share hot paths, or simply `tools/run-test.sh all`.

## 6. Document it

Update `docs/GUIDE.md` (the "What's shared" table, and the known limitations if any part isn't covered) and the protocol table in `docs/TECHNICAL.md`. Say how it was tested (that goes in `CHANGELOG.md` at release). Add a line to the README's feature list only if a player would notice it.
