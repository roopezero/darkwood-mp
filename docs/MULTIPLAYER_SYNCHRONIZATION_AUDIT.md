# Multiplayer synchronization audit

Each section covers one area and is kept by whoever audits it. Status words as in
`MULTIPLAYER_ENGINEERING_LEDGER.md`: confirmed, likely, suspected, not tested.

## UI windows

The question: does a window that is open on one player's screen show another player's change to the object it
shows at once, without the viewer closing it, hovering, moving the mouse or clicking?

### How the game draws its windows

- **Slots** (any `Inventory` with its window open): each item's icon is a `UIInvItem` object made by
  `InvSlot.createInvItemIcon`. `InvSlot.createItem` makes it when the inventory is open, and `InvSlot.removeItem`
  destroys it and sets the slot's frame (`switchTexture`). So a slot changed in code while its window is open
  draws itself. The mod applies remote slot changes through `createItem`/`removeItem` (`SlotShare.Set`,
  `Inventory.SaveState.loadValues`), so the slots were already live.
- **Workbench recipes**: tinted red when they can't be made (`UIInvItem.refresh`, from `CraftingRequirement.met`).
  `met` counts what the player carries plus the open storage (`haveItemAmountInPlayer(.., includeAdditionalInventory)`),
  and is only worked out again by `CraftingRecipes.refresh`, which `Player.refreshRecipes` calls. In the single-player
  game moving things between the backpack and the storage never changes that total, so the game only calls it after
  crafting, repairing, upgrading and dropping. Another player's change to the storage does change it.
- **Saw**: the "Fuel: N" label (`InventoryLabel.rightText`) and the Convert button's enabled look are set by
  `Saw.refresh` and when the window opens; nothing redraws them every frame.
- **Fuel bar** of the generator or saw under the cursor (`EnemyHealthBar`): the value is set by `show`, which the game
  calls when the pointer reaches the object and from `Generator.addFuel` / `drainFuel` / `Saw.addFuel` while it's the
  selected object.
- **Trader**: the trade window shows the trader's `Inventory` (`DialogueWindow`: `npc.inventory.show()`).

### Coverage

| Window | How it draws | Remote change shown live, before | After | Test step (map_scenario_ui) | Two real games |
|---|---|---|---|---|---|
| Workbench storage slots (client views, host changes; and the reverse) | slot icons by createItem/removeItem | yes | yes | `ui-wb-set` / `ui-wb-burst` / `ui-wb-race`, then `ui-wb-check` on the viewer | yes, both directions |
| Workbench recipes' tint (can it be made) | cached `met`, redrawn by `Player.refreshRecipes` | **no**: 4 checks (each where a change made a recipe makeable or not) found stale tints | yes (`UiRefresh.Changed`) | `ui-wb-check` (compares each recipe's tint with what the player and storage hold now) | yes |
| Workbench upgrade button | `Workbench.refreshWorkbenchUpgrade` | level: yes (`SlotShare.OnLevel`); its requirements after a storage change: no | yes (through `Player.refreshRecipes`) | not checked on its own | no |
| Item upgrade menu at the workbench | `UpgradeItemMenu.refresh` | no (same cause as the recipes) | yes (through `Player.refreshRecipes`) | none | no |
| Saw "Fuel" label and Convert button | `Saw.refresh` | **no**: label stayed "Fuel: 0" with 10 and 15 fuel | yes (`UiRefresh.FuelChanged`, and `Changed` for its slots) | `ui-saw-open`, host `fuel`, `ui-saw-check` (twice) | yes |
| Generator / saw fuel bar under the cursor | `EnemyHealthBar.show` | generator: only once its own drain ticks while running; saw: no | yes (`show(.., onlyRefresh)` when it's the bar's object) | none (the test can't hold the pointer on it) | no |
| Trader's stock in the trade window (host restocks) | slots | **no**: the new stock closed the window (`Containers.Apply` hid any open container) | yes: loaded into the open window | `ui-trader-show`, host `trader-restock`, `ui-trader-check` | yes (window shown as the trade window shows it, not opened through the dialogue) |
| Other containers (chest, wardrobe, corpse, dropped item) | slots | one player at a time (the lock), so another player's change can't reach an open one, except the host's whole contents (a roll-back, a corpse's loot): those closed the window | loaded into the open window; on a client slot by slot (`SlotShare.MergeOpen`) | none | no |
| Several quick changes | slots | yes | yes | `ui-wb-burst` (x1..x5, 0.12 s apart), checked on the other game | yes |
| Close and reopen | slots, the host's contents asked for on open (`ContainerSlotsAsk`) | yes | yes | `ui-wb-close`, host change, `ui-wb-open`, `ui-wb-check` | yes |
| Both players change different slots at once | slots | yes | yes | `ui-wb-race` (epoch-synchronised), checked in both games | yes |

