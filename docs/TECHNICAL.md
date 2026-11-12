# How DarkwoodMP works

The design and the network protocol, for anyone reading or changing the code. [DEVELOPMENT.md](DEVELOPMENT.md) covers building and testing.

## Design

- **Host-authoritative, star topology.** The host runs the real simulation (enemies, the clock, the world's own changes). A client sends only what its own player does; the host checks it, applies it and relays it. Clients' frozen copies of the host's enemies never act.
- **Prediction and rollback.** A client's player acts at once in its own game, as a prediction; what it did goes to the host as a `Request`, which the host checks against its own world and answers with a `Verdict`. The host decides anything left to chance, and how much damage a hit does.
  - **Doors, windows, traps and switches:** the player must be within reach (600 units) and the action must fit the thing's state here (no opening a locked or broken door, no barricading one twice, no disarming a trap that's gone, no lockpicking a door that has no lock, no lock opened with a key that isn't in the journal, no padlock opened with the wrong code). The host rolls trap disarms and crate picks.
  - **Disarming a trap:** the host rolls the trap's chance, as the game would for its own player, and says how it went.
  - **Hits on the host's enemies:** the enemy must be alive and in reach (500 for melee, further for guns), at most 24 hits a second, or for a gun as many as its pellets times its shots a second (by the host's numbers for that gun), with half again as a margin. The damage is at most what the weapon can do: its damage or special damage with upgrades, doubled for the Strong skill, plus a margin. The blood shows at once; the host's damage follows.
  - **Containers:** a client's changes go slot by slot ("slot 3 held 2 ropes, now 1"). The host takes each only if the slot still holds what the player saw, the player is at the container, the container is open in the host's world (a locked crate the host hasn't seen picked open isn't), and nobody else has it open.
  - **Dragging and throwing:** only from where the player is.
  - **Movement:** a player may not cover more than 2000 units a second (running is about a quarter of that). One who does is put back where they last were (`PlayerCorrection`). Jumps of over 3000 units are the game's own (into a dream, a cave, home after dying) and are allowed.
  - **When the host refuses:** it first sends how the thing really is (`ObjState`), and the client puts its copy back that way. What the player's inventory gained or lost in that frame is undone: items used up come back, items gained go again. A refused container slot is undone item by item.
  - This is server reconciliation, not rollback as fighting games do it (re-simulating frames from the last confirmed input): that needs a deterministic game that can save and restore its whole state every frame, and Darkwood isn't one.
- **Loading a world** is faster with the mod, without changing what's loaded (`World/LoadSpeed.cs`):
  - Slow searches in the game's loading are done with an index instead: reading back the 21,000 saved objects (a dictionary walked from the start for each one), and placing about 9,000 objects on the culling grid. `--dwmp-verify-load` checks every object lands on the same grid nodes as with the game's own search.
  - The save's 11 MB of JSON is parsed on a background thread while the screen fades out, instead of after.
  - Each kind of saved object's prefab is looked up once, not once per object (19,000 objects, about 1,100 kinds).
  - On the test save, a world is ready about 10 s after leaving the menu, about 10 s sooner than without the mod. About 5 s of what's left are the game's own waits (the fade before loading, and 2 s after it with traps held off while things settle), which are left alone.
