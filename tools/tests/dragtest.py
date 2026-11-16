"""Plays two clients that drag the same thing at a DarkwoodMP host, and checks that the host gives each drag to one
player at a time (World.DragSync: DragClaim, DragOwner); then that the host takes a batch of container slot changes
whole or not at all (World.SlotShare). Run against a host with --dwmp-debug-world.

    dragtest.py PORT

A drag is claimed with a Request (40) holding DragClaim (48: ObjRef); the host answers Verdict (41) and tells everyone
who drags it (DragOwner, 49: ObjRef, u64 owner, u8 flags). Positions go as ObjMove (33: ObjRef, vec3, quat, u8 last).
Prints PASS or FAIL for each check, and exits 1 if one failed."""
import json, socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, ask, frame, hello, player_state, rstr, rbig, s16

def join(s, r, name, key, h=bytes(32)):
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

PORT = int(sys.argv[1])
SAVED = 1
STOOL, STOOL_AT = 18910, (-11862.0, -5775.0)  # a stool by the spawn in profile 1's save (host-world drags it)
WARDROBE = 18914                               # a container by the spawn (see authtest.py)
ROPE = '{"type":"rope","durability":1.0,"amount":3}'
failed = 0

def check(ok, what):
    global failed
    print(f"  {'PASS' if ok else 'FAIL'}: {what}", flush=True)
    if not ok: failed += 1

def ref(oid): return bytes([SAVED]) + struct.pack("<q", oid)

class Peer:
    def __init__(self, name, key, h=bytes(32)):
        self.s = socket.create_connection(("127.0.0.1", PORT), timeout=10); self.r = Reader(self.s)
        self.id, self.h = join(self.s, self.r, name, key=key, h=h)
        self.name, self.pos, self.got, self.seq, self.host_pos = name, None, [], 0, None
        print(f"{name} joined as #{self.id}", flush=True)
    def send(self, p): self.s.sendall(frame(p))
    def state(self, x, z): self.pos = (x, z); self.s.sendall(player_state(self.id, x, 16.0, z))
    def read(self):
        try:
            while True:
                m = self.r.msg(0.005)
                if m[0] == 10 and struct.unpack_from("<Q", m, 1)[0] == 1: self.host_pos = struct.unpack_from("<fff", m, 9)
                if m[0] in (33, 38, 41, 49, 0xC9): self.got.append((time.time(), m))
        except socket.timeout: pass
    def close(self): self.s.close()

peers = []
def pump(secs):
    end = time.time() + secs; tick = 0
    while time.time() < end:
        tick += 1
        for p in peers:
            if p.pos and tick % 4 == 0: p.state(*p.pos)
            p.read()
        time.sleep(0.01)

def since(p, t0, kind): return [m for t, m in p.got if t >= t0 and m[0] == kind]
def moves(p, t0, oid=STOOL, last=None):
    out = []
    for m in since(p, t0, 33):
        if m[1] == SAVED and struct.unpack_from("<q", m, 2)[0] == oid and (last is None or bool(m[38] & 1) == last):
            out.append(struct.unpack_from("<fff", m, 10))
    return out
def owners(p, t0, oid=STOOL):
    return [(struct.unpack_from("<Q", m, 10)[0], m[18]) for m in since(p, t0, 49) if struct.unpack_from("<q", m, 2)[0] == oid]

def request(p, label, inner):
    p.seq += 1; seq = p.seq; t0 = time.time()
    print(label, flush=True)
    p.send(bytes([40]) + struct.pack("<I", seq) + inner)
    end = time.time() + 5
    while time.time() < end:
        pump(0.05)
        for m in since(p, t0, 41):
            if struct.unpack_from("<I", m, 1)[0] == seq:
                reason, _ = rstr(m, 6)
                print(f"  -> {'ok' if m[5] else 'refused'}{': ' + reason if reason else ''}", flush=True)
                return bool(m[5]), reason
    print("  -> no verdict", flush=True)
    return None, ""