"Before" is the same module with `UI_BEFORE=1` (both games run `ui-refresh off`, which switches UiRefresh off).

### What the fix does (`World/UiRefresh.cs`)

- After a remote slot change is applied to an inventory that is open here (`SlotShare.OnSlots`, `OnRefused`,
  `Merge`; `Containers.ApplyOpen`), `UiRefresh.Changed` calls the game's own `Player.refreshRecipes` (recipes, upgrade
  button, upgrade menu, backpack, hotbar) and `Saw.refresh` for an open saw. After a remote fuel change
  (`FuelSync.OnFuel`), `UiRefresh.FuelChanged` calls `Saw.refresh` for an open saw and refreshes the fuel bar if it
  shows that object. Nothing runs when the window isn't open.
- `Containers.Apply` no longer closes an open container: when the slot count matches, it loads the contents into the
  open window (`ApplyOpen`). On a client that watches it slot by slot, the contents go through `SlotShare.MergeOpen`
  (OnContents' rules: a change of ours not yet sent stays if the host's slot still holds what we saw, and is undone
  otherwise), so a stale local view can't overwrite the host and nothing is lost or doubled. On the host, what our
  player changed since the last look is sent first, and the loaded contents become the watchers' starting point, so
  they aren't sent back. A different slot count still closes the window, as before.
- The redraws only redraw; they run with `WorldActions.Replaying` set and change no slot, so nothing is sent from
  them. No message or byte changed.

### Results (two real games, 2026-11-11)

- `UI_BEFORE=1 MAP_SCENARIO=map_scenario_ui tools/run-test.sh map`: 7 steps failed, e.g.
  `client ui-wb-check -4 wood 40 -5 nail 40 -6 wire 20 --makeable 1 -> fail the recipes are drawn stale: plank_nails drawn red, is makeable (woodx2 have 40,nailx10 have 43)`,
  `client ui-saw-check 10 -> fail fuel 10.0, label 'Fuel: 0' ... the label is stale (should be 'Fuel: 10')`,
  `client ui-trader-check -> fail nightTrader: 34 items, open=False ... the new stock closed his window`.
- `MAP_SCENARIO=map_scenario_ui tools/run-test.sh map` with the fix: every step passed (152 s), e.g.
  `client ui-saw-check 15 -> done fuel 15.0, label 'Fuel: 15'`, `client ui-trader-check -> done nightTrader: 33 items, open=True`.
  The dump comparison has one difference, not a UI one: the saw's `Item.searched` is true in the client's game and
  false in the host's (see below).
- `MAP_SCENARIO=map_scenario_world` afterwards: 0 dump differences; 3 steps failed (`pickup alcohol`,
  `put-item Wardrobe_low_small_01 alcohol`, `take-from Wardrobe_low_small_01`) because the alcohol they use is
  dropped by the full scenario's starter steps (`map_scenario.py`: take-item/drop-item), which the module doesn't run
  alone. Not caused by this change.

### Open

- **Fixed**: opening a container the game's way (`Item.openInventory`) set `Item.searched` (and `Character.searched` on
  a corpse) only in the opener's game. It's now sent as the WorldAction `ItemSearched` (`OpenSearchedPatch`); a
  client's goes to the host as a request, checked for reach and refused for a crate that isn't picked open yet.
  `map:ui` afterwards: every step passed, `dumps: SAME` (`items: 43 matched, 0 differences`), 145 s.
