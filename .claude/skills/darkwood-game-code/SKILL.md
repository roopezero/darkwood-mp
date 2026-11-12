---
name: darkwood-game-code
description: Decompile Darkwood's game code (Assembly-CSharp.dll) and find how the game does something (a system, a method, a field), with the gotchas that matter for patching it with Harmony. Use before patching or syncing any game behaviour in DarkwoodMP.
---

# Reading Darkwood's code

## Decompile (once per machine or game update)

```
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:~/.dotnet/tools:$PATH DOTNET_ROLL_FORWARD=Major
M=~/.local/share/Steam/steamapps/common/Darkwood/Darkwood_Data/Managed
D=/tmp/darkwood-decomp            # outside the repo: never commit decompiled code
[ -d "$D" ] || ilspycmd -p -o "$D" -r "$M" "$M/Assembly-CSharp.dll"
```

`ilspycmd` 8.2 targets .NET 6, and `DOTNET_ROLL_FORWARD=Major` lets it run on the installed .NET 8. If it's missing: `dotnet tool install -g ilspycmd --version 8.2.0.7535`. The result is about 1,100 `.cs` files, one per class, in the global namespace.

## Where things are

| System | Classes |
|---|---|
| The player | `Player` (9,000 lines, a singleton: `Player.Instance`), `CharBase` (shared with characters), `PlayerSkills` |
| Enemies, NPCs | `Character` (AI: `canSeeEnemy`, `checkForCharactersInViewRange`, `attackCharacter`, `heardSound`, `alertInArea`, `getHit`, `die`), `NPC`, `CharacterSpawner`, `WorldGenerator.respawnAllEnemies` |
| Hits | `MeleeSensor.OnTriggerEnter` (melee, both ways), `Bullet`, `Explodes`, `Burn`, `Trigger` (traps) |
| Saving | `SaveManager` (`baseSaveDirectory`, `Save`, `SavedObj`, `prefabDictionary`, `uniqueIdDict`), `SaveableObject` (`uniqueId`, `prefabName`), `X.SaveState` classes |
| World | `Door`, `Window`, `Inventory`/`InvSlot`/`InvItemClass`, `Item` (`turnOn`, `disarm`, `switchTriggerState`), `WorldGrid` (culling), `OutsideLocations`, `Dreams` |
| Time, story | `Controller` (`CurrentTime`, `day`, `refreshTime`, `skipDay`, `DoUpdateTime`), `Flags` (`flagsDict`, `npcStates`, `dialogues`) |
| Menus, loading | `MainMenu` (`displayProfilesMenu`), `UI.initLoadGame`, `Core` (`mainMenu`, `currentProfile`, `AddPrefab`, `returnToMainMenu`) |
| Sprites | tk2d: `tk2dBaseSprite` (`Collection`, `spriteId`, `scale`), `tk2dSpriteAnimator` (`CurrentClip`, `PlayFromFrame`) |

Search with `grep -n` on the decompile. To find what a big method does, filter it to the relevant names, for example `sed -n 'A,Bp' Character.cs | grep -n "target\|Player.Instance"`.

## Gotchas when patching

- **Overloads:** give Harmony the argument types (`[HarmonyPatch(typeof(X), nameof(X.m), new[] { typeof(float), ... })]`), or patch all of them with `TargetMethods()`. An ambiguous match once stopped the whole mod from loading.
- **Private methods:** `[HarmonyPatch(typeof(X), "name")]`. Call them with `AccessTools.Method(...).Invoke`. Read private fields with `AccessTools.FieldRefAccess<X, T>("field")`.
- **Coroutines:** an `IEnumerator` method's body runs in a nested compiler-generated type (for example `Player.<onDeath>d__N.MoveNext`). Find it on the stack by `DeclaringType == typeof(Player)` and a name containing `onDeath`.
- **Shadowed names:** the game has its own global `RandomNumberGenerator` and `Button`; qualify .NET's (`System.Security.Cryptography.RandomNumberGenerator`).
- **Unity lifecycle:** `Awake` and `OnEnable` run as soon as a prefab is created (`Core.AddPrefab` takes paths under `Resources/Prefabs/`), and `Start` runs on the next frame. `Character` subscribes to its animators' events in `OnEnable`.
- **Save state classes only set fields:** visuals come from later initialisation, so on a live object, replay the game's own methods instead of loading its state.
- **Things that look odd but are true:** a new day starts at dawn (`startDay`), not at midnight. `Save()` refuses at night unless `force`. Characters are found by `Physics.OverlapSphere` on layers 11 and 21 and must be living `CharBase`s of a faction they attack. `CharBase.updateHealthBar` does nothing for characters.