def claim(p, label): return request(p, label, bytes([48]) + ref(STOOL))
def move(p, x, z, last=False):
    p.send(bytes([33]) + ref(STOOL) + struct.pack("<fff", x, -0.43, z) + struct.pack("<ffff", 0, 0, 0, 1) + bytes([1 if last else 0]))
def stream(p, x0, z0, dx, secs=1.0):
    """Drag it from x0,z0 by dx over secs, ten moves a second, as a game does."""
    start = time.time()
    while time.time() - start < secs:
        move(p, x0 + dx * (time.time() - start) / secs, z0); pump(0.1)

a = Peer("Dragger", "c" * 64); peers.append(a)
b = Peer("Other", "d" * 64, h=a.h); peers.append(b)
pump(3)
hx, _, hz = a.host_pos
sx, sz = STOOL_AT
a.state(hx + 40, hz); b.state(hx + 60, hz + 20); pump(1.5)

# 1. Dragger takes hold of the stool; Other can't while Dragger has it.
t0 = time.time()
ok, _ = claim(a, "Dragger takes hold of the stool")
check(ok is True, "Dragger's claim is agreed to")
pump(0.5)
check((a.id, 1) in owners(b, t0), f"Other is told Dragger drags it (DragOwner {owners(b, t0)})")
t1 = time.time()
ok, why = claim(b, "Other takes hold of it too")
check(ok is False and why.endswith("is moving it"), f"Other's claim is refused ({why!r})")
check(len(moves(b, t1, last=True)) >= 1, "Other was sent where the stool is before the refusal")

# 2. Both send moves: only Dragger's are taken (passed on to Other); Other is sent where it is.
t2 = time.time()
print("both drag it for a second, Dragger +100, Other -100", flush=True)
start = time.time()
while time.time() - start < 1.0:
    f = (time.time() - start)
    move(a, sx + 100 * f, sz); move(b, sx - 100 * f, sz); pump(0.1)
pump(0.5)
relayed = moves(b, t2, last=False)
check(len(relayed) >= 5 and all(x > sx - 1 for x, _, _ in relayed), f"Dragger's moves reach Other ({len(relayed)}, x {min((x for x, _, _ in relayed), default=0):.0f}..{max((x for x, _, _ in relayed), default=0):.0f})")
check(not any(x < sx - 1 for x, _, _ in moves(a, t2)), "none of Other's moves reach Dragger")
check(len(moves(b, t2, last=True)) >= 1, f"Other is sent where it is ({len(moves(b, t2, last=True))} time(s))")

# 3. Dragger lets go: free for Other.
t3 = time.time()
move(a, sx + 100, sz, last=True); pump(0.5)
check((0, 0) in owners(b, t3), f"letting go frees it (DragOwner {owners(b, t3)})")
ok, _ = claim(b, "Other takes hold of it now")
check(ok is True, "Other's claim is agreed to once Dragger let go")

# 4. Other goes quiet: freed after a few seconds.
t4 = time.time()
print("Other stops sending for 5 s", flush=True)
pump(5)
check((0, 1) in owners(a, t4), f"a silent drag is freed (DragOwner {owners(a, t4)})")

# 5. A bare DragClaim (not a request) does nothing.
t5 = time.time()
a.send(bytes([48]) + ref(STOOL)); pump(0.8)
check(not any(o == a.id for o, _ in owners(b, t5)), "a DragClaim outside a Request is dropped")

# 6. A move without a claim (a push still sliding) is passed on only from a mover at it and to near it: one to 3000
#    away is dropped (a drag's end, a push's rest, is checked the same way and taken).
move(a, sx + 3000, sz); pump(0.5)
check(not moves(b, t5, last=False), "a move without a claim to far away isn't passed on")
t6 = time.time()
move(a, sx + 20, sz); pump(0.5)
check(len(moves(b, t6, last=False)) >= 1, "a slide move without a claim, beside it, is passed on")

# 7. Out of reach: the drag is taken away and the mover lets go.
ok, _ = claim(b, "Other takes hold of it again")
check(ok is True, "Other's claim is agreed to")
t7 = time.time()
move(b, sx + 3000, sz); pump(1.0)
check((0, 1) in owners(b, t7) and len(moves(b, t7, last=True)) >= 1, f"a move out of reach frees the drag and puts it back (DragOwner {owners(b, t7)})")