- Not shared, likely harmless: `Item.openInventory` also sets `inventory.RefreshingItems`, which makes the opener's
  game regenerate or drain the durability of items in that container. A container's contents go to the others
  while it's open (`Containers`), and the dump compares slot durability, but no test has an item like that in a
  container.
- Not tested in game: the fuel bar refresh, the upgrade button and item upgrade menu, the roll-back and corpse-loot
  paths of `Containers.ApplyOpen`, and the trade window opened through the dialogue itself.
- The item tooltip (`itemPopup`) over a slot another player just changed keeps the old item's text until the
  pointer moves. Not handled.

## Enemies: savages and animation

**Report (players, older version):** savages attacking a player looped one or two animation frames instead of
their attack; dogs and other enemies nearby didn't. **Status: not reproduced** on 0.20.0 (protocol 25) with two real
games, for any savage kind, in either game. One related copy bug was found and fixed (a copy's frame correction
didn't stick, below). The test is kept as a regression test.

### What the savages are

The `Villager*` kinds: `Villager`, `VillagerPitchfork`, `Villager3_plank`, `VillagerTorch`, `Villager1_Burning`
(and `Villager_pistol` in `CharacterType`, which has no prefab under `Characters/`). The `Villagers/Villager_infected*`
kinds stand still and never attack. `Redneck*` are the other human-shaped enemies.

### How an attack is animated (game code)

- One `tk2dSpriteAnimator` on the character's root (`Character.animator`) for both savages and dogs; no weapon or
  body-part animators were found on `Villager` or `Dog` (`sv-spawn` logs each character's animators). The game
  supports a `legsAnimator` that the main animator drives (`tk2dSpriteAnimator.legs`, `SetFrameInternal` sets the
  legs' clip and frame), but neither kind has one.
- `Character.processAnims` sets `clipToPlay` every frame; while `attacking` it is `attackAni` ("Attack1".."Attack9",
  picked in `chooseAttack`), and `animator.Play(clip)` doesn't restart a clip that is already playing.
- The attack ends only when its clip ends: the clips are `WrapMode.Once`, the animator's `AnimationCompleted` calls
  `OnAniFinish`, which for "AttackN" starts `startRecoveringFromAttack` and clears `attacking`. The hit itself is
  a frame event (`doAttack` -> `melee`, a pooled `MeleeSensor`).
- Differences from dogs, as sampled: `Villager` has Attack1 (20 frames) and Attack2 (18 frames) at 16 fps; `Dog`
  has Attack1 (10 frames) at 13 fps. During recovery a villager shows `IdleAggressive`, a **one-frame clip**, for
  about 1.1 s; a dog shows its 18-frame `Idle`. So a savage between attacks holds one pose by the game's design (in
  single player too); that isn't the bug, but it is the likeliest thing a player would describe as "stuck".

### How the mod copies it (Combat/NetEnemies.cs)

- Host, every 0.05 s (`StateInterval`): per enemy, up to 8 animators by path hash, each with clip name hash,
  `CurrentFrame` and `Playing` (`WriteState`).
- Client (`ApplyAnim`): the copy's frame events and completion callback are replaced (only "TweenShadows" plays, see
  `CopyFrameEvent`), so its frames can't attack or make sounds. A different clip is started with `PlayFromFrame` at
  the host's frame; for the same clip, a frame more than 2 off is set with `SetFrame(frame, triggerEvent: false)`;
  pause/resume follow the host's `Playing`. The client's animator otherwise runs on its own, so a Once clip plays
  through and stops on its last frame, as on the host.
- Things checked that could make a copy loop a couple of frames, and why they don't here:
  - Clip restarted on every update: only when the clip *name* differs, so a clip the host keeps playing isn't
    restarted.
  - `SetFrame` corrections (**confirmed bug, fixed**): `SetFrame` changes only the sprite shown, not the animator's
    clip time, so the next animator update went back to the copy's own time: a copy more than 2 frames off flicked
    to the host's frame for one frame on each update (every 0.05 s) and back, and never caught up. Seen in the full
    run on a world Rabbit, 3 frames ahead all through its Idle (`Rabbit #3 .: 51 % of 95 match (mismatches: ours
    Idle:10 host Idle:7 x4 ...)`); not on a savage. `ApplyAnim` now uses `PlayFromFrame(clip, frame)`, which moves
    the clip time; after it the same rounds matched 98-100%.
  - Two animators with the same path would be fed each other's clips alternately; savages and dogs have one.
  - A copy's attack progress doesn't depend on its frame events or `AnimationCompleted` (both off): the host's
    state sets the clip, and the animator plays it.
- Host side with a remote target: the host's savage attacking the client's stand-in (`NetPlayerProxy`) went
  Attack -> IdleAggressive (recovering) -> Attack again every ~2.3 s, playing each attack's frames through (e.g.
  `Attack2(1.1s 18/18f A>c44) > IdleAggressive(1.2s 1/1f R>c44) > Attack1(1.2s 20/20f A>c44)`): its attack's
  completion fires; it doesn't stall.

