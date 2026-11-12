"""Plays a client that asks a DarkwoodMP host for things it must allow or refuse (World.Authority), and prints
what the host decided. Run against a host with --dwmp-dummy --dwmp-spawn DogMutated (an enemy to hit).

    authtest.py PORT

Each request goes as Request (u32 seq + a whole message); the host answers Verdict (seq, ok, reason, result) and,
for a refused one, first the state to roll back to (ObjState). Movement isn't a request: a player who moves too
fast is put back (PlayerCorrection)."""
import json, socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, ask, frame, hello, rstr, s16

PORT = int(sys.argv[1])
DOOR, TRAP, WARDROBE = 18933, 18924, 18914  # in profile 1's save, near the spawn (see host-world)
SAVED = 1
PADLOCK, PADLOCK_AT, PADLOCK_CODE = 18923, (-11725.0, -5399.0), "2919"  # a padlocked container (host-world logs it)
CRATE, CRATE_AT = 20238, (-15011.0, -7390.0)                           # a locked crate, disarmChance 1
FEEDER, FEEDER_AT = 18913, (-11464.0, -5849.0)                         # a feeder to build (a Constructible)
OVEN, OVEN_AT = 102, (-12013.0, -5988.0)                               # an NPC with dialogue
NOTE, NOTE_AT = 19005, (-12762.0, -6266.0)                             # a note by the generator: onActivate, its event removes it
WORLD_EVENT = "cottageTrailer_txt_5"                                   # a world event that hasn't run

def join(s, r, name, key="c" * 64, h=bytes(32)):
    """Join as a player: download what the host offers (SaveOffer, 4) and Hello again with its hash, until Welcome (2)."""
    m = ask(s, r, hello(h, key=key, name=name), 20)
    while m[0] != 2:
        if m[0] == 4:
            h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]; got = off = 0
            while got < size:
                while off < size and off - got < 4 * (256 << 10):
                    s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
                m = r.msg(20)
                if m[0] == 6: got += struct.unpack_from("<I", m, 37)[0]
            m = ask(s, r, hello(h, key=key, name=name), 20)
        else:
            m = r.msg(20)
    return struct.unpack_from("<Q", m, 1)[0], h

s = socket.create_connection(("127.0.0.1", PORT), timeout=10); r = Reader(s)
my_id, h = join(s, r, "Asker")
print("joined as", my_id, flush=True)

host_pos, enemies, enemy_pos, seq = None, {}, {}, 0
pos = None

def state(x, z):
    global pos
    pos = (x, z)
    s.sendall(frame(bytes([10]) + struct.pack("<Q", my_id) + struct.pack("<fff", x, 16.0, z) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([1]) + s16("") + s16("") + bytes([0, 0, 100]) + s16("") + struct.pack("<fff", 0, 0, 0) + struct.pack("<f", 0)))

def pump(secs, until=None):
    """Read messages for up to secs; returns the first one until(m) accepts (or None)."""
    global host_pos
    end = time.time() + secs
    while time.time() < end:
        if pos: state(*pos)
        try: m = r.msg(0.05)
        except socket.timeout: continue
        t = m[0]
        if t == 10 and struct.unpack_from("<Q", m, 1)[0] == 1: host_pos = struct.unpack_from("<fff", m, 9)
        elif t == 14:
            eid = struct.unpack_from("<I", m, 1)[0]; _, at = rstr(m, 9); name, at = rstr(m, at); enemies[eid] = name
            enemy_pos[eid] = struct.unpack_from("<fff", m, at)
        elif t == 15:  # EnemyState: where each is
            n = struct.unpack_from("<H", m, 5)[0]; at = 7  # after f32 the host's clock
            for _ in range(n):
                eid = struct.unpack_from("<I", m, at)[0]; enemy_pos[eid] = struct.unpack_from("<fff", m, at + 4)
                at += 4 + 12 + 16 + 1 + 4
                k = m[at]; at += 1 + k * 11
        elif t == 42:
            k, oid, kind = m[1], struct.unpack_from("<q", m, 2)[0], m[10]
            desc = {0: "gone", 1: "door", 2: "window", 3: "trap", 4: "item", 5: "padlock", 6: "explosive"}.get(kind, kind)
            extra = f" flags {m[11]}" if kind in (1, 2, 3, 4, 5, 6) else ""
            if kind == 4: extra += f" health {struct.unpack_from('<i', m, 12)[0]}"
            print(f"  ObjState {k}:{oid} {desc}{extra}", flush=True)
        elif t == 43:
            print(f"  PlayerCorrection to {struct.unpack_from('<fff', m, 1)[0]:.0f},{struct.unpack_from('<fff', m, 1)[2]:.0f}", flush=True)
        elif t == 38:
            k, oid = m[1], struct.unpack_from("<q", m, 2)[0]; slot = struct.unpack_from("<H", m, 10)[0]
            print(f"  ContainerSlotRefused {k}:{oid} slot {slot}", flush=True)
        if until and until(m): return m
    return None