# 8. The owner leaves: freed.
ok, _ = claim(a, "Dragger takes hold of it again")
check(ok is True, "Dragger's claim is agreed to")
t8 = time.time()
peers.remove(a); a.close(); print("Dragger leaves", flush=True)
pump(2)
check((0, 1) in owners(b, t8), f"a drag of a player who left is freed (DragOwner {owners(b, t8)})")

# 9. Container slots: a batch is taken whole or not at all. Other's view of the wardrobe is stale: it moves the rope
#    from one slot to another (two changes) after a third player took it; both are refused, nothing doubles.
c = Peer("Third", "e" * 64, h=b.h); peers.append(c)
c.state(hx + 50, hz - 20); pump(2)
def contents(p):
    t = time.time(); p.send(bytes([0xC8]) + ref(WARDROBE)); pump(1.5)
    m = since(p, t, 0xC9)
    if not m: return None
    js, _ = rbig(m[-1], 10)
    return json.loads(js) if js else None
inv = contents(c)
check(inv is not None, "the wardrobe's contents come back")
if inv is not None:
    slots = lambda p, ch: p.send(bytes([37]) + ref(WARDROBE) + bytes([len(ch)]) + b"".join(struct.pack("<H", s) + s16(o) + s16(n) for s, o, n in ch))
    def key(it):
        """SlotShare.Key: type|amount|durability x1000|recipe|upgrades."""
        return f"{it['type']}|{it.get('amount', 0)}|{round(it.get('durability', 0) * 1000)}|{1 if it.get('isRecipe') else 0}|{len(it.get('upgrades') or [])}"
    items = sorted(inv.get("invItems", []), key=lambda i: i.get("slotId", 0))
    used = {i.get("slotId") for i in items}
    free = [i for i in range(inv.get("slotCount", 0)) if i not in used]
    if len(free) < 2:  # make room: Third takes what's in the first slots
        take = [i for i in items if i.get("type")][:2 - len(free)]
        print(f"  Third takes {[(i['slotId'], i['type']) for i in take]} to make room", flush=True)
        t = time.time(); slots(c, [(i["slotId"], key(i), "") for i in take]); pump(1)
        check(not since(c, t, 38), "Third's take is agreed to")
        inv = contents(c); used = {i.get("slotId") for i in inv.get("invItems", [])}
        free = [i for i in range(inv.get("slotCount", 0)) if i not in used]
    print(f"  the wardrobe has {inv.get('slotCount')} slots, free: {free[:6]}", flush=True)
    if len(free) >= 2:
        e0, e1 = free[0], free[1]
        slots(c, [(e0, "", ROPE)]); pump(1)
        it = next((i for i in contents(c).get("invItems", []) if i.get("slotId") == e0), None)
        check(it is not None and it.get("type") == "rope", f"Third put the rope in slot {e0}")
        key = f"rope|{it.get('amount', 3)}|{round(it.get('durability', 1.0) * 1000)}|0|0" if it else "rope|3|1000|0|0"
        slots(c, [(e0, key, "")]); pump(1)  # Third takes it
        t9 = time.time()
        slots(b, [(e0, key, ""), (e1, "", ROPE)]); pump(1.5)  # Other moves it to e1, from a stale view
        refused = sorted(struct.unpack_from("<H", m, 10)[0] for m in since(b, t9, 38))
        check(refused == sorted([e0, e1]), f"both of Other's changes are refused (slots {refused})")
        now = {i.get("slotId"): i.get("type") for i in contents(c).get("invItems", [])}
        check(e0 not in now and e1 not in now, f"slots {e0} and {e1} are empty on the host ({now.get(e0)}, {now.get(e1)}): the rope wasn't doubled")
    else: check(False, "two free slots in the wardrobe")

for p in peers: p.close()
print(f"done: {'all passed' if failed == 0 else f'{failed} failed'}", flush=True)
sys.exit(1 if failed else 0)