- **Wide screens** (`WideScreen.cs`): the game zooms its camera by the screen's width over 1920, which on 21:9 (3440x1440, 2560x1080) shows less of the world from top to bottom than 16:9 does. On screens wider than 16:9 the zoom follows the height instead, so they see as much from top to bottom as a 16:9 screen and more to the sides. A first start on such a monitor uses its own resolution, not the game's stretched 1920x1200. 16:9 and narrower screens are unchanged.
- **The mouse** stays in the game's window only while playing (`CursorLock.cs`). The game kept it there in its menus too, which on two monitors meant it couldn't leave the main menu.
- **Enemies on the host** are found as they're enabled (`AllCharacters` in `Combat/NetEnemies.cs`), not by searching the whole scene twice a second, which cost about 50 ms each time (a stutter) in a loaded world. With a player in, the mod's own work is about 0.1 ms a frame (`--dwmp-profile-mod`).
- **Backends.**
  - `TcpBackend` (Direct): one TCP connection per player, with a reader thread that only queues messages, processed in the game's frame loop as goasis's `wire.Conn` does, and a writer thread that sends what the game queued, so the game's frames never wait on the network. The writer sends a `Ping` when it has had nothing to send for 3 seconds; that runs while a game is busy loading too, and 15 seconds of silence from a peer that pings closes its connection. Before Hello, a connection may send messages of at most 64 KiB, must say Hello within 10 seconds, and only 8 such connections are kept at once. It also answers goasis's UDP `info` query, so goasis's own `browser.Query` can read a DarkwoodMP server.
  - `SteamBackend`: lobbies plus `ISteamNetworkingMessages`. All Steamworks types stay inside it (with `Lobby` and `SteamTransport`), so the mod loads without Steam (GOG); the build fails if any other class names one, compiler-made ones included (a check in `DarkwoodMP.csproj` that reads the built DLL: fields, signatures, locals, instructions, catches, generic constraints and attributes). Steam takes at most 512 KiB in one message, so a longer reliable message goes as `SteamPiece` messages and is joined again on the other side; messages Steam has no room for yet wait in order and go out on later frames. The host is whoever owned the lobby when you entered it: Steam passes ownership on when the owner crashes, but a joiner never becomes the host.
- **Handshake** (goasis-style): `Hello` (protocol version, game, player key, password, mod version, name, the hash of the world the client has). The host answers `Refused` and closes, or, when the client hasn't got its world, `SaveOffer` (after a `JoinWait` while it makes the snapshot): the client downloads it in 256 KiB pieces (packed with Deflate), unpacks it and checks its SHA-256 on a worker thread, and says `Hello` again. Then `Welcome`. A client sends Hello again if nothing at all came back in 5 seconds, and the host treats a Hello from someone already in as a new join.
- **Worlds:** Darkwood's world generation isn't seeded, so the world itself is sent: the host's save (profile, `sav.dat`, `savs.dat`). The client loads it with the game's `baseSaveDirectory` redirected to the mod's cache.
- **Players:** each player's sprites (tk2d collection, sprite and transform) are sent 20 times a second. On the host, each remote player is also a `NetPlayerProxy`, a `CharBase` of the player faction on the player's layer, which the game's own AI sees and attacks. Each game also sends what its own player sounds and looks like beyond the sprites (`Sync/PlayerFx.cs`): the sounds the game plays on that player or from its own code (looping ones left out), batched at most 10 times a second, a gun's muzzle flash, blood splats when it's hurt, and its flames while it burns. Every other game plays them on that player's puppet.
- **Enemies:** the host gives each enemy a net ID. Clients adopt their copy from the save by save ID (only an ID from the save they loaded, for the same kind of creature; an NPC only while the host runs it as a hostile one, or for one a scripted event made in each game, the one of that kind near the host's), or create it from its prefab, freeze it, and drive it with the host's position and animation clip and frame. The client clears the copy's animation events, so it can't attack or make sounds of its own. The game targets "the last visible character in its view-range list", so threat works by ordering that list before each sight check. While the host sends a copy, it is taken out of the client's own culling (its grid node and its location's character list) and switched on, since the client culls only around its own player and a copy 1600 to 3000 away would otherwise be switched off and never shown again; it goes back into the culling when the host stops sending it.
- **World state:** calls are replayed (doors, traps, switches), states are sent (containers, as the game saves them), periodic diffs carry the story, and objects are matched across games by `ObjRef`. Each game culls only around its own players, so a change can reach an object that is switched off there: a door opened there swings open when its area is switched on again (a switched-off door loses the push that swings it), and an object another game moved goes into the culling node of its new place.

## Protocol

Every message is `u8 type` + payload, little-endian. Over TCP, each is framed with a `u32 length` first. `str` means a u16 length + UTF-8; `bigstr`, used for JSON (inventories, characters, the profile), a u32 length + UTF-8. The current protocol version is in `src/DarkwoodMP/Net/Packets.cs`, and that file's comments are the authoritative byte layouts.

