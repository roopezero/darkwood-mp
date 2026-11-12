using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DarkwoodMP.Net
{
    /// <summary>
    /// Message types: the first byte of every message's payload.
    ///
    /// Modelled on goasis's package wire. Over TCP every message is framed as
    ///   u32 length (bytes that follow), u8 type, payload        — all little-endian
    /// and payloads are built from u8/u16/u32/u64, f32, str (u16 length + UTF-8 bytes), bigstr (u32 length + UTF-8, for JSON), blob (u16 length + bytes),
    /// big (u32 length + bytes) and vec3 (3 x f32). Steam carries the same messages, one per Steam message.
    ///
    /// A session: the client sends Hello. The host answers Refused (and closes); or, if the client hasn't got the
    /// host's world (its save hash differs), SaveOffer, as goasis's PackOffer: the client downloads the world a
    /// piece at a time with NeedSave, each answered with SaveData, checks it, and sends Hello again with its hash.
    /// Otherwise the host answers Welcome with the client's id, then a PlayerInfo for everyone already in, and the
    /// client loads the world. While they play, both sides send PlayerState.
    /// </summary>
    public enum MsgType : byte
    {
        /// <summary>u16 version, str game, str key, str password, str modVersion, str name, 32 bytes save hash (zeros: none yet).</summary>
        Hello = 1,
        /// <summary>u64 your id.</summary>
        Welcome = 2,
        /// <summary>str reason. The host closes the connection after it.</summary>
        Refused = 3,
        /// <summary>
        /// 32 bytes hash, u32 size (bytes to download), bigstr profile JSON, u8 n, n x (str file name, u32 size), u8 packing
        /// (0 none: the files back to back; 1 Deflate (RFC 1951) of them), u32 unpacked size (the files' sizes added up): the
        /// host's world. The hash is over the unpacked files (WorldFiles.Hash).
        /// </summary>
        SaveOffer = 4,
        /// <summary>32 bytes hash, u32 offset, u32 length: a piece of the offered world (offsets into the bytes as sent, packed).</summary>
        NeedSave = 5,
        /// <summary>32 bytes hash, u32 offset, big data (u32 length + bytes).</summary>
        SaveData = 6,
        /// <summary>
        /// A player's look and position: u64 owner, vec3 position, quat rotation, u8 state (1 alive, 2 watching others,
        /// 4 flashlight on, 8 invisible, 16 shadow ward, 32 forest spirit ward, 64 Friend of the Forest, 128 Enemy of the
        /// Forest: what the host's enemies check about the player they look at), str the body's animation library (the game's resource name, "" if none), str the type of
        /// the lit thing in hand (a torch, a lamp: its light and flames; "" if none), u8 n, n x sprite (see PlayerSnapshot.Write),
        /// u8 more state (1 away: in a separate location or a dream, 2 in an after-night: in their hideout after dawn), u8 health (percent of the maximum, 0 to 100), str where
        /// ("" in the shared world, the location's name in a separate location, "dream:" and its name in a dream; see World.Locations.Current),
        /// vec3 where the held light's flames are, relative to the player and in its rotation (zero if none; see PlayerSnapshot.HeldFlames),
        /// f32 the radius of a hotbar lantern's light around the player (0 if none; see PlayerSnapshot.Lantern).
        /// </summary>
        PlayerState = 10,
        /// <summary>u64 id: a player left.</summary>
        PlayerLeft = 11,
        /// <summary>u64 id, str name: a player in the session.</summary>
        PlayerInfo = 12,
        /// <summary>Host -> client: an enemy hit you (see Combat.PlayerDamage).</summary>
        PlayerDamage = 13,
        /// <summary>Host -> client: u32 id, i32 save id, str prefab path, str prefab name, vec3 pos, quat rot.</summary>
        EnemySpawn = 14,
        /// <summary>Host -> client: f32 the host's clock when sent (Time.unscaledTime; the client draws enemies by it, Combat.EnemyMotion), u16 n, n x (u32 id, vec3 pos, quat rot, u8 state (1 alive or in a pre-death stage, 2 asleep, 4 cut in half: Character.cuttingInHalf), f32 health, u8 m, m x (i32 animator, i32 clip, u16 frame, u8 playing)).</summary>
        EnemyState = 15,
        /// <summary>Host -> client: u32 id, u8 keep corpse.</summary>
        EnemyDespawn = 16,
        /// <summary>Client -> host: u32 id, f32 damage, u8 flags (1 cut in half, 2 by player, 4 interrupts, 8 normal hit), u8 n, n x effect (the weapon's: see Combat.PlayerDamage), str weapon (InvItemClass.SaveState JSON, "" bare hands). Sent inside a Request: the host decides the damage (at most what the weapon can do) and says it in the Verdict's result.</summary>
        EnemyHit = 17,
        /// <summary>Host -> client: u32 id, str audio id, f32 volume. One of the enemy's loops (CharacterSounds: idle, aggressive, defensive, sleeping, escaping) replaces the copy's loop; "" means its loop stopped (CharacterSounds.playIdleLoop with no loop).</summary>
        EnemySound = 18,
        /// <summary>Client -> host: vec3 pos, f32 range, f32 volume, u8 flags (1 dangerous, 2 gunshot): noise your character made.</summary>
        PlayerNoise = 19,
        /// <summary>Host -> clients: u32 day, u16 minute of the day.</summary>
        TimeSync = 20,
        /// <summary>Either way: u8 action, ObjRef, f32 a, f32 b, u8 flags, str s: a door, window, lock, padlock, crate, trap, switch, breakable item, explosion, feeder, pickup or NPC call (the actions are listed in World.WorldActions.Action).</summary>
        WorldAction = 21,
        /// <summary>Either way: ObjRef, bigstr contents (Inventory.SaveState as JSON) of a container.</summary>
        ContainerState = 22,
        /// <summary>Either way: i64 id, u8 flags (1 placed by a player), str prefab, vec3 pos, quat rot, bigstr contents, i32 experience (a death bag's DeathDrop.expAmount, else 0): something made while playing. A client's placed one (flags 1) comes in a Request: the host checks it's within reach and a prefab a player can place.</summary>
        ObjSpawn = 23,
        /// <summary>Client -> host, once in the world: send me my character.</summary>
        CharacterRequest = 24,
        /// <summary>Client -> host: bigstr Player.SaveState, bigstr inventory, bigstr hotbar (as JSON): my character now.</summary>
        CharacterSave = 25,
        /// <summary>Host -> client: u8 kind (1 yours, 0 new, 2 copy of the host's, 3 yours from an earlier chapter: stand beside the host, 4 yours, which died for good: watch the others), bigstr player, bigstr inventory, bigstr hotbar, vec3 where to stand (beside the host; kinds 0, 2 and 3).</summary>
        CharacterLoad = 26,
        /// <summary>Either way: u16 n, n x (str key, str value): story state that changed (see World.FlagsSync).</summary>
        FlagsDelta = 27,
        /// <summary>Client -> host: ObjRef, u8 open: I opened (1) or closed (0) this container.</summary>
        ContainerLock = 28,
        /// <summary>Host -> client: ObjRef, str who: someone else has it open.</summary>
        ContainerBusy = 29,
        /// <summary>Host -> client: u32 enemy id, f32 rotation, u8 in water: blood from a hit on it.</summary>
        EnemyBlood = 30,
        /// <summary>Host -> clients: my world changed (a new chapter, a reloaded save, back from the menu): load it again.</summary>
        WorldReload = 31,
        /// <summary>Host -> clients: I'm going back to the main menu and stopping the server; go back to yours.</summary>
        HostAway = 32,
        /// <summary>Either way: ObjRef, vec3 pos, quat rot, u8 flags (1 last: the drag ended here, or a pushed object came to rest here): an object a player is dragging or pushed (see World.DragSync).</summary>
        ObjMove = 33,
        /// <summary>Host -> client: str why. Not yet (the host is loading, or in the prologue or a dream): send Hello again in a few seconds. Also while the host makes its world for the joiner (WorldShare.Preparing): its SaveOffer follows by itself. And for a NeedSave of a world no longer offered: start again with Hello.</summary>
        JoinWait = 34,
        /// <summary>Either way: u64 thrower, str item type, vec3 pos, quat rot, vec3 velocity, vec3 land target, f32 distance (-1: dropped at the thrower's feet, Player.dropItem): something thrown or dropped (see World.ThrowSync).</summary>
        ItemThrown = 35,
        /// <summary>Host -> client: ObjRef, u8 what (1 enemy, 2 door, 3 window, 4 item), f32 burn time, u8 on: something started or stopped burning (see World.FireSync). Client -> host, inside a Request, the same bytes with on = 1: our player's flames set this door, window or item alight.</summary>
        Burning = 36,
        /// <summary>Either way: ObjRef, u8 n, n x (u16 slot, str what it held (its key: "type|amount|durability x1000|recipe|upgrades", "" empty), str what it holds now (InvItemClass.SaveState JSON, "" empty)): slots of the workbench's storage that changed (see World.SlotShare).</summary>
        ContainerSlots = 37,
        /// <summary>Host -> client: ObjRef, u16 slot, str what it holds (JSON), str what you saw there (key), str what you put there (JSON): your change came too late.</summary>
        ContainerSlotRefused = 38,
        /// <summary>Either way: i32 level: the workbench was upgraded.</summary>
        WorkbenchLevel = 39,
        /// <summary>Client -> host: ObjRef: what does this container hold? Sent as a client opens a container (before it sends any ContainerSlots for it), and for every workbench's storage once its world has loaded (see World.SlotShare).</summary>
        ContainerSlotsAsk = 0xC8,
        /// <summary>Host -> client: ObjRef, bigstr contents (Inventory.SaveState as JSON; "" when the host hasn't got it): the answer to ContainerSlotsAsk, what the client's slot changes are then compared with.</summary>
        ContainerSlotsAll = 0xC9,
        /// <summary>Client -> host, inside a Request: ObjRef: our player pressed Sort &amp; Stack at this workbench's storage. The host merges and sorts its own copy (World.SlotShare.Sort) and answers with ContainerSlots: every slot to the requester (also when it refuses, to roll back to), the changed slots to everyone else.</summary>
        StorageSort = 0xCA,
        /// <summary>Client -> host: u32 seq, then a whole message (u8 type + payload) that the host must agree to (see World.Authority).</summary>
        Request = 40,
        /// <summary>Host -> client: u32 seq, u8 ok, str reason (why it was refused, "" if not), str result (what the host decided: "" if nothing).</summary>
        Verdict = 41,
        /// <summary>Host -> client: ObjRef, u8 kind, then the kind's state (see WorldActions.SendState): how a thing really is, to roll a refused request back to.</summary>
        ObjState = 42,
        /// <summary>Host -> client: vec3 position: your player can't be where you say (moved too fast); you're put back here.</summary>
        PlayerCorrection = 43,
        /// <summary>
        /// Either way: ObjRef (u8 kind, i64 id), u8 what (1 generator, 2 saw), f32 fuel, u8 on (generators: running).
        /// From a client: its player poured fuel (the host checks reach and the amount, passes it on, or sends back how
        /// it is). From the host: how much a generator has and whether it runs, when that changed.
        /// </summary>
        Fuel = 44,
        /// <summary>Client -> host: str npc, f32 x, f32 z (where he stands here): the trader appeared here; what do they have? A host with no copy of a joiner's morning trader puts its own in that hideout (World.TraderSync).</summary>
        TraderAsk = 45,
        /// <summary>Host -> client: str npc, bigstr contents (Inventory.SaveState JSON): what the trader has.</summary>
        TraderStock = 46,
        /// <summary>Host -> client: vec3 position, str sound (the game's audio id, "" none), str effect (the explosion prefab's name, "" none): one of the host's enemies' explosions, for the sound and the flash (see Combat.EnemyAreaHits).</summary>
        EnemyExplosion = 47,
        /// <summary>
        /// Either way: u64 player (from a client: anything; the host puts in who sent it), u8 n, n x (str audio id,
        /// f32 volume): sounds that player's game played on its own player since the last one (steps, shots, reloads,
        /// hammering, pain), batched and sent at most 10 times a second (see Sync.PlayerFx). Played on their puppet.
        /// </summary>
        PlayerSounds = 50,
        /// <summary>
        /// Either way: u64 player, str muzzle prefab (in the FX pool, "" none), str muzzle particles (FX pool, "" none),
        /// u8 flash (1: FX/Muzzle/PistolFlash too), vec3 muzzle offset (x right, y up): that player fired a gun.
        /// </summary>
        PlayerShot = 51,
        /// <summary>
        /// Either way: u64 player, u8 what, then: 1 (bled): str prefab path (under Prefabs/), f32 height above their
        /// feet, f32 yaw; 2 (started burning): str flames prefab (FX pool, "" none), str fire sound ("" none);
        /// 3 (stopped burning): nothing. What hurting that player looked like, shown on their puppet.
        /// </summary>
        PlayerHurt = 52,
        /// <summary>Host -> client: str the night scenario the host chose ("" none). Sent when it changes and every 10 seconds (see World.WorldEvents).</summary>
        NightScenario = 62,
        /// <summary>
        /// Host -> client: u8 kind, then 1 (a night event fired): str scenario, str event (its CustomEvent's name),
        /// str the host's big location ("" none); or 2 (the shadows' lights): ObjRef generator, ObjRef the lamp that stays on.
        /// </summary>
        NightEvent = 63,
        /// <summary>
        /// Host -> client: u8 kind, then 1 (the dawn's random objects, all of them): u16 n, n x (i64 id, u16 chunk
        /// (index in WorldGenerator.worldChunks), u16 object (index in RandomWorldObjects.objects), vec3 position in the
        /// chunk, str contents (Inventory.SaveState JSON, "" none)); or 2 (a night mushroom): i64 id, str prefab, vec3 pos.
        /// </summary>
        RandomObjects = 64,
        /// <summary>Host -> client: u8 flags (1 rain today, 2 lightning before it), f32 rain start, f32 duration, f32 pre-rain lightning time, f32 next lightning, i32 fog in time, i32 fog in day, i32 fog out time, i32 fog out day (-1: none). When it changes and every 10 seconds.</summary>
        Weather = 65,
        /// <summary>
        /// Either way: ObjRef (u8 kind, i64 id) of the Constructible, i32 option: it was built or repaired with that
        /// option. From a client: its player wants to build it (a request; the host checks reach and that it can still
        /// be built; the client builds its copy only once the host agrees). From the host: build your copy
        /// (construct(manual: false, option), no ingredients).
        /// </summary>
        Construct = 66,
        /// <summary>
        /// Either way: u8 kind (1 a trigger a player set off, 2 a world event, 3 a trigger the host's world set off by
        /// itself), ObjRef (u8 kind, i64 id) of the EventTriggers (kind 2: none, 0 and 0), u8 the trigger's index in it,
        /// str the world event's name (kinds 1 and 3: ""). From a client (kinds 1 and 2): its player set off this
        /// scripted event (a request; the host checks it hasn't fired, reach and what it needs, then runs the host's and
        /// the shared steps; the client runs its own and the shared steps only once the host agrees). From the host: it
        /// fired; mark it fired and run the shared steps (kind 3: and the messages, if your player is near it). See
        /// World.ScriptedEvents.
        /// </summary>
        EventFired = 67,
        /// <summary>
        /// Client -> host: ObjRef (u8 kind, i64 id) of the NPC, u8 talking (1 our player's dialogue with them opened, as
        /// a request: refused with who it is if someone else talks to them or just did; 0 it closed, sent after the
        /// dialogue's FlagsDelta), str name (the NPC's name for one that is the same NPC in every game wherever they
        /// stand, a trader or the wolf; "" for others, which go by the ObjRef).
        /// </summary>
        TalkLock = 68,
        /// <summary>Host -> client: u8 in combat (1: one of the host's enemies is chasing your player, 0: none is). Sent when it changes, for the combat music (see Combat.EnemyFx).</summary>
        CombatMusic = 78,
        /// <summary>
        /// Host -> client: u8 kind, then 1 (a prefab): str path under Resources/Prefabs (as Core.AddPrefab takes it),
        /// vec3 position, f32 y rotation; or 2 (a pooled effect): str the FX pool's prefab name, vec3 position; or 3
        /// (an enemy's particle emitter): u32 enemy net ID, i32 toggle (Character.modifyEmitter). The look of what the
        /// host's enemies do (the banshee's scream, the forest spirit's teleport), see Combat.EnemyFx.
        /// </summary>
        EnemyEffect = 79,
        /// <summary>Host -> client: no payload. The host's after-night is over (its trader left, its clock runs again); end yours too (see Combat.EnemyFx).</summary>
        AfterNightEnded = 80,
        /// <summary>Client -> host: u8 kind (0 your player swings: enemies within 150 may dodge; 1 your player aims a weapon: enemies within 350 get scared). The host runs it at your proxy (see Combat.PlayerCues).</summary>
        PlayerCue = 70,
        /// <summary>
        /// Host -> client: i32 chapter. The host's game is starting that chapter (a chapter's GameEvent ran on the
        /// host, set off by its own player or a joiner's). Send your character now and wait under a black screen: a
        /// WorldReload follows once the host's new chapter is ready (see World.Chapters).
        /// </summary>
        ChapterStart = 86,
        /// <summary>
        /// Host -> client: u8 kind (1 the epilogue, 2 the game's end screen). The host's game reached the end and its
        /// server stops: leave, show the ending in your copy of the world (the epilogue, or the end screen), then the
        /// credits and the main menu (see World.Chapters).
        /// </summary>
        GameEnd = 87,
        /// <summary>
        /// Client -> host: ObjRef (u8 kind, i64 id) of the GameEvents, u8 the step's index in it, u8 kind (1 a new
        /// chapter, 2 the epilogue, 3 the end screen), i32 the chapter (kind 1). Our player's event reached a step
        /// that starts a chapter or ends the game, which our game didn't run (a request; the host checks it's the next
        /// chapter, or the last for the end, and that the player is near the event, or away in a place of their own
        /// if the host hasn't got it, then runs the step itself; see World.Chapters).
        /// </summary>
        StoryRequest = 88,
        /// <summary>
        /// Client -> host, a request: u8 the hideout (Location.hideoutId, 1-3) to take everything from. Our player
        /// had the porter move their stuff (Location.transportAllItemsToCurrentHideout, from a dialogue's world
        /// event); our game didn't do it. The host checks that the player's stand-in is at another hideout, does the
        /// move into a package at that hideout and sends PorterDelivered (see World.Hideouts).
        /// </summary>
        PorterTransport = 120,
        /// <summary>
        /// Host -> client: u8 the hideout emptied, u8 the hideout delivered to, i64 the package's spawned id (ObjRef
        /// Spawned), i64 its spawner's spawned id, bigstr contents (JSON Inventory.SaveState of the package). The porter
        /// took the unlocked containers' contents of the first hideout to the second: empty yours, make the hidden
        /// package there with these contents and the event that shows it later, and the porter leaves (World.Hideouts).
        /// </summary>
        PorterDelivered = 121,
        /// <summary>
        /// Either way (a client's is a request): ObjRef (u8 kind, i64 id) of a hideout oven (ExperienceMachine), then
        /// ObjRef of another oven (kind 0: none). From a client: its player lit the first (Player.setExperienceMachine)
        /// and the second was its oven before. From the host: the first is lit in every game, and the second is put
        /// out (the host decided nobody uses it as theirs any more), except in the game whose player still has it as
        /// theirs (see World.Hideouts).
        /// </summary>
        OvenLit = 122,
        /// <summary>
        /// Client -> host: u8 flags (1 a dream), str the location's name. Our player is about to load a separate
        /// location (or a dream) our game hasn't got: where does it stand? The host answers with LocationSlot (see
        /// World.Locations).
        /// </summary>
        LocationAsk = 115,
        /// <summary>
        /// Host -> client: str the location's name, vec3 its marker's spot, i16 its turn in degrees (what
        /// Core.getRandomHalfRotation would have rolled). Load that location there, turned so, as the host has it or
        /// will (see World.Locations).
        /// </summary>
        LocationSlot = 116,
        /// <summary>
        /// Either way (a client's is a request): ObjRef (u8 kind, i64 id) of the egg, f32 x, f32 z (where it stands,
        /// to find it by name if the ObjRef doesn't), str its GameObject's name. A larva egg (or another item broken
        /// open to get at its contents) was broken in the sender's game; break yours (see World.EggSync).
        /// </summary>
        EggDestroyed = 0xD0,
        /// <summary>
        /// Either way (a client's is a request): u16 n, then n times: ObjRef (u8 kind, i64 id), f32 x, f32 z. These
        /// patches of infection (the damaging ground fire clears) were burned away in the sender's game in one frame;
        /// clear yours (see World.InfectionSync). A client sends only what its player's burning weapon cleared.
        /// </summary>
        InfectionCleared = 0xD1,
        /// <summary>
        /// Host -> client: i64 spawned id, f32 x, f32 y, f32 z, ObjRef (u8 kind, i64 id) of its parent (a location;
        /// kind 0: none). A new patch of infection appeared in the host's world (it spreads, or a spider laid it);
        /// make yours with that id (ObjRef Spawned). Clients' infection never spreads by itself (see World.InfectionSync).
        /// </summary>
        InfectionSpawned = 0xD2,
        /// <summary>
        /// Either way (a client's is a request): str npc (the trader's name), f32 x, f32 z (where the trader stands in
        /// the sender's game), u16 n, then n times: str item type, i32 count (what the player took from the trader),
        /// u16 m, then m times: str item type, i32 count (what the player gave), bigstr contents (JSON Inventory.SaveState
        /// of the trader's stock after the trade), i32 reputation change (what the trade did to the trader's reputation:
        /// the value given less the value taken). A trade was accepted in the sender's game; the trader's stock is
        /// now this (the player's own items stay theirs), and each game adds the change to his reputation. The host
        /// checks a client's against its own trader's stock and reputation, relays it to the others, or sends its
        /// TraderStock back and refuses (see World.TraderSync).
        /// </summary>
        TraderTrade = 0xD8,
        /// <summary>
        /// Either way: i64 spawned id of a death bag (ObjSpawn), vec3 where its map marker stands. The player died in
        /// a separate location, so the bag lies there and the game put a marker (Objects/_Unique/deathDrop_marker) at
        /// the thing that took them in; make yours there and tie it to the bag, which removes it when it goes
        /// (DeathDrop.additionalMapMarker). The host passes a client's on (see World.Containers).
        /// </summary>
        DeathBagMarker = 130,
        /// <summary>
        /// Host to clients: ids for objects the world made by itself while players are joined (a location's
        /// characters, a delayed event's spawn...), which each game would give its own save id. u8 flags (1 = made
        /// before you joined: in the world you loaded), u16 count, then count times: i64 id, str the object's name,
        /// vec3 where it stands in the host's game. A client ties each id to its own object of that name that started
        /// within 30 s of the message, the nearest within 50 of that spot, and names it by that id as an ObjRef
        /// Spawned; one it hasn't tied it finds by that name and spot (see World.HostIds).
        /// </summary>
        HostIds = 155,
        /// <summary>
        /// Host -> client: u8 kind, i64 id, then for kinds 1 and 3: str night scenario, str night event, u8 which of
        /// its RandomEvent's GameEvents, u8 which step of it, vec3 where, f32 x, y, z, w rotation, ObjRef (u8 kind,
        /// i64 id) what it was made under (kind 0: nothing), u8 its Trigger armed (0 no, 1 yes, 255 it has none). Kind 1: a night event's gameObject / spawn step made this
        /// object on the host (a mushroom, lights, cockroaches...); the client makes that step's prefab there and names
        /// it by the id (ObjRef Spawned); again for one it has, when the trap it is was armed or disarmed. Kind 3: the same, made before this client came: its copy from the save, of
        /// that name within 50, is tied to the id, or else it's made. Kind 2 (only the id): the host's is gone, so is
        /// the client's. See World.NightObjects.
        /// </summary>
        NightObject = 165,
        /// <summary>
        /// Client -> host, no payload: our character died for good (Nightmare, or Hard with no lives left) and we
        /// watch the others now (World.Spectator). The host keeps that with our character (World.Characters), so
        /// coming back gives us CharacterLoad kind 4: we watch again, as the game doesn't bring a dead character back.
        /// </summary>
        CharacterDead = 140,
        /// <summary>
        /// Either way, Direct (TCP) only, no payload: a keepalive. Each side's writer thread sends one when the
        /// connection opens and again after 3 s with nothing else to send, even while the game is busy loading. A side
        /// that has had a Ping drops the connection after 15 s with nothing at all from it. It never reaches Session.
        /// </summary>
        Ping = 135,
        /// <summary>
        /// Either way, Steam only: a piece of one reliable message too long for Steam (it takes at most 512 KiB per
        /// message). The first piece is u32 the whole message's length, then its first bytes; each next piece is the
        /// next bytes, on the same channel, until that length is there. Then the whole message is handled as if it
        /// had come in one. SteamTransport makes and joins these; they never reach Session.
        /// </summary>
        SteamPiece = 170,
        /// <summary>
        /// Host -> client: i32 the shared essence (experience, counted from the start as Player.experience is), u8 the
        /// shared level (the most levels any player has taken). Everyone's essence bar shows the shared essence, up to
        /// the next level of their own; a full bar lets them take a level at the stove (see World.Progression).
        /// </summary>
        ProgressState = 180,
        /// <summary>
        /// Client -> host, a request: i32 the essence our player just cooked at a stove (InvItemClass.consumeExp). The
        /// host adds it to the shared essence, or refuses it when the shared bar is already full (World.Progression).
        /// </summary>
        EssenceAdd = 181,
        /// <summary>
        /// Client -> host: u8 our player's own level now (Player.currentLevel: after taking a level at the stove,
        /// after a skill reset, after its character came). The host raises the shared level to it, as far as the
        /// shared essence allows (World.Progression).
        /// </summary>
        LevelTaken = 182,
        /// <summary>
        /// Client -> host, a request, no payload: our player wants to reset their skills at the stove. The host allows
        /// it only in the player's morning (their PlayerState says they are in an after-night: after dawn, before
        /// they leave their hideout) and once per player per in-game day (Controller.day); the client resets once
        /// it's allowed (its skills taken back, its levels kept, so the points can be spent again).
        /// </summary>
        SkillReset = 183,
        /// <summary>
        /// Client -> host, a request: u8 our player's level (Player.currentLevel), str the good skill we learn, str
        /// the negative skill taken with it ("" for none), u8 n, then n times str a tier skill we had before (good
        /// or negative). The host checks the choice by the game's rules (World.SkillTree: the tier is open at that
        /// level, at most two good skills a tier, a negative one with the first skill while an open tier has none,
        /// a point left) and that the level isn't more than the shared essence gives; the client takes the skills
        /// back if it refuses.
        /// </summary>
        SkillSpend = 160,
        /// <summary>
        /// Host -> client: str the dream's preset name (DreamPreset). The host took a level that brings a dream
        /// (SkillsMenu.confirmSkills); every player goes into this same dream (World.DreamSync).
        /// </summary>
        DreamStart = 184,
        /// <summary>
        /// Either way: str the outcome (Dreams.outcome, e.g. "playerDeath"). The shared dream ends with this outcome.
        /// From a client: its dream ended (it died, or reached the end); the host ends its own with that outcome and
        /// passes it on. From the host: end yours with it (World.DreamSync).
        /// </summary>
        DreamEnd = 185,
        /// <summary>Host -> clients: str name, u8 what (0 is joining: the host let their Hello in and is getting its world to them; 1 stopped joining before they got in). "Joined" is the PlayerInfo after Welcome.</summary>
        JoinNotice = 190,
        /// <summary>
        /// Either way (a client's is a request): vec3 where the porter is to come (Location.porterPosition of the big
        /// location the whistler is in). From a client: its player blew the porter whistle there; its game didn't
        /// call the porter. The host checks it's day, the porter isn't in transit or dead, and that the player's
        /// stand-in is in that location, then calls the porter (Location.spawnPorter). From the host: the porter
        /// was called there; call it in your game too (World.Hideouts).
        /// </summary>
        PorterCalled = 220,

        /// <summary>
        /// Host -> clients: u16 n, then n times (i32 save id, f32 x, f32 z): where the host's NPCs are, for those that
        /// moved (and all of them for a new joiner). Clients keep their copies there (World.NpcSync).
        /// </summary>
        NpcPos = 230,

        /// <summary>Client -> host: ObjRef npc, f32 damage, u8 flags (as EnemyHit), u8 n, n x effect (as EnemyHit), str weapon (as EnemyHit). Our player hit an NPC the host isn't running as an enemy yet (it's still friendly there). Sent inside a Request: the host applies it to its copy with our stand-in as the attacker (at most what the weapon can do), which turns it on us there; from then on it's one of the host's enemies (Combat.HostileNpcs). Refused with "no such NPC here" when the host hasn't got it: the NPC is then ours, and the hit is applied here.</summary>
        NpcHit = 240,
        /// <summary>Client -> host: ObjRef npc. Something in our game (a dialogue's outcome, a scripted attack) sent this NPC after our player. Sent inside a Request: the host sends its copy after our stand-in instead, and runs it as one of its enemies (Combat.HostileNpcs).</summary>
        NpcAttack = 241,

        /// <summary>
        /// Client -> host: u16 half width, u16 half height: how far round its player the client's game keeps the
        /// world running (its cull rect, from its screen and zoom). The host keeps that much running round them
        /// (Combat.ViewRects); sent once in the world and again when it changes.
        /// </summary>
        ViewRect = 250,

        /// <summary>
        /// Host -> client: i64 spawned id, u32 net id of the enemy that threw it, str name of what landed
        /// (ThrownItem.prefabToSpawnOnLand, e.g. Fov_Mob_Spawner), f32 x, f32 y, f32 z, f32 yaw. Something one of the
        /// host's enemies threw (Character.spawnProjectile) landed and left this; make it there with that id (ObjRef
        /// Spawned). A client's copies of the host's enemies never throw (see World.EnemyLandings).
        /// </summary>
        EnemyLanded = 235,
        /// <summary>
        /// Host -> client: u32 net id of the enemy, str the projectile (its pooled prefab, Character.spawnProjectile's
        /// type), f32 x, f32 y, f32 z, f32 yaw, f32 vx, f32 vy, f32 vz. One of the host's enemies threw this; show it
        /// flying from there. It's only a look: it hurts nobody and leaves nothing, and goes when it hits something
        /// (what it leaves comes as EnemyLanded; whom it hurts, as PlayerDamage). See World.EnemyLandings.
        /// </summary>
        EnemyProjectile = 236,
        /// <summary>
        /// Host -> client: i64 spawned id. What a host enemy's projectile left (EnemyLanded) is gone from the host's
        /// world (it ran out, or was broken); remove yours. A client's copy never goes by itself (World.EnemyLandings).
        /// </summary>
        EnemyLandedGone = 237,
        /// <summary>
        /// Either way: ObjRef door, f32 leaf angle (signed degrees from shut: DeltaAngle(leaf local z, 90), as the mod
        /// saves Door.SaveState.rot). An open door's leaf that a player walked into came to rest at this angle in their
        /// game. The host checks the pusher is near and the door is open here, sets its leaf, and sends it to everyone,
        /// the pusher too (World.DoorPush).
        /// </summary>
        DoorLeaf = 100,
        /// <summary>
        /// Host -> client: u32 net id of the enemy, str the name of its ranged SensorType (Character.rangedAttack's,
        /// e.g. the human spider's "projectile"), f32 x, f32 y, f32 z, f32 yaw, f32 vx, f32 vy, f32 vz, f32 land x,
        /// f32 land z (ThrownItem.landTarget). One of the host's enemies threw its sensor's prefab (a ThrownItem made
        /// with Core.AddPrefab, not pooled); make the same from your copy's sensor and show it flying from there. Only
        /// a look, as EnemyProjectile: it hurts nobody and leaves nothing (see World.EnemyLandings).
        /// </summary>
        EnemySensorThrown = 90,
    }

    /// <summary>Channels (Steam only; TCP has one ordered stream).</summary>
    public static class Channel
    {
        public const int Control = 0;
        public const int State = 1;
    }

    public static class Protocol
    {
        /// <summary>Bump whenever the wire format changes; a host refuses other versions.</summary>
        public const ushort Version = 26;
        public const string Game = "darkwood";
        /// <summary>Most players in one game, the host included: the host and four friends (map4 with MAP_CLIENTS=4 plays five).</summary>
        public const int MaxPlayers = 5;
        /// <summary>A longer message is an error and ends the connection (as goasis's MaxMessage).</summary>
        public const int MaxMessage = 32 << 20;
    }

    /// <summary>Builds one message: type byte, then the payload. BinaryWriter is little-endian on every platform.</summary>
    public sealed class PacketWriter
    {
        private readonly MemoryStream _ms = new MemoryStream(1024);
        private readonly BinaryWriter _w;

        public PacketWriter() { _w = new BinaryWriter(_ms, Encoding.UTF8); }

        public PacketWriter Begin(MsgType type)
        {
            _ms.SetLength(0);
            _w.Write((byte)type);
            return this;
        }

        public BinaryWriter W => _w;
        public byte[] Buffer => _ms.GetBuffer();
        public int Length => (int)_ms.Length;

        public void Write(Vector3 v) { _w.Write(v.x); _w.Write(v.y); _w.Write(v.z); }

        /// <summary>str: u16 length, then that many UTF-8 bytes. Longer text is cut (and logged): use WriteBigStr for JSON.</summary>
        public void WriteStr(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, ushort.MaxValue);
            if (n < b.Length) Plugin.Log.LogError($"A {b.Length}-byte string was cut to {n} bytes in a message: use WriteBigStr for it\n{Environment.StackTrace}");
            _w.Write((ushort)n);
            _w.Write(b, 0, n);
        }

        /// <summary>bigstr: u32 length, then that many UTF-8 bytes. For JSON (inventories, profiles), which can pass 64 KiB.</summary>
        public void WriteBigStr(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            _w.Write((uint)b.Length);
            _w.Write(b);
        }

        public void WriteBytes(byte[] b) => _w.Write(b);
    }

    public sealed class PacketReader
    {
        private readonly BinaryReader _r;
        public MsgType Type { get; }

        public PacketReader(byte[] data, int length)
        {
            _r = new BinaryReader(new MemoryStream(data, 0, length, false), Encoding.UTF8);
            Type = (MsgType)_r.ReadByte();
        }

        public BinaryReader R => _r;
        public Vector3 ReadVector3() => new Vector3(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());

        public string ReadStr()
        {
            int n = _r.ReadUInt16();
            return Encoding.UTF8.GetString(_r.ReadBytes(n));
        }

        public string ReadBigStr()
        {
            uint n = _r.ReadUInt32();
            if (n > Protocol.MaxMessage) throw new InvalidDataException($"bigstr of {n} bytes");
            var b = _r.ReadBytes((int)n);
            if (b.Length != n) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(b);
        }
    }

    /// <summary>FNV-1a, stable across processes/platforms (string.GetHashCode is not).</summary>
    public static class StableHash
    {
        public static int Of(string s)
        {
            if (s == null) return 0;
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return (int)h;
            }
        }
    }
}