### The test (tools/tests/map_scenario_savage.py, World/MapScenarioSavage.cs)

`MAP_SCENARIO=map_scenario_savage tools/run-test.sh map` (two real games; `SV_ONLY=Kind1,Kind2` for some kinds).
For each kind (Villager, VillagerPitchfork, Villager3_plank, VillagerTorch, Villager1_Burning, Redneck03, and
DogMutated and Dog as the control) the host spawns it beside the client's player, then three rounds of 10-12 s
sampled every frame in both games at the same moment (`host+client sv-sample AT`): (a) it attacks the standing
client again and again, (b) the client walks 600 away mid-attack and back, (c) the host's player walks up and hits
it, so its target switches to the host, and walks back. Then three savages at once. Each game fails if a clip of
more than two frames showed only one or two different sprites while playing for a second or more; `sv-compare`
(client) fails if under 85% of the client's samples of an animator match the host's same clip within 2 frames
(or the same sprite) in the 0.5 s before, or if a clip the host played for 0.3 s or more never showed on the client.
The samples are in `$TMPDIR/dwmp-savage/<tag>-{host,client}.tsv` (time, net id, kind, animator path, clip, frame,
sprite id, frames, playing, host AI state and target).

### Results (two real games, 2026-11-11)

- `SV_ONLY=Villager,Dog`: PASS (178 s). Villager: 100% of 588 / 576 / 700 client samples matched the host's in
  rounds a / b / c; Dog: 100% of 568 / 589 / 695. No stuck clip in either game. Attack clips went through all their
  frames on both sides (client Villager: `Attack2(1.1s 18/18f) > IdleAggressive(1.2s 1/1f) > Attack1(1.3s 20/20f)`).
- Full module, before the fix (569 s): no stuck clip in either game for any kind. Client/host match 96-100% for every
  savage, Redneck03, DogMutated (and its Legs animator) and Dog in all rounds, three savages at once included.
  Failed only on the world Rabbit's 3-frame offset (rounds Villager3_plank-b 74%, VillagerTorch-b 51%).
  VillagerPitchfork, Villager3_plank, VillagerTorch, Villager1_Burning and Redneck03 have 2-4 attack clips of 15-101
  frames, all `Once`; DogMutated is the only kind sampled with a second (legs) animator.
- After the fix, `SV_ONLY=Villager3_plank,VillagerTorch,DogMutated`: PASS (234 s), every animator 97-100%; the
  rabbit 98-100%. The remaining 1-3% are clip changes that land between two state updates.
- Not run after the fix: `MAP_SCENARIO=map_scenario_enemies` (regression of the enemy fights).

### Not tested / open

- Real network latency and packet loss: both games ran on one machine. Over the internet the `SetFrame` flick
  described above is possible if a clip's timing drifts; it would look like a one-frame jitter, not a held loop.
- Savages at night, in water (`Attack*_Water`), or against a barricade or door; `Villager_pistol` (no prefab).
- The older version the report came from wasn't run, so whether it had a bug that was fixed since is unknown.
