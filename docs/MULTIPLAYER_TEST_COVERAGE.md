# Multiplayer test coverage: entities and mechanics

What the tests actually check, kind by kind, as of `main` f665e0e (0.20.0, protocol 25). Written from the test code
(2026-11-11, reliability sweep agent E); no test was run for this file. Where this disagrees with the README or
`docs/INTERACTIONS.md`, this file says what the test code does.

## Short answer: is every entity tested?

No. What is true:

- Every kind in `tools/tests/map_scenario_enemies.py` is spawned beside a real client and killed by it, but only the
  21 "core" kinds run by default. The other 14 run only with `EE_FULL=1`, and nothing records such a run.
- Of the 21 core kinds, 8 have movement in the client's game (`--moves`) and hits on the client's player (`--hurts`)
  asserted; Spider01 only movement; the kamikaze, the centipede and Villager_infected1 only whom they go for
  (`--targets`). The six animals, the worms and the big mutant pig only have to die.
  Spiders, the banshee, the kamikaze and the centipede never hurt the client in the test, and the test accepts that.
- Enemy **animation is never compared**. The host sends each enemy's animator clip, frame and playing flag, and the
  client plays them, but no dump section, no compare field and no map step reads an enemy's animator. Enemy **facing**
  isn't compared either: the rotation is sent but not dumped.