def request(label, inner):
    global seq
    seq += 1
    print(label, flush=True)
    s.sendall(frame(bytes([40]) + struct.pack("<I", seq) + inner))
    m = pump(5, lambda m: m[0] == 41 and struct.unpack_from("<I", m, 1)[0] == seq)
    if m is None: print("  -> no verdict", flush=True); return None
    ok = m[5]; reason, at = rstr(m, 6); result, _ = rstr(m, at)
    print(f"  -> {'ok' if ok else 'refused'}{': ' + reason if reason else ''}{' (result ' + result + ')' if result else ''}", flush=True)
    return ok, reason, result

def action(a, oid, x=0.0, y=0.0, flags=0, s=""):
    return bytes([21, a, SAVED]) + struct.pack("<q", oid) + struct.pack("<ff", x, y) + bytes([flags]) + s16(s)

pump(8, lambda m: host_pos is not None and m[0] == 10)
hx, hy, hz = host_pos
state(hx + 40, hz); pump(2)

request("open the door, standing by it", action(1, DOOR, hx, hz))
request("close it again", action(2, DOOR))
request("unbarricade a door that has no barricade", action(4, DOOR))
request("hit the door for 5000", action(6, DOOR, 5000.0))
request("hit the wardrobe for 1", action(51, WARDROBE, 1.0))
request("hit the wardrobe for 5000", action(51, WARDROBE, 5000.0))
request("repair the wardrobe, which isn't broken", action(53, WARDROBE))
request("set off the wardrobe as if it were a barrel", action(61, WARDROBE))
request("pick the door up off the ground (Removed: not a note or a death bag)", action(91, DOOR))
request("switch the door on as if it were an InteractiveItem", action(90, DOOR, flags=1))
request("use the door as a feeder", action(92, DOOR))
request("set the door alight as if it were spilt gasoline", action(93, DOOR))
request("cut the door as if it were a vine", action(130, DOOR, 10.0))
request("kill the shadow on the wardrobe (only the host decides)", action(131, WARDROBE))

state(hx + 4000, hz); pump(1)  # far away (a jump that far is the game moving us: allowed)
request("open the door from 4000 away", action(1, DOOR, hx, hz))
request("hit the wardrobe from 4000 away", action(51, WARDROBE, 1.0))
s.sendall(frame(bytes([37, SAVED]) + struct.pack("<q", WARDROBE) + bytes([1]) + struct.pack("<H", 0) + s16("") + s16("")))
print("take from the wardrobe from 4000 away (ContainerSlots)", flush=True); pump(1.5)
state(hx + 40, hz); pump(1)

request("disarm the mushroom trap", action(23, TRAP))
request("disarm it again", action(23, TRAP))

pump(6, lambda m: len(enemies) > 0)
dog = next((e for e, n in enemies.items() if n == "DogMutated"), next(iter(enemies), None))
hit = lambda eid, dmg, weapon="": bytes([17]) + struct.pack("<If", eid, dmg) + bytes([2 | 4 | 8, 0]) + s16(weapon)
def beside_dog():
    """Stand 100 from the dog (bare hands reach 500), as go() does: a jump away first, so it isn't a speed hack."""
    pump(0.5)
    dx, _, dz = enemy_pos.get(dog, (hx, 0, hz))
    state(dx + 6100, dz + 6000); pump(0.5); state(dx + 100, dz); pump(1)
    print(f"  (beside #{dog} at {dx:.0f},{dz:.0f})", flush=True)
request("hit an enemy that isn't there", hit(999999, 10.0))

def burst(label, inners):
    """Send every request at once (within a second), then count the host's verdicts."""
    global seq
    first = seq + 1
    print(label, flush=True)
    for inner in inners:
        seq += 1
        s.sendall(frame(bytes([40]) + struct.pack("<I", seq) + inner))
    got = {}
    def verdict(m):
        if m[0] == 41 and first <= struct.unpack_from("<I", m, 1)[0] <= seq:
            got[struct.unpack_from("<I", m, 1)[0]] = (m[5], rstr(m, 6)[0])
        return len(got) == len(inners)
    pump(8, verdict)
    reasons = sorted({why for ok, why in got.values() if not ok})
    print(f"  -> {sum(1 for ok, _ in got.values() if ok)} ok, {sum(1 for ok, _ in got.values() if not ok)} refused"
          + (f", {len(inners) - len(got)} no verdict" if len(got) < len(inners) else "") + (f": {reasons}" if reasons else ""), flush=True)

# Hits a second: a gun's pellets and fire rate allow more than bare hands' 24 (NetEnemies.MaxHits).
weapon = lambda t: '{"type":"%s","durability":100.0,"amount":1}' % t
if dog is not None:
    beside_dog()
    pump(1.2)
    burst(f"30 hits on #{dog} in a second with a pump-action shotgun (2 shots of 15 pellets)",
          [hit(dog, 0.1, weapon("weapon_shotgun_02_pumpAction")) for _ in range(30)])
    pump(1.2)
    burst(f"28 hits on #{dog} in a second with a submachine gun (20 shots a second)",
          [hit(dog, 0.1, weapon("weapon_submachine_01_full")) for _ in range(28)])
    pump(1.2)
    burst(f"30 hits on #{dog} in a second with bare hands (most 24: 6 refused)", [hit(dog, 0.1) for _ in range(30)])
    pump(1.2)
    request(f"hit {enemies[dog]} #{dog} for 99999, bare hands", hit(dog, 99999.0))
    state(hx + 40, hz); pump(1)