| Type | Name | Direction | Payload |
|---|---|---|---|
| 1 | Hello | C→H | u16 version, str game, str key, str password, str modVersion, str name, 32 B world hash |
| 2 | Welcome | H→C | u64 your id |
| 3 | Refused | H→C | str reason |
| 4 | SaveOffer | H→C | 32 B hash, u32 size (to download), bigstr profile JSON, u8 n, n × (str file, u32 size), u8 packing (0 none, 1 Deflate), u32 unpacked size |
| 5 | NeedSave | C→H | 32 B hash, u32 offset, u32 length |
| 6 | SaveData | H→C | 32 B hash, u32 offset, u32 length, bytes |
| 10 | PlayerState | both | u64 owner, vec3, quat, u8 state (alive, watching, flashlight on, invisible, shadow ward, forest spirit ward, Friend / Enemy of the Forest), str animation library, str lit thing in hand, sprites, u8 more state (away in a separate location or a dream, in an after-night), u8 health %, str where (a location's name, `dream:` and a dream's), vec3 where the lit thing's flames are, f32 a hotbar lantern's light radius (0: none) |
| 11 | PlayerLeft | H→C | u64 id |
| 12 | PlayerInfo | H→C | u64 id, str name |
| 13 | PlayerDamage | H→C | f32 damage, u8 flags, str attacker, effects |
| 14–18 | EnemySpawn, EnemyState, EnemyDespawn, EnemyHit (C→H, in a Request, with the weapon), EnemySound | H→C | see `Packets.cs` |
| 19 | PlayerNoise | C→H | vec3, f32 range, f32 volume, u8 flags |
| 20 | TimeSync | H→C | u32 day, u16 minute |
| 21 | WorldAction | both | u8 action, ObjRef, f32 a, f32 b, u8 flags, str |
| 22 | ContainerState | both | ObjRef, bigstr contents (JSON) |
| 23 | ObjSpawn | both | i64 id, u8 flags, str prefab, vec3, quat, bigstr contents, i32 experience (a death bag); a client's placed thing (flags 1) is a request |
| 24–26 | CharacterRequest (C→H), CharacterSave (C→H), CharacterLoad (H→C) | | see `Packets.cs` |
| 27 | FlagsDelta | both | u16 n, n × (str key, str value) |
| 28–29 | ContainerLock (C→H), ContainerBusy (H→C) | | ObjRef, … |
| 30 | EnemyBlood | H→C | u32 id, f32 rotation, u8 in water |
| 31–32 | WorldReload, HostAway (the host went to its menu: the server stops) | H→C | (none) |
| 33–39 | ObjMove, JoinWait, ItemThrown, Burning (also C→H in a Request: a friend's flames lit it), ContainerSlots, ContainerSlotRefused, WorkbenchLevel | | see `Packets.cs` |
| 40 | Request | C→H | u32 seq, then a whole message the host must agree to |
| 41 | Verdict | H→C | u32 seq, u8 ok, str reason, str result |
| 42 | ObjState | H→C | ObjRef, u8 kind, the kind's state (to roll back to) |
| 43 | PlayerCorrection | H→C | vec3 (where you're put back) |
| 44 | Fuel | both | ObjRef, u8 what (generator, saw), f32 fuel, u8 running |
| 45–46 | TraderAsk (C→H), TraderStock (H→C) | | str npc, f32 x, f32 z (where he stands); str npc, bigstr contents (JSON) |
| 47 | EnemyExplosion | H→C | vec3, str sound, str effect prefab |
| 50 | PlayerSounds | both | u64 player, u8 n, n x (str audio id, f32 volume): the sounds on that player, at most 10 batches a second |
| 51 | PlayerShot | both | u64 player, str muzzle prefab, str muzzle particles, u8 flash, vec3 offset |
| 52 | PlayerHurt | both | u64 player, u8 what (1 blood, 2 burning, 3 not burning), then what's: blood prefab, height, yaw; flames prefab, fire sound |
| 62 | NightScenario | H→C | str scenario |
| 63 | NightEvent | H→C | u8 kind: 1 str scenario, str event, str where; 2 ObjRef generator, ObjRef lamp kept on |
| 64 | RandomObjects | H→C | u8 kind: 1 u16 n, n × (i64 id, u16 chunk, u16 object, vec3, str contents); 2 i64 id, str prefab, vec3 |
| 65 | Weather | H→C | u8 flags, 4 × f32 rain and lightning times, 4 × i32 fog times |
| 66 | Construct | both | ObjRef constructible, i32 option (from a client: a request) |
| 67 | EventFired | both | u8 kind (1 trigger, 2 world event, 3 a trigger the host's world set off), ObjRef triggers, u8 index, str world event (from a client: a request) |
| 68 | TalkLock | C→H | ObjRef NPC, u8 talking (1: a request, refused if someone else talks to them or let go under 1.5 s ago; 0 sent after the dialogue's FlagsDelta), str name (traders and the wolf are locked by name) |
| 70 | PlayerCue | C→H | u8 kind (0 a swing: enemies may dodge; 1 aiming: enemies get scared) |
| 78 | CombatMusic | H→C | u8 in combat (one of the host's enemies is chasing you) |
| 79 | EnemyEffect | H→C | u8 kind: 1 str prefab path, vec3, f32 y rotation; 2 str pooled FX name, vec3; 3 u32 enemy id, i32 emitter toggle |
| 80 | AfterNightEnded | H→C | nothing (the host's after-night is over) |
| 86 | ChapterStart | H→C | i32 chapter (the host is starting it: send your character, wait for WorldReload) |
| 87 | GameEnd | H→C | u8 kind (1 the epilogue, 2 the end screen): leave, show the ending, then the credits and the menu |
| 88 | StoryRequest | C→H | ObjRef GameEvents, u8 step index, u8 kind (1 a new chapter, 2 the epilogue, 3 the end screen), i32 chapter: a request; the host checks it and runs the step |
| 90 | EnemySensorThrown | H→C | u32 enemy, str ranged sensor name, f32 x, y, z, f32 yaw, f32 vx, vy, vz, f32 land x, z: a host enemy's ranged attack (`Character.rangedAttack`, the human spider's projectile) threw its sensor's prefab; clients make it from their copy's sensor and show it flying (it hurts nobody there and leaves nothing) |
| 115 | LocationAsk | C→H | u8 flags (1 a dream), str location: where does this location stand? |
| 116 | LocationSlot | H→C | str location, vec3 spot, i16 turn: load it there, turned so |
| 120 | PorterTransport | C→H | u8 hideout to empty: the porter moved our stuff; the host does it at the hideout we are at |
| 121 | PorterDelivered | H→C | u8 hideout emptied, u8 hideout delivered to, i64 package id, i64 spawner id, bigstr contents (JSON) |
| 122 | OvenLit | both | ObjRef oven lit, ObjRef previous oven (or none): a hideout oven and its shadow ward (a client's is a request) |
| 130 | DeathBagMarker | both | i64 spawned id of a death bag, vec3: the map marker of a bag left in a separate location |
| 135 | Ping | both | nothing: Direct only, a keepalive from the writer thread; it never reaches the session |
| 140 | CharacterDead | C→H | nothing: our character died for good (we watch the others); the host keeps it with our character, so rejoining gives CharacterLoad kind 4 (watch again) |
| 155 | HostIds | H→C | u8 flags (1: made before you joined), u16 n, n × (i64 id, str name, vec3 position): ids for what the world made by itself; a client ties each to its own copy (named as an ObjRef Spawned) |
| 160 | SkillSpend | C→H | u8 level, str good skill, str negative skill ("" none), u8 n, n × str skills had: a request to spend a skill point; the host checks the game's rules |
| 165 | NightObject | H→C | u8 kind (1 made, 2 gone, 3 made before you joined), i64 id, then (not for 2) str night scenario, str night event, u8 GameEvents, u8 step, vec3, quat, ObjRef parent, u8 trap armed (255: no trap): an object a night event's spawn step made on the host; clients make that step's prefab there, or tie their copy from the save |
| 170 | SteamPiece | both | Steam only: a piece of a reliable message over Steam's 512 KiB limit (the first piece starts with u32 the whole length); joined again before the session sees it |
| 180 | ProgressState | H→C | i32 shared essence, u8 shared level |
| 181 | EssenceAdd | C→H | i32 essence cooked (a request; refused while the shared bar is full) |
| 182 | LevelTaken | C→H | u8 our player's own level |
| 183 | SkillReset | C→H | nothing: a request to reset our skills (in our morning, once a day) |
| 184 | DreamStart | H→C | str dream preset: the host's level dream; go into it |
| 185 | DreamEnd | both | str outcome: the shared dream ends (a client's goes to the host, which passes it on) |
| 190 | JoinNotice | H→C | str name, u8 what (0 is joining, 1 stopped joining before getting in) |
| 250 | ViewRect | C→H | u16 half width, u16 half height of the area the client's game keeps running round its player |
| 200–201 | ContainerSlotsAsk (C→H), ContainerSlotsAll (H→C) | | ObjRef; ObjRef, bigstr contents (JSON): what a container holds on the host, as a client opens it and once its world has loaded |
| 202 | StorageSort (C→H, in a Request) | | ObjRef: Sort & Stack pressed at a workbench's storage; the host answers with ContainerSlots (every slot to the sender, the changed ones to the others) |
| 208 | EggDestroyed | both | ObjRef egg, f32 x, f32 z, str name: an egg was broken open (a client's is a request; the host checks reach) |
| 209 | InfectionCleared | both | u16 n, n × (ObjRef, f32 x, f32 z): infection burned away this frame (a client's is a request: its player's burning weapon, within reach) |
| 210 | InfectionSpawned | H→C | i64 spawned id, f32 x, y, z, ObjRef parent: a new patch of infection in the host's world |
| 216 | TraderTrade | both | str npc, f32 x, f32 z, u16 n × (str type, i32 count) taken, u16 m × (str type, i32 count) given, bigstr contents (JSON), i32 reputation change: a trade was accepted and this is the trader's stock now (a client's is a request; the host checks reach, that the stock adds up and that the trader's reputation pays for it) |
| 220 | PorterCalled | both | vec3 the porter's place: the porter whistle was blown there (a client's is a request; the host checks the time and that the player is there) |
| 230 | NpcPos | H→C | u16 n, n × (i32 save id, f32 x, f32 z): where the host's NPCs stand (those that moved, and all every 4 s); clients keep their copies there |
| 240 | NpcHit | C→H | in a Request: ObjRef NPC, then EnemyHit's bytes after its id: our player hit a friendly NPC; the host's copy takes the hit (capped by the weapon) and turns on us |
| 241 | NpcAttack | C→H | in a Request: ObjRef NPC: a scripted attack in our game sent it after our player; the host's copy comes after us instead |
| 235 | EnemyLanded | H→C | i64 spawned id, u32 enemy, str name, f32 x, y, z, f32 yaw: what a host enemy's projectile left where it landed (the swamper's splat spawner, the human spider's Fov_Mob_Spawner); made on clients with that id |
| 236 | EnemyProjectile | H→C | u32 enemy, str projectile (pooled prefab), f32 x, y, z, f32 yaw, f32 vx, vy, vz: a host enemy threw this; clients show it flying (it hurts nobody there and leaves nothing) |
| 237 | EnemyLandedGone | H→C | i64 spawned id: what an EnemyLanded made is gone on the host; clients remove theirs (their copies never end by themselves) |
| 100 | DoorLeaf | both | ObjRef door, f32 signed degrees from shut: an open door's leaf a player walked into came to rest there; the host checks the pusher is near and the door is open, and sends it to everyone, the pusher too (`World/DoorLeaf.cs`, DoorPush) |

The browser's UDP query is goasis's: the datagram `info` SEP END, answered with `INFO` SEP name SEP players SEP max SEP password SEP localchars SEP version SEP `game=darkwood` SEP `proto=N` [SEP `world=Day 3, chapter 1`] SEP END, where SEP is byte 0 and END is byte 237.