- **Joining while something is active** is tested only for things at rest: a door left open, a locked door, a
  barricade, the save's armed traps, the workbench's storage changed during the join, two NPCs spawned friendly, a dog
  standing far off, and (in a module that isn't part of `map`) one night event's mushrooms. Joining during a chase,
  a fire, a night attack, a dream or a hostile NPC's fight is not tested.
- Eight `CharacterType` kinds are spawned by no test (Kid, Lizard, Villager_pistol, Robber, Ghost, Tank, ChomperBlue,
  ChomperGreen). The forest spirit and shadow armour are tested on a Dog with the flag or component added, not on the
  game's real creatures.
- The scripted-peer tests in `tools/run-test.sh` pass when the game logs no exception and the user's saves are
  unchanged. Their behaviour lines are printed for a person to read; only `host-progress` and `host-chars` check
  results in code.

## Legend

| Mark | Meaning |
|---|---|
| **2G** | TESTED-2GAME: two real games (the `map` or `map4` test), and a step fails (`r.Fail`) or a dump compare (`tools/tests/mapcompare.py`) fails when it goes wrong. |
| **2G\*** | As 2G, but only when the step's module or flag is run on purpose (`EE_FULL=1`, `MAP_SCENARIO=map_scenario_night`, `..._skills`, `..._npcs`); the default `map` doesn't run it. |
| **2G~** | 2G, but the check is weak (named in the notes: info-only result, wide tolerance, or a stand-in object). |
| **SC** | TESTED-SCRIPTED: a real game against a Python peer. Unless a note says "asserted", the result is printed, not checked; the run passes on no exceptions and unchanged saves. |
| **SO** | SPAWN-ONLY: it appears in a test (and may be compared as present/alive), but this behaviour isn't asserted. |
| **CO** | CODE-ONLY: the mod implements it; no test exercises it. |
| none | NONE: neither the mod nor a test is known to handle it specifically. |
| n/a | Doesn't apply to this thing. |

What a two-game dump compare checks (`World/StateDump.cs`, `tools/tests/mapcompare.py`):

- `enemies`: per shared enemy, keyed by ObjRef: `alive`, `health` (hostile NPCs only), `loopSound`, `pos` (60 units
  tolerance); for corpses `searchable`, `walkThrough`, `loot`. An enemy only in the host's dump 3300 or more from the
  client's player is ignored. No rotation, no animator.
- `enemylooks` (`MapScenarioFixChecks.cs`): `inPlay`, health as a percentage while in play; for corpses the shadow
  (faded/shown) and emitting particle systems.
- `doors`, `windows`, `containers`, `dropped`, `items`, `traps`, `power`, `saw`, `built`, `traders`, `npcpos`,
  `journal`, `story`, `time`, plus `vines`, `shadows`, `deathbags`, `afternight` (`MapScenarioGaps.cs`),
  `infection` (`MapScenarioIssues.cs`), `nightobjects` (`MapScenarioNight.cs`).
- The full `map` compares every section once, after all `STEPS`; some modules also compare mid-way
  (`("both", "compare", WHO, SECTIONS)`), named in the notes below.

Columns used in the enemy tables: **Spawn** (the client gets a copy), **Desp** (the copy goes when the host's does,
or by range), **Move** (the copy moves in the client's game), **Face** (facing), **Anim** (clip/frame), **AI** (the
host runs it for a remote player: chases, wakes, flees), **Target** (goes for the client by threat), **Hits** (its
attack hurts the client's player), **Dmg** (the client's hits take its health on the host), **Death** (dead in both
games), **Corpse** (searchable, same loot), **map4** (three or more client games), **Save** (save/load or location
reload), **Join** (a client joining while it's active).

## Enemies

Core kinds: run by the default `map` (and `MAP_SCENARIO=map_scenario_enemies`). Each runs `ee-spawn`, `ee-fight KIND
FLAGS` (client), `ee-check [--targets]` (host), `ee-corpse` (client), `compare enemies`, `ee-clear`. `ee-check` gets
`--targets` only for kinds with `--hurts` or `--targets`.

| Kind | Spawn | Desp | Move | Face | Anim | AI | Target | Hits | Dmg | Death | Corpse | map4 | Save | Join | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ChomperRed | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | `--cut`: cut in half by a real swing, the ChomperHalf the host spawns is fought too |
| Banshee | 2G | CO | SO | CO | CO | SO | SO | none | 2G | 2G | 2G~ | none | none | none | `--minions`: its babies must die too. Scream and blackout reach a client only in SC (`host-combat` `scream`, a synthetic call) |
| Spider01 | 2G | CO | 2G | CO | CO | SO | SO | none | 2G | 2G | 2G~ | none | none | none | Comes at the client, never bites; `ee-solo Spider01` (EE_FULL) only logs whether it bites the host alone |
| HumanSpider | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | `--throws`: its EnemySensorThrown landing shows on the client. The projectile's flight (EnemyProjectile) was never seen (README) |
| HumanSpiderMinion | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | `--twice`: the pooled melee sensor hurts the client again (gap 74) |
| Kamikaze | 2G | CO | none | CO | CO | SO | 2G | none | 2G | 2G | 2G~ | none | none | none | Sleeps where it's put. Its explosion is never set off near a client; area damage is SC only (`--dwmp-test-blast`) |
| Swamper1 | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | Its projectile's landing (splat spawner) isn't asserted (no `--throws`) |
| Centipede | 2G | CO | none | CO | CO | SO | 2G | none | 2G | 2G | 2G~ | none | none | none | Sleeps; never hurts the client; `ee-solo Centipede` is info only |
| Redneck03 | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | |
| Villagers/Villager_infected1 | 2G | CO | n/a | CO | CO | SO | 2G | n/a | 2G | 2G | 2G~ | none | none | none | Immobile, no attack. "Faces the nearest player" is checked as its target, not its rotation |
| Villager | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | |
| VillagerTorch | 2G | CO | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | none | none | none | Setting the client alight isn't asserted |
| DogMutated | 2G | SC | 2G | CO | CO | 2G | 2G | 2G | 2G | 2G | 2G~ | 2G | none | none | `--twice`. map4: `mt-target` (goes for the bigger threat, not the nearer player), `mt-never c3`, `mt-dead`/`mt-corpse` in every game. `nl-herd`/`nl-motion` (2G\*, night module): stalls under 10% of moving frames. SC: `host-combat`, `host-authority`, `client-enemies` |
| FakeChars/Worms_enemy_01 | 2G | CO | SO | CO | CO | SO | SO | none | 2G | 2G | 2G~ | none | none | none | No death animation, so its body stays unsearchable (as in the game) |
| FakeChars/pig_big_mutant | 2G | CO | SO | CO | CO | SO | SO | none | 2G | 2G | 2G~ | none | none | none | |

Kinds run only with `EE_FULL=1` (2G\*; the same flags as above, no recorded run):

| Kind | Asserted when run | Notes |
|---|---|---|
| ChomperRed_small, ChomperBlack, Redneck, Redneck02, Villager1_Burning, Villager3_plank, VillagerPitchfork | moves, hurts, targets, death, corpse (2G\*) | |
| ChomperBride | moves, hurts twice, targets, death (2G\*) | |
| Spider02, Spider03_day | moves, death (2G\*) | no hit expected |
| Villager_infected2, _infected3, _infected4, _infected1_ch2 | targets, death (2G\*) | immobile |

The Dog (plain, from the save and spawned) is the kind the other modules use for everything else:

| Mechanic on a Dog | Mark | Where |
|---|---|---|
| Spawn and kill by each player, corpse searchable in both | 2G | `spawn Dog` + `kill-enemy` (world), final compare |
| Client hits, host kills, copy goes silent, corpse searched | 2G | `spawn-dog`, `attack-dog`, `dog-hurt`, `kill-dog`, `dead-dog`, `search-corpse` (issues) |
| Health bar follows the host, pre-death stage stays in the fight, corpse looks (shadow faded, particles off) | 2G | `fx-spawn`...`fx-corpse` (fixchecks), `enemylooks` |
| Switched on by a client far from the host, shown near it, hidden when gone (range) | 2G | `far-dog`, `far-dog-active`, `far-dog-shown`, `far-dog-gone`, `far-dog-hidden` (far) |
| In a separate location: sees the joiner, goes for it by threat, wakes by its noise, killed stays killed when the location is unloaded and reloaded | 2G | `loc-sight`, `loc-dog-target`, `loc-noise`, `loc-wake`, `loc-stopped`, `loc-dog-check dead` (locations) |
| Lures, soporific meat, gas bomb, noise heard | 2G | `i2-heard`, `i2-eats`, `i2-gassed` (items2) |
| Every gun by each player: hits accepted, damage lands on the host, shots shown | 2G | `i2-fire`, `i2-hurt`, `i2-shots-seen` (items2) |
| Loop sound stopped by destroySounds | 2G | `sweep-loop-stop` + `compare enemies` (sweep) |
| Near a joiner (not jumping back to its spawn) | 2G | `near-joiner` (fixchecks) |
| Joining while it exists | 2G~ | the save's dogs and `far-dog` (PRE_JOIN) are in the final compare; none is chasing or attacking at the join |

Other enemies and enemy mechanics:

| Thing | Mark | Notes |
|---|---|---|
| Hostile NPC Wolfman_att | 2G | `npc-spawn` (PRE_JOIN, friendly at join), `npc-friendly`, `npc-hit` (health drops on the host), `npc-host-runs FIRST client`, `npc-attacks-us`, `npc-kill`, `npc-dead` (story state), `compare enemies,story` (hostile) |
| Hostile NPC Doctor_confronted2 | 2G | `npc-provoke` (a scripted attack in the client's game), `npc-host-runs`, `npc-kill`, `npc-dead` |
| A hostile NPC already hostile when someone joins | none | both NPCs are friendly at the join |
| Shadow armour (chapter 2 night enemies) | 2G~ | `ee-shadow-spawn`/`ee-flashlight`/`ee-shadow-check`: a Dog with a `ShadowArmor` component added; the client's flashlight hurts it and it turns on the client. No real shadow-armoured creature |
| Forest spirit (ForestSpirit2) | 2G~ | `spirit leaves/stays` (fixchecks): a Dog with `forestSpirit = true` for one `checkStuff` call. The real spirit, its teleport and its spawn event are untested (INTERACTIONS gap 17) |
| Run-away activity runs from the nearest player | 2G | `ee-flee-activity` (a chicken runs from the client) |
| Night attackers per occupied hideout | 2G~ | `mt-night` (map4_multi) counts what the host spawned per hideout; that the attackers reach and fight the clients isn't asserted. SC: `host-combat --dwmp-test-night` |
| Night worms | SC | `host-combat --dwmp-test-night` (log); the map holds noon (`ee-day`) to keep them away |
| Enemy bomb, damage aura, flame on a client | SC | `host-combat` `blast,aura,flame,boom` (a test enemy bomb, `--dwmp-test-blast`) |
| Fliers (diving at a player), stationary shooter | CO | README: "fliers and shooters only read from the code". Raven is spawned and killed only |
| Day redneck ambush near a joiner | CO | INTERACTIONS gap 2: "no test plays a day at noon outdoors past day 2" |
| Larva eggs | 2G | `egg-break`, `egg-cull`, `egg-check --from-other` (issues), one culled far off in the client's game |
| Kid, Lizard, Villager_pistol, Robber, Ghost, Tank, ChomperBlue, ChomperGreen, ChomperRed_FOVmobility2 | none | in `CharacterType`; no test spawns them (they may have no prefab: `ee-spawn` would report "skip" and pass) |
| Excluded on purpose | n/a | Villager1Granny, clone_villager_01, Bride_running, larva_big_01, centipede_hole, LocationChars/RedneckBury (NPCs or props, see `map_scenario_enemies.py`) |

## Animals

All six are core kinds with no flags: `ee-fight` must kill them and `ee-check` finds them dead on the host.

| Kind | Spawn | Move | Anim | AI (flee etc.) | Hits | Dmg | Death | Corpse | map4 | Join | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Raven | 2G | SO | CO | SO | CO | 2G | 2G | 2G~ | none | none | A flier; its dive is untested |
| Pig | 2G | SO | CO | SO | n/a | 2G | 2G | 2G~ | none | none | |
| Chicken | 2G | SO | CO | 2G | n/a | 2G | 2G | 2G~ | none | none | Flees from the client in `ee-flee-activity` |
| Deer | 2G | SO | CO | SO | n/a | 2G | 2G | 2G~ | none | none | |
| Rabbit | 2G | SO | CO | SO | n/a | 2G | 2G | 2G~ | none | none | |
| Brat_babykury | 2G | SO | CO | SO | n/a | 2G | 2G | 2G~ | none | none | |

Animals treating a joiner by its perks (gap 18) and fleeing a joiner's ward: CO ("no such scripted scene").

## NPCs and traders

| Thing | Mark | Where / notes |
|---|---|---|
| Night trader: restock (host), a trade by each | 2G | `trader-restock`, `trade` (world), `traders` compare |
| One player at a time in a dialogue | 2G | `talk`, `talk --refused`, `talk-end` (gaps); map4: `mt-talk`, `mt-talk --refused`, `trade --race` (one wins), `traders` compared in every game |
| An NPC the host moves is moved for the client (NpcSync), talked to there | 2G | `npc-move`, `npc-talk` (npcs WALK), `npcpos` compare |
| The client's morning: the host's copy of its trader, a trade, gone when it walks out | 2G | `host-copy here/gone` (npcs MORNING) |
| Morning clock, one talker, a trade while the host walks out, mornings ending | 2G | `an-start`, `an-clock`, `trade-open`, `talk-free`, `an-check` (gaps) |
| The client kills the night trader: the dream is the killer's | 2G | `kill-trader`, `trader-dream` (fixchecks) |
| The client kills the morning trader: the host's copy dies | 2G\* | npcs `KILL`, only with `MAP_SCENARIO=map_scenario_npcs` |
| A follower follows the joiner who recruited it | 2G~ | `follow-recruit` checks the leader and target; the night trader never walks, so following isn't seen (`follow-go 300 0`/`follow-check` left out, npcs FOLLOW) |
| The porter whistle, refused when forged | 2G | `porter-forged`, `whistle`, `porter-came` (items1) |
| The porter moving a hideout's stuff to the asker's hideout | SC | `host-world` log only |
| The after-night wolf | SO | moved away (`wolf-away`) so the trader comes; never talked to |
| Other story NPCs, their dialogues and rewards | none | only the night trader, the wolfman and the doctor are used |

## World objects

| Thing | Interaction | Seen in the other game | Refused / authority | map4 | In the save the client loads | Joining while active | Where |
|---|---|---|---|---|---|---|---|
| Doors: open/close | 2G | 2G | n/a | 2G | 2G | 2G~ (open door, at rest) | `toggle-door` (map, map4_scenario), PRE_JOIN `toggle-door open` |
| Doors: locks, lockpick, key | 2G | 2G | 2G (`door-key --forged`) | none | 2G (`door-lock 2`) | n/a | `door-pick`, `door-lock-key`, `door-key`, `door-locked` (world, items1) |
| Doors: barricade build, hit, break | 2G | 2G | none | none | none | none | `door-barricade`, `door-hit` (world) |
| Doors: leaf pushed | 2G | 2G | n/a | none | n/a | none | `sweep-push-door`, `sweep-door-leaf` (sweep) |
| Doors: changed while far off, drawn open later | 2G | 2G | n/a | 2G | n/a | n/a | `far-door`, `far-door-check` (far), `mt-door-drawn` (map4_multi) |
| Doors: padlock code | SC | SC | none | none | none | none | `host-world` log |
| Windows: barricade, hit | 2G | 2G | none | none | 2G (`window-barricade 2`) | n/a | `window-barricade`, `window-hit` (world) |
| Containers: put, take | 2G | 2G | 2G | 2G | 2G | none | `put-item`, `take-from` (world); `mt-grab` three at once, one wins (map4_multi) |
| Containers: one user at a time, lock released on leave | 2G | 2G | 2G | 2G | n/a | none | `hold-open`, `open-busy`, `release` (world); `mt-leave`, `mt-open-free` (map4 POST) |
| Crates: picked open (progress bar), locked crate lockpicked | 2G | 2G | 2G (`--refused` until picked) | none | n/a | none | `pick-crate` (world) |
| Workbench storage | 2G | 2G | 2G | none | 2G | 2G (`wb-fill` changes it during the join) | `wb-use`, `wb-host` (issues); Sort & Stack SC (`host-world`) |
| Rotting food in an open container | 2G | 2G | 2G | none | 2G | none | `rot-open`, `rot-host` (gaps) |
| Dropped items: drop, pick up, held flare/match dropped | 2G | 2G | none | 2G | n/a | none | `drop-item`, `pickup` (world), `drop-held`, `expect-dropped` (fixchecks) |
| Death bag (left in a separate location) | 2G~ | 2G | n/a | none | n/a | none | `bag-drop`, `bag-check`, `bag-take` (gaps): dropped "as from a separate location", not by a real death |
| Traps: place (both), disarm, step in, chain trap, broken glass | 2G | 2G | 2G (`trap-place-far`, `place-forged`) | none | 2G~ (save's traps in the final `traps` compare) | none (no trap sprung or being disarmed at the join) | `trap-place`, `trap-disarm`, `trap-fire` (world, items1) |
| Traps: disarm crouch | 2G | 2G | n/a | none | n/a | none | `disarm-crouch`, `puppet-crouch`, `disarm-finish` (status). The only step that compares animation frames (the player puppet's) |
| Explosives: barrel, gas bottle | 2G | 2G (final compare) | none | none | n/a | none | `explode`, `ignite` (world) |
| Fire: doors set alight, molotovs on furniture and on open ground | 2G | 2G | none | none | none | none | `door-burn` + `expect-burning`, `throw-molotov`, `i2-throw molotov` + `i2-seen ... fire` |
| Fire: burning down, spreading, gasoline trails | SC | SC | none | none | none | none | `host-world` (trail lit, log). Final dumps come after `wait 12` with fires out |
| Infection patches: burned, cleared with a torch | 2G | 2G | n/a | none | 2G (`issue-setup`) | none | `burn-infection`, `swing-infection`, `infection-check --from-other` (issues) |
| Vines | 2G | 2G | n/a | none | n/a | none | `vine-make`, `vine-cut`, `vine-check` (gaps) |
| Shadow on an item | 2G | 2G | n/a | none | 2G (`gaps-setup`) | 2G~ (it exists at join) | `shadow-hit`, `shadow-check` (gaps) |
| Furniture: placed, hit, broken; made by the world in each game (host ids) | 2G | 2G | n/a | none | n/a | none | `place-furniture`, `hit`, `world-furniture`, `ids-tied`, `ids-hit` (world) |
| Dragged furniture | 2G | 2G (final `items` compare) | none | none | n/a | none | `drag` (world). `far-drag`/`far-obj-shown` exist but aren't run (no draggable on its own culling node) |
| Generator: fuel, on; lamps switched on | 2G | 2G (`power`) | none | none | n/a | none | `fuel`, `switch` (world); a far generator drained by the client's skill: `fe-generator` (farevents) |
| Saw: fuel | 2G | 2G (`saw`) | none | none | n/a | none | `fuel saw_stationary_01` (world). Cutting planks (`Saw.convert`): SC/CO |
| InteractiveItem switches (candles, radio, stove) | 2G | 2G | n/a | none | n/a | none | `ii-switch`, `ii-check --fired/--not-fired` (fixchecks) |
| Lantern light on the wearer's puppet | 2G | 2G | n/a | none | n/a | none | `i2-lantern`, `i2-lit` (items2) |
| Hideout oven lit, its shadow ward | SC | SC | none | none | none | none | `host-world` log |
| Feeders | SC | SC | none | none | none | none | `host-world` log |
| Building (Constructible) | SC | SC | none | none | 2G~ (`built` compares the save's state) | none | `host-world` "building ... option 0" (log). A `use` map step can build, but no scenario calls it |
| Crafting at the workbench, repairing | none | none | none | none | n/a | none | no step crafts or repairs |
| Map items (places into the other's journal and map) | 2G | 2G | n/a | none | n/a | n/a | `i2-map-pick`, `i2-map-use`, `i2-map-check` (items2) |
| Notes, keys, quest items in the journal | 2G | 2G | n/a | none | n/a | n/a | `journal` (world), `journal-pick/add/remove/has` (gaps), `journal` compare |
| Throwables: noise lure, meat, gas bomb, flare, stone | 2G | 2G | n/a | none | n/a | n/a | `i2-throw`, `i2-seen` (items2) |

## Systems

| System | Mark | Where / notes |
|---|---|---|
| Clock (host's), time skips | 2G | `time +1/+2`, final `time` compare; held at noon by `ee-day` |
| Slow motion in co-op | 2G | `slowmo` (fixchecks) |
| Weather | SC | `host-world` log |
| Story flags, reputation, flags set while sharing is paused | 2G | `flag`, `reputation` (world), `sweep-flag-paused` + `compare story` (sweep), `flags` (issues) |
| A client's death sets story flags in every game | 2G | `mt-die` + `compare doors` (map4_multi) |
| Night events' objects (mushrooms, lights) | 2G\* | `nl-event` + `compare nightobjects` (night module only); joining with them: PRE_JOIN `CEvent_spawn_Mushroom` |
| A whole night with a real joiner (events, knocking, attackers, worms) | none | INTERACTIONS gap 2: "Not tested: a whole night with a real joiner" |
| Morning after the night (after-night) | 2G | gaps `an-*`, npcs MORNING, `afternight` section |
| Dreams: the clock waits; the trader's dream; the host's level dream dreamt together | 2G | `dream`, `clock-check` (gaps), `trader-dream` (fixchecks), `level-dream`, `in-host-dream`, `back-from-dream` (dreams POST, after the dumps) |
| Joining while someone is in a dream | none | |
| Separate locations: going in, its enemies run by the host, saving, stopping when empty | 2G | locations module; `loc-save` checks the save; farevents `fe-*`, `fe-loading` |
| Location events by who is there | 2G | `fe-check-host`, `fe-check-client`, `fe-churn`, `fe-stay`, `fe-left` (farevents) |
| Chapters (host's and a joiner's), the end, the epilogue | SC | `host-chapter`, `client-chapter`, `host-end`, `client-end`, `client-epilogue` (log; `storytest.py` has no checks) |
| Shared essence and levels, skill points, skill reset in the morning | 2G\* + SC | skills module only (`skills-learn --refused`, `skills-reset off/on`); `host-progress` (asserted); `progress-shared` (dreams POST, 2G) |
| Per-player characters | SC | `host-chars` (asserted: a permadead character comes back dead), `client-chars`; `check-equip` (issues, 2G) |
| Death for good and spectating | 2G | `permadeath`, `sees-spectator` (status POST, after the dumps) |
| The host's death with a player in the world | SC | `host-death` (log) |
| Host away, reload, main menu, end; client drop | SC | `host-elsewhere`, `host-reload`, `host-menu`, `client-away`, `client-drop`, `client-reload` (log) |
| Joining: loading screen, placed apart, prologue wait, snapshot stop, LAN search | 2G | `join-log`, `join-apart`, `join-waited`, `join-stop`, `lan-search` |
| Handshake refusals | SC | `handshake` (`tcptest.py`, log) |
| Authority: requests allowed or refused, rollback | 2G + SC | the `--refused`/`--forged` map steps above; `host-authority`, `client-authority` (log) |
| Save/load of the shared world while a client is connected | none | no step saves and reloads the host's world and compares it; `host-reload` is SC and only logs the WorldReload |

## Enemy animation and facing (question 3)

- Implemented: `Combat/NetEnemies.cs` sends up to 8 sprite animators per enemy (clip hash, frame, playing; around
  line 401) and the client applies them with `ApplyAnim` (around line 1071), with frame events cleared so a copy
  can't attack or make sounds.
- Not asserted anywhere: `StateDump` has no animator field for enemies, `mapcompare.py` compares none, no
  `MapScenario*.cs` step reads an enemy's animator, and the scripted `client-enemies` (`worldtest.py --enemies`)
  sends `EnemyState` with zero animators, so it doesn't exercise the path either.
- The death animation is covered only by its result: the corpse settles (`walkThrough`, `searchable`) and its shadow
  fades (`enemylooks`).
- The only animation assertions in the suite are the player's: `disarm-crouch` (own torso clip `InventoryLoop`)
  and `puppet-crouch` (the puppet's sprite ids matched against the `InventoryGet`/`InventoryLoop` clips).
- Facing: the rotation is sent in `EnemyState` but not dumped. The infected villagers' "whom they face" is checked as
  their target (`ee-check --targets`), not as a rotation.

## Joining while it's active (question 3)

| Active thing at the join | Mark | Evidence |
|---|---|---|
| Enemy existing, at rest or wandering | 2G~ | the save's dogs; `far-dog off:0,2400` in PRE_JOIN, then `far-dog-active`; final compare |
| Enemy chasing or attacking a player | none | |
| Hostile NPC already turned | none | `npc-spawn` in PRE_JOIN leaves both friendly |
| Armed traps | 2G~ | the save's traps, final `traps` compare |
| A trap sprung or being disarmed | none | |
| Fire burning (door, furniture, gasoline) | none | no PRE_JOIN step lights anything |
| Night event objects | 2G\* | `map_scenario_night.py` PRE_JOIN `nl-event CEvent_spawn_Mushroom`, then `compare nightobjects`; not in the default `map` |
| A night attack in progress, worms | none | |
| A container being changed | 2G | `wb-fill` changes the workbench storage while the client joins, `wb-use` |
| A dream or a morning in progress | none | |
| The host in the prologue | 2G | `hold-joiners 15`, `join-waited` |

## Known flaky or failing

- The save's awake dog (Dog#14) patrols near the test centre and sometimes kills the client at the rot wardrobe or
  the egg station (`docs/MULTIPLAYER_ENGINEERING_LEDGER.md`). A death changes the story flags, so the steps after it
  and the final `story` compare fail. The README says the 0.20.0 full `map` fails this way; the 0.19.0 code does too.
  So no clean full `map` pass on 0.20.0 is on record; `map_scenario_night` and `map_scenario_skills` passed alone.
- The 0.19.0 full map run had 3 failed steps, all the burst rifle's reload (fixed, then that scenario was run again).
- `ee-spawn` reports "skip" and passes when a kind has no prefab or is an NPC; `ee-corpse` reports "not searchable"
  as info, not a failure; `ee-solo` is info only. These can hide a regression.

## INTERACTIONS.md rows still partial or open

From `docs/INTERACTIONS.md` (gap numbers): 2 (night and day events fire only where the host is; a whole night with a
real joiner not tested), 10 (a scripted step's target can still be "the player"), 14 (random world objects, read and
partly tested), 17 (an activity's run-away with no target runs from the host; no test with a real banshee or forest
spirit near a joiner), 18 (wards and perks: no enemy is seen fleeing a joiner's ward), 26 (chapter changes by a joiner:
mostly), 38 (each game spawns its own after-night trader), 45 (fliers' altitude shadows, `TweenShadows`, shadow
armour looks not tested). Rows in the tables marked Partly include: the stationary shooter, banshee sight, forest
spirit teleport and spawn, shadow armour, gasoline trails, an explosive set off by a shot (not shared), flashlight and
torch light on the puppet, keys and quest items left lying for the others, and the death bag picked up without
opening it.

Out of date in INTERACTIONS.md: gap 22 says "no banshee in the tests"; the enemies module now spawns one (it only has
to die there).

## Top 10 gaps by multiplayer risk, with a test for each

1. **Enemy animation never compared.** Every client sees every enemy, and a wrong or frozen clip (attack, death) is
   what players notice first. Test: a dump section `enemyclips` (in `MapScenarioEnemies.cs`, beside the steps) that
   records, by a postfix on `tk2dSpriteAnimator.Play` for shared enemies, the set of clip names each one played since
   it spawned, and the current clip of a corpse. Add `("both", "compare", "client", "enemies,enemyclips")` after each
   `ee-fight`. Fails when a clip the host played (an attack, the death) never played on the client, or a corpse
   rests on a different clip.
2. **Joining during a fight.** A joiner's first copies are made from the snapshot plus `EnemySpawn`; a chasing
   enemy's state (target, health, pre-death, burning) at that moment is untested. Test: a module
   `map_scenario_joinactive.py` whose PRE_JOIN has the host spawn a DogMutated that chases the host (`i2-safe on`) and
   a hostile Wolfman_att already turned on the host; AT_JOIN a client step `ja-copies` asserts within 3 s both are in
   play, moving at least 30, and 60 or less from the host's positions; then `compare enemies,story`.
3. **A night with a real joiner.** The night module isn't in `map`, and no step asserts that attackers or worms
   reach a client. Test: add `map_scenario_night` to the default battery and a host step `nl-attack` (in
   `MapScenarioNight.cs`) that starts the night with the client at its hideout and fails unless one of the night's
   attackers targets the client's proxy within 60 s; a client step reuses `ee-fight --moves --hurts` on it; then
   `compare enemies,nightobjects`.
4. **Attacks that never land in any test.** Spiders, the banshee's scream, the kamikaze's explosion, the centipede,
   fliers' dives, the stationary shooter and the swamper's projectile. Test: in `MapScenarioEnemies.cs`, a
   `ee-solo KIND --must-hurt` mode that fails when the kind doesn't hurt the host alone (to learn which kinds can
   attack at all in the test world), then for those kinds `ee-fight --hurts`; for the kamikaze, wake it with
   `loc-noise`-style noise by the client and assert damage from `EnemyAreaHits`; for the banshee, a client step that
   asserts the scream's `EnemyFx` effect was shown.
5. **Enemy facing.** A corpse or an immobile enemy facing the wrong way stays wrong. Test: add `yaw` to the
   `enemies` section (`StateDump.cs`) and a tolerance in `mapcompare.py` (`FIELD_TOL["yaw"]`, e.g. 30 degrees for
   enemies in play, 5 for corpses); the infected villagers' `ee-check` also compares the copy's yaw with the host's.
6. **Fire at the join and fire's course.** Fire changes doors, furniture and health over time, and a late joiner's
   copy of a burning thing is untested. Test: PRE_JOIN `door-burn 3` and `throw-molotov Wardrobe_Big_1`; AT_JOIN
   `expect-burning door 3` on the client; after `wait 20`, `compare doors,items` (broken, health), so the burned-down
   result matches too.
7. **Save and reload with a client connected.** Corpses, loot, containers and traps after the host's world reloads
   are untested in two games. Test: a module that kills a Dog and takes from a container, then the host saves and
   reloads (`host-reload` path), waits for the client's rejoin (`World ready`) and runs `compare all`; fails on any
   lost corpse, loot or slot.
8. **Building, crafting and repairing.** Construction is SC (log only) and crafting/repairing has no test at all.
   Test: in `MapScenarioWorld.cs`, call the existing `use` step on a Constructible from the client
   (`("client", "use", "<constructible>")`) then `compare built`; add a `craft` step (client crafts at the workbench
   through the game's crafting call) asserting the item is in its bags and the workbench's storage lost the parts in
   both games (`compare containers`).
9. **Kinds that don't run or can skip.** 14 kinds need `EE_FULL=1`, eight `CharacterType` kinds have no test, and a
   missing prefab passes as "skip". Test: make `ee-spawn` fail on a skip for a kind in `KINDS` (keep the NPC skip as
   n/a), add an `EE_FULL` battery to `tools/run-test.sh` (agent D's list) and log its result in the ledger; probe the
   eight unspawned kinds once with `ee-spawn` to learn whether they exist.
10. **Stand-ins for the forest spirit and shadow armour.** Both are tested on a Dog, so the real creatures' teleport,
    spawn event and light damage are untested. Test: spawn `ForestSpirit2` through `CharacterSpawner.spawnForestSpirit`
    around the client's proxy (a host step in `MapScenarioFixChecks.cs`), assert its target is the proxy, that its
    position reaches the client within 60, and that it leaves when the client goes inside; for shadow armour, spawn a
    chapter 2 night kind that has `ShadowArmor` in its prefab and rerun `ee-flashlight`/`ee-shadow-check`.

Also worth fixing in the harness (agent D's files): the scripted tests should fail, not just print, when what they
wait for doesn't happen (`combattest.py` could exit 1 unless everything in UNTIL was seen; `worldobs.py` and
`storytest.py` have no checks at all).

## Sources

- Steps: `src/DarkwoodMP/World/MapScenario*.cs` (`r.Fail` conditions), scenarios: `tools/tests/map_scenario*.py`,
  `map4_scenario.py`, `map4_multi.py`.
- Dumps and compare: `src/DarkwoodMP/World/StateDump.cs`, the `AddSection` calls in `MapScenarioGaps.cs`,
  `MapScenarioIssues.cs`, `MapScenarioNight.cs`, `MapScenarioFixChecks.cs`; `tools/tests/mapcompare.py`.
- Scripted tests: `tools/run-test.sh` (the `check` function: exceptions and saves only), `tools/tests/*.py`,
  `src/DarkwoodMP/World/WorldTest.cs`.
- Enemy replication: `src/DarkwoodMP/Combat/NetEnemies.cs`. Kinds: the game's `CharacterType` enum.
- Status claims: `README.md`, `docs/INTERACTIONS.md`, `docs/MULTIPLAYER_ENGINEERING_LEDGER.md`, `git log`.