# Locks (see host-world's log for where these are): the door here has no lock; the padlock and the crate are
# further off, so first from here (too far), then from beside them (a jump of more than 3000 is the game moving us).
def go(x, z):
    state(x + 6000, z + 6000); pump(0.5); state(x, z); pump(1)
request("lockpick the door by the spawn, which has no lock", action(8, DOOR, 2.0))
request("unlock the padlock from here, with its code", action(35, PADLOCK, flags=1, s=PADLOCK_CODE))
request("pick the locked crate from here", action(23, CRATE))
go(*PADLOCK_AT)
request("unlock the padlock beside it, with a wrong code", action(35, PADLOCK, flags=1, s="0000"))
request("unlock the padlock beside it, with its code", action(35, PADLOCK, flags=1, s=PADLOCK_CODE))
go(*CRATE_AT)
request("unlock the crate's lock, which it hasn't got", action(33, CRATE, 2.0))
request("say the crate was picked open (the host's to say)", action(34, CRATE))
request("pick the locked crate beside it (chance 1)", action(23, CRATE))
request("pick it again (already open)", action(23, CRATE))
go(hx + 40, hz)

# Building, scripted events and dialogue (host-world logs these as "buildable", "NPC with dialogue", "world event").
construct = lambda oid, opt: bytes([66, SAVED]) + struct.pack("<qi", oid, opt)
event = lambda kind, oid, idx, name="": bytes([67, kind, SAVED if oid else 0]) + struct.pack("<q", oid) + bytes([idx]) + s16(name)
talk = lambda oid, on: bytes([68, SAVED]) + struct.pack("<q", oid) + bytes([on]) + s16("")
go(FEEDER_AT[0] + 3000, FEEDER_AT[1])
request("build the feeder from 3000 away", construct(FEEDER, 0))
go(*FEEDER_AT)
request("build the feeder beside it, with an option it hasn't got", construct(FEEDER, 3))
request("build the feeder beside it", construct(FEEDER, 0))
request("build it again", construct(FEEDER, 0))
go(NOTE_AT[0] + 4000, NOTE_AT[1])
request("use the note from 4000 away", event(1, NOTE, 0))
go(*NOTE_AT)
request("use the note beside it", event(1, NOTE, 0))
request("use it again (it's gone)", event(1, NOTE, 0))
request(f"set off world event {WORLD_EVENT}", event(2, 0, 0, WORLD_EVENT))
request("set off a world event that doesn't exist", event(2, 0, 0, "no_such_event"))
go(*OVEN_AT)
request("talk to the oven", talk(OVEN, 1))
# A second player wants the same NPC while we talk to them.
s2 = socket.create_connection(("127.0.0.1", PORT), timeout=10); r2 = Reader(s2)
join(s2, r2, "Asker2", key="d" * 64, h=h)
print("Asker2 joined", flush=True)
def request2(label, inner):
    global seq
    seq += 1
    print(label, flush=True)
    s2.sendall(frame(bytes([40]) + struct.pack("<I", seq) + inner))
    end = time.time() + 5
    while time.time() < end:
        pump(0.05)
        try: m = r2.msg(0.05)
        except socket.timeout: continue
        if m[0] == 41 and struct.unpack_from("<I", m, 1)[0] == seq:
            reason, _ = rstr(m, 6)
            print(f"  -> {'ok' if m[5] else 'refused'}{': ' + reason if reason else ''}", flush=True); return
    print("  -> no verdict", flush=True)
request2("Asker2: talk to the oven too", talk(OVEN, 1))
s.sendall(frame(talk(OVEN, 0))); print("Asker: done talking", flush=True); pump(0.5)
request2("Asker2: talk to the oven at once (Asker's dialogue changes are on their way: refused)", talk(OVEN, 1))
end2 = time.time() + 1.5
while time.time() < end2:  # Asker2 reads too, or what queued up for it hides the verdict
    pump(0.05)
    try:
        while True: r2.msg(0.01)
    except socket.timeout: pass
request2("Asker2: talk to the oven now", talk(OVEN, 1))
s2.close()
go(hx + 40, hz)

print("run at 5000 a second for a second (a speed hack):", flush=True)
start = time.time()
while time.time() - start < 1.0:
    state(hx + 40 + 5000 * (time.time() - start), hz); pump(0.05)
pump(1.5)
print("walk at 300 a second for two seconds:", flush=True)
x0 = pos[0]; start = time.time()
while time.time() - start < 2.0:
    state(x0 - 300 * (time.time() - start), hz); pump(0.05)
pump(1.0)
print("done", flush=True)
