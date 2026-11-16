# Playtest 0.25.1: triage and progress

The reports are in [REPORTS.md](REPORTS.md). This file is the source of truth for their progress.

**Convention.** The project has no local convention for bug reports. Earlier playtest reports were GitHub issues (#7-#18, closed by `darkwood-release`), and the repository has none open now. Following the task's rule for an external tracker, no issues were created: triage lives here, and the issues are proposed at the end. Commits use the project's conventional-commit style (`fix(scope): ...`), with the R-IDs in the message.

Triage was done on `main` at 0.26.0 (protocol 28). The reports were written against 0.25.1, so the 0.26.0 fixes were checked first. Unless an entry says otherwise, its evidence comes **from reading the code** (the mod and the decompiled game). Nothing was reproduced in game during triage.

Status words: `valid`, `already fixed`, `cannot reproduce`, `unclear`, `not implemented`, `partially implemented`; and in Phase 2, `fixed` and `open`.

## Summary

| ID | Title | Status | Size | Related |
|---|---|---|---|---|
| R01 | Crate shadow stays for joiners (Ch2 hideout) | valid | S | R02, R09 |
| R02 | Shadow barrier on night enemies not shown to joiners | valid (known gap) | M | R01 |
| R03 | Container window stuck open | valid (likely cause found) | M | R08 |
| R04 | Joiners can't dive | unclear (two candidate causes) | S + R06 | R05, R06, R09 |
| R05 | One player dives, everyone dives | valid | S-M | R04, R09-E |
| R06 | Oxygen tank stays filled; anyone can dive | not implemented | M | R04, R05 |
| R07 | Tooltips for all items and map locations | not implemented | M (L with journal items) | - |
| R08 | Hide HUD and inventory under the pause menu | not implemented | S-M | R10, R03 |
| R09 | Story progression that still needs the host | partially implemented | L in total (5 parts) | R04, R05 |
| R10 | Name tags drawn over cutscenes and menus | valid | S | R08 |

## Shared causes

**Host vs client (R01, R02, R04, R05, R09).** There is no single root cause. They fall into three groups:

1. **Steps are sorted by their type, never by what they target** (`src/DarkwoodMP/World/ScriptedEvents.cs:105-133`). A shared step such as `runFunction diveIn` on the unique object `player` runs in every game against that game's own `Player.Instance`. This causes R05 (and R09-E). It is the reverse of "host only": player state gets shared.
2. **State changed on the host that doesn't reach clients, or is blocked on them.** R01: a client blocks the game's own load-time `ShadowArmor.die(instant)`, so a mid-game joiner keeps a dead shadow. R02: an enemy's shadow armour is never sent at all. R09-A, B and C: triggers that a replayed action, a world trigger or a forwarded hit fires on the host give the player's part to the host's player.
3. **Per-player gates.** R04: the dive needs `oxygentank_full` in that player's own inventory. The host may also undo the client's short scripted teleport (`MoveCheck`). R06 is the request to change the gate.

**Mid-game joiners.** Flags, NPC state, the journal and fired triggers arrive through the snapshot and FlagsSync (0.26.0 fixed the catch-up). Two things don't: shadows that died before the join (R01), and one-time trips owed to a later player (R09-D).

**UI state (R03, R08, R10).** The mod has no central UI-state check. Each feature tests its own subset of `Core.mainMenu`, `Core.Paused`, `inMenu()`, the map and the epilogue (`CursorLock.cs:16`, `World/SkillsPanel.cs:63,94`, `Sync/PlayerSync.cs:162-164`, `Sync/MapMarkers.cs:82`, `MenuList.cs:21`). A `UiState` helper covers R10 fully and gives R08 its trigger. R08 also needs a hide-and-restore step. R03 is a separate bug (an orphaned window); it only touches R08 in that an orphaned window shows through the pause menu.

## Entries

### R01. Shadow on the crate doesn't disappear for joining players

- **Status:** valid
- **Evidence:**
  - `src/DarkwoodMP/World/ObjectActions.cs:370-389` (`ShadowArmorDiedPatch`): on a client, every `ShadowArmor.die` on an item that isn't a replay of the host's `ShadowArmorDied` is blocked (line 379). That includes the game's own `die(instant: true)` in `Item.loadValues` (`/tmp/darkwood-decomp/Item.cs:205-212`), which removes a shadow that the save says is dead.
  - The crate is the hideout 5 crate (`Item.name_noShadowArmor`, `Item.cs:229`). A joiner who loads the host's world after the shadow died (a mid-game join, a rejoin, a world reload or a new chapter) keeps the shadow and can't open the crate (`Item.openInventory`, `Item.cs:808`). Joiners who were in the game when it died do get the message.
- **Fix:** let `die(instant: true)` run on a client (it comes from loading a save, never from light) and don't send it.
- **Verification:** new map step in `map:gaps`. The client has a crate whose shadow is dead in the save and loads it (`Item.loadValues` with no `saS`); then `shadow-check dead` on the client. Manual: Chapter 2 save; the host burns off the hideout crate's shadow and saves; a client joins and opens the crate.
- **Size:** S. **Related:** R02, R09.

### R02. Black shadow barrier not visible to other players

- **Status:** valid; a known gap (`docs/INTERACTIONS.md:307`, "Joiners don't show the armour's particles")
- **Evidence:**
  - `CharacterSpawner` adds `ShadowArmor` at runtime to half of the chapter 2+ night spawns (`/tmp/darkwood-decomp/CharacterSpawner.cs:246-251`), with the `particles/shadow_armor_01` effect (`ShadowArmor.cs:81`). The red chomper is `CharacterType.ChomperRed`.
  - `EnemySpawn` carries only id, save id, prefab, name, position and rotation (`src/DarkwoodMP/Combat/NetEnemies.cs:326-341`). The client builds its copy from the prefab (`NetEnemies.cs:885-893`), so the copy has no armour. Mid-night joiners get the same `EnemySpawn`.
  - The armour itself works on the host, and joiners' lights wear it down there (`Combat/ShadowArmorLight.cs`).
- **Fix:** add an armour flag to `EnemySpawn`, plus a message when the host's armour dies. On a client, add a `ShadowArmor` that only shows the effect: no light check and no damage on copies. Protocol bump.
- **Verification:** extend `map:enemies` (`ee-shadow-spawn`/`ee-shadow-check`, `World/MapScenarioEnemies.cs:544`): the client's copy has the armour effect, the host lights the enemy until the armour dies, and the client's armour is then gone. Manual: Chapter 2 night at the hideout; a client looks at an armoured red chomper and burns its armour off.
- **Size:** M. **Related:** R01.

### R03. Container window sometimes gets stuck open

- **Status:** valid. The likely cause was found from reading the code; it was not reproduced.
- **Evidence:**
  - A container's window (`Inventory.thisUI`) is parented under `Singleton<UI>`, not under the container (`/tmp/darkwood-decomp/Inventory.cs:687-688`). `Inventory` has no `OnDestroy`.
  - When the container is destroyed while its window is open, `Player.openedItemInventory` becomes Unity-null. `Player.closeInventory` (`Player.cs:5017`) then skips `hide()`, and nothing ever refers to that window again until the scene reloads (leaving and rejoining).
  - The mod destroys open containers without hiding them first in five places:
    - `Combat/NetEnemies.cs:1255`: a corpse despawned by the host. This fits "opens a corpse".
    - `Combat/NetEnemies.cs:1162`: a corpse cut in half, which destroys the `Inventory` component.
    - `World/WorldActions.cs:632`: ObjState `Gone`.
    - `World/ObjectActions.cs:129`: removed by another player.
    - `World/Containers.cs:524`: our placement refused.
  - The paths that do hide first are fine (`Containers.cs:306`, `:410-416`, `SlotShare.cs:304-307`).
- **Fix:** add a `Containers.CloseIfOpen(GameObject)` that hides any open `Inventory` in the object, and call it before each destroy. Add a per-frame safety net: when `openedItemInventory` was destroyed, destroy its orphaned window and close the inventory.
- **Verification:** new map step in `map:corpse`. The client opens the host's corpse, the host removes the corpse, and the check is that the client has no window left under `UI` and `openedItemInventory` is a real null. Manual: a client loots a corpse while the host burns it or it despawns.
- **Size:** M. **Related:** R08.

### R04. Diving doesn't work for joining players

- **Status:** unclear: two candidate causes, both from reading the code
- **Evidence:**
  - Diving is `Player.diveIn`/`diveOut` (`/tmp/darkwood-decomp/Player.cs:6837-6857`), called by `runFunction` steps of the village cellar's water holes (`outside_village_ch2_cellar_01`, from the asset strings). The gate is most likely an `EventTriggerRequirement` `haveItem oxygentank_full`, checked against that game's own player (`EventTriggerRequirement.cs:138-163`).
  - Cause 1: the full tank is an item in one player's inventory, so a client without it gets "noOxygenTank". That is the game's design, which R06 asks to change.
  - Cause 2: the client's scripted teleport under water (`water_teleport_01B`, `transportPlayerToObject`) is shorter than `MoveCheck.Teleport` (3000), so the host may take it for a speed hack and send `PlayerCorrection` (`src/DarkwoodMP/Sync/MoveCheck.cs:53-72`). No scripted transport calls `MoveCheck.Forget`. This would hit any short scripted teleport.
  - Ruled out: `TriggerAllowed` (`World/ScriptedEvents.cs:810-820`) doesn't check items, and the host loads the cellar for the joiner (`World/Locations.cs:20-23`).
- **Fix:** forget the client's old position on the host when a client's scripted teleport is granted, or when the client reports one. The R06 change removes cause 1.
- **Verification:** a map step where the client fires a dive hole with the tank and ends at the target, with no `put back` in the host's log. Manual: a Chapter 2 save, the client holds the full tank and dives in the village cellar.
- **Size:** S (plus R06). **Related:** R05, R06, R09.

### R05. When one player dives, every player dives

- **Status:** valid
- **Evidence:** `PlayerSteps` (`src/DarkwoodMP/World/ScriptedEvents.cs:105-112`) doesn't include `runFunction`, so `Runs()` (`:130-133`, `:186-196`) treats it as shared. The step's target is the unique object `"player"` (`/tmp/darkwood-decomp/GameEvent.cs:644-650`), which is each game's own `Player.Instance`. So every game runs `diveIn` on its own player: it calls `halt()` and sets `Core.forbidInputs = true` (`Player.cs:6837-6845`). On the first dive the dive-out comes from the diver's dream (`Dreams.cs:633`), so the others' inputs may stay forbidden. `docs/INTERACTIONS.md` gap 10 lists this as still open.
- **Fix:** count a step whose resolved target is the player (`targetUniqueObjects` contains `player`, or a target is the player's GameObject) as the player's own. Do the same in `LaterStep`, so a later player still gets it.
- **Verification:** a map step that runs a test `GameEvent` with a `runFunction` on the unique object `player` from the client and checks that only the client's player was affected. Re-run `map:later`.
- **Size:** S-M. **Related:** R04, R09-E.

### R06. Oxygen tank as a persistent flag; anyone can dive

- **Status:** not implemented
- **Evidence:** the dive is gated per player by holding `oxygentank_full` (R04). Whether a dive uses up the tank couldn't be read from the scene data offline. `Flags.SaveState` saves every `flagsDict` entry (`/tmp/darkwood-decomp/Flags.cs` ~27-46), but `Flags.setFlag` ignores names not in the game's database (`Flags.cs:164-176`).
- **Fix:** once any player holds a full tank, set a saved world flag. Once it is set, the `haveItem oxygentank_full` requirement passes for every player, and any step that would take the full tank away is skipped.
- **Verification:** map step: the client dives without a tank after the host got one, twice, and again after the world reloads. Manual: one refill, then repeated dives by host and client, then quit and reload.
- **Size:** M. **Related:** R04, R05.
- **Decisions needed:** listed below.

### R07. Tooltips for all items and map locations

- **Status:** not implemented. The gaps are in the game's own data. "Tubular magazine" is the vanilla `firecraft_tubularMagazine`, and its `_desc` entry is empty in every language.
- **Evidence:**
  - Item popup: `ItemPopup.show` reads `Items/<key>_desc` if `Language.HasKey` (`/tmp/darkwood-decomp/ItemPopup.cs:143-151`).
  - Map popup: `ItemPopup.show(string, Vector2, string)` from `Map.Update` (`Map.cs:152-169`, `ItemPopup.cs:608-621`), with no description lookup at all.
  - The localization sheets were carved out of `resources.assets` offline. Of the 378 items in `ItemsDatabase`, 131 have a description and **247 don't**. About 115 of those are real inventory items, 132 are journal items (notes, keys, photos) and 19 are nameless dev items.
  - **No map location has a description**: 55 scene names have a UI name, about 45 of them distinct.
  - The lists are kept in [`r07/items_missing_desc.tsv`](r07/items_missing_desc.tsv).
- **Fix:** an embedded table (key, text, draft flag) and a Harmony postfix on `Language.Get`/`HasKey` that fills an empty `Items/*_desc`, plus a map-popup prefix for locations. Stat lines come only from real `InvItem` fields (a runtime dump); purpose and lore are drafts for review.
- **Verification:** an in-game check that walks `ItemsDatabase` and the map's elements and fails on any missing text, run as a `run-test.sh` case.
- **Size:** M (L with journal items).

### R08. Hide HUD and inventory screens when the pause menu is open

- **Status:** not implemented
- **Evidence:** in co-op, `MenuEntry.PauseMenuOpened` (`src/DarkwoodMP/MenuEntry.cs:35-46`) keeps `CamMain` on and only dims the screen, so the world stays visible and running. The HUD under `Singleton<UI>` (bars, hotbar, clock, open inventories) shows through. `PauseMenuClosed` (`:48`) restores only the backdrop.
- **Fix:** when the pause menu opens in the world, record which HUD objects are active and switch them off; switch exactly those back on when it closes, and also when it is torn down.
- **Verification:** the `shots` test logs the HUD's state in the `pause-menu` and `world-after` shots and screenshots both. Manual: open a container, open the pause menu, check everything is hidden, resume, and check it is as before.
- **Size:** S-M. **Related:** R10, R03.

### R10. Player names show through cutscenes and menus

- **Status:** valid
- **Evidence:** name tags are IMGUI drawn from `Plugin.OnGUI` (`src/DarkwoodMP/Plugin.cs:1163` -> `Sync/PlayerSync.cs:160-181`). They check only the map and the epilogue: not the pause menu (`Core.mainMenu`), cutscenes (`Controller.playingCutscene`), `Core.HideUI`, `Player.inMenu()` (dialogue, journal, note), the video overlay or the black screens.
- **Fix:** a `UiState.WorldOverlaysHidden` helper, and an early return from `DrawNameTags` when it is set.
- **Verification:** the `shots` test logs whether tags are hidden in the `pause-menu` shot. Manual: two players; dialogue, journal, a story cutscene.
- **Size:** S. **Related:** R08.

### R09. Some story progression still requires the host

- **Status:** partially implemented. Several host-only (or "the actor loses it") paths remain, from reading the code.

How the mod sorts a joiner's trigger today: an `EventTriggers` trigger set off by our player (`ByOurPlayer`, `ScriptedEvents.cs:257-263`) becomes a request that the host checks (`TriggerAllowed`, `:809-819`). World triggers (`HostWorldTypes`, `:246-250`) run whole on the host.

| Trigger | Mod handling for a joiner | Host-only? |
|---|---|---|
| `area` (walk in) | request (`ScriptedEvents.cs:260, 891-905`) | no |
| `onActivate` (use) | request | no |
| `onExamine` | request (`INTERACTIONS.md:253` "not shared" is out of date) | no |
| `onEnterDialogue` / `onCloseDialogue`, `Events.fireWorldEvent` | request; `WorldEventPatch` (`:1039-1055`) | no |
| `onTakeInvItem` / `onPlaceItem` | request | no |
| `onOpenContainer` / `onCloseContainer` | request | no |
| `onUnlockPadlock` / `onTryToOpenLocked` | request | no |
| `onInSightOfPlayer` / `onOutOfSightOfPlayer` | request | no |
| `onEnterLocation` / `onExitLocation` | FarEvents | no |
| Chapter, epilogue, `endGame` | `Chapters` `StoryRequest` (0.26.0 queues it) | no |
| Flags, NPC state, reputation, journal | FlagsSync from any game | no |
| `onOpenDoor` / `onCloseDoor` / `onOpenCloseDoor` | **A**: fired by the host's replay | **yes, the actor loses it** |
| `onTurnOn` / `onTurnOff` | **A** (ItemOn replay, `WorldActions.cs:302, 677`) | **yes** |
| `onDisarmed` | **A** (the host's `disarm`, `WorldActions.cs:1186`) | probably yes |
| `onDeath` of an NPC or item | **B**: world trigger; the player part goes to the host's player | partly |
| `refreshWhenTurnedOn`, `onSpawned`, `onGameObjectActivated` | **B** | partly |
| `onTime` | world trigger (right, the world owns it), but **B** applies | partly, by design |
| `onGetAttacked(ByPlayer)` on a character | **C**: the forwarded hit runs it on the host, unshared | **yes** |
| `onGetAttacked` on an item, door or window | the replay fires it in every game | partly (duplicated) |
| `onBurn` | each game's own | no, but duplicated |
| `onPlayAttackAnimation` | host only (copies don't animate) | yes, minor |
| Level-up dreams and their outcomes | only the host's level starts them (`Progression.cs:357`, `DreamSync.cs:272`) | yes, by design |
| One-time trips owed to a later player | **D**: not rebuilt after a join or reload | yes, for joiners |
| Steps targeting the player (`runFunction`, `player_tweenShadow`) | **E**: shared, so they run on every player | the reverse (R05) |

The parts:

- **R09-A (M): triggers fired by a replayed shared action.**
  - When a joiner opens a door, the host replays it with `door.open` (`WorldActions.cs:319`), which fires `onOpenDoor` in the host's game. Because `Replaying` is set, `ByOurPlayer` is false (`ScriptedEvents.cs:259`), so the trigger runs as `Part.All` and its player steps go to the host's player. Relayed to the other joiners, it runs whole there too.
  - The joiner's own `EventFired` arrives later and is refused as "already happened" (`:813`). So the one player who opened the door gets nothing. The same goes for `onTurnOn`/`onTurnOff` and probably `onDisarmed`.
  - Fix: while replaying a shared action, the triggers it fires run only their shared part (or none), and the actor's own `EventFired` is the one that runs the event.
- **R09-B (M/L): world triggers give their player part to the host's player** (decided in `INTERACTIONS.md` gap 78). Example: a joiner kills an NPC whose `onDeath` gives an item. Fix: give the player part to the cause (the killer, the switcher).
- **R09-C (M): a joiner's hits on the host's characters.** `onGetAttackedByPlayer` runs on the host as `Part.All` and unshared (`NetEnemies.cs:513`; the joiner's copy never calls `getHit`, `CombatPatches.cs:393-415`).
- **R09-D (M): one-time trips owed to a later player are lost on a join or reload.** `Owed` is filled only when an `EventFired` arrives (`ScriptedEvents.cs:380, 750`), so a mid-game joiner can end up on the wrong side of a hole or ladder someone else used.
- **R09-E (S/M): steps sorted by type, not target.** This is the cause of R05.

**Verification:** map steps per part, using test-world triggers: a once-only `onOpenDoor` trigger with a message and an object step, opened by the joiner, must run the player's part in the joiner's game, the host's and shared part in the host's, and be refused nowhere. Manual: chapter 1 and 2 story beats played with the client leading.

**Size:** L in total. **Related:** R04, R05.

## Decisions needed

1. **R06 design.** (a) Does a player who never held the tank get one, or just pass the dive check? (b) Is finding the empty tank enough, or must it be refilled at the compressor? (c) Does the first-dive dream play once per player? Proposed: pass the check without an item; refill required; dream once per player (as 0.26.0 does for one-time trips).
2. **R07 scope.** (a) Mod text is English only: fill empty descriptions in every language, or only in English? (b) Are journal items (132) in scope? (c) Should stat lines also be appended to items that already have a game description? (d) Location descriptions can't come from game data, so they will mostly be drafts for review. Proposed: fill every language's empty descriptions with English text; inventory items only (journal items and dev items allow-listed); append stats only where the game's text has none; all location texts as drafts.
3. **R09-B** reverses an earlier decision (`INTERACTIONS.md` gap 78: world triggers' player part is the host's). Proposed: give it to the player who caused it.

## Proposed GitHub issues

Not created. One issue per report, R01-R10, with the title from the summary table and this file's entry as the body. R09 could be split into five issues (A-E).

## Progress (Phase 2)

(Filled in as fixes land.)

## Manual verification checklist

(Filled in as fixes land.)
