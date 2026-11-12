"""Shared progression against a DarkwoodMP host (World.Progression): join, get the character and the shared essence and
level (ProgressState after CharacterLoad), cook essence (EssenceAdd, a request: the shared essence goes up for
everyone), say a level that the essence doesn't allow (the host keeps the shared level), spend skill points (SkillSpend:
refused with no point, without the negative skill tier I needs, from a tier not open yet; allowed with it), reset the
skills (SkillReset: refused outside the morning; in the morning, as our PlayerState says, allowed once and refused the
second time the same day)."""
import socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, frame, hello, rstr, s16
from worldtest import ask as hello_until_answered

PORT = int(sys.argv[1]); KEY = "e" * 64

def join():
    """Join as chartest.py does: download the offered world, then Hello again with its hash until Welcome."""
    s = socket.create_connection(("127.0.0.1", PORT), timeout=10); r = Reader(s)
    m = hello_until_answered(s, r, hello(key=KEY, name="Cook"), 30); assert m[0] == 4, f"expected SaveOffer, got {m[0]}"
    h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]; got = off = 0
    while got < size:
        while off < size and off - got < 4 * (256 << 10):
            s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
        m = r.msg(30)
        if m[0] == 6: got += struct.unpack_from("<I", m, 37)[0]
    s.sendall(hello(h, key=KEY, name="Cook"))
    while True:
        m = r.msg(30)
        if m[0] == 2: return s, r, struct.unpack_from("<Q", m, 1)[0]

s, r, my_id = join()
seq = 0
state = None

def pump(secs, until):
    global state
    end = time.time() + secs
    while time.time() < end:
        try: m = r.msg(max(0.05, end - time.time()))
        except Exception: return None
        if m[0] == 180:
            state = struct.unpack_from("<iB", m, 1)
            print(f"  ProgressState essence {state[0]} level {state[1]}", flush=True)
        if until(m): return m
    return None

def request(label, inner):
    global seq
    seq += 1
    print(label, flush=True)
    s.sendall(frame(bytes([40]) + struct.pack("<I", seq) + inner))
    m = pump(5, lambda m: m[0] == 41 and struct.unpack_from("<I", m, 1)[0] == seq)
    if m is None: print("  -> no verdict", flush=True); return None
    ok = m[5]; reason, _ = rstr(m, 6)
    print(f"  -> {'ok' if ok else 'refused'}{': ' + reason if reason else ''}", flush=True)
    return ok

s.sendall(frame(bytes([24])))  # CharacterRequest
pump(30, lambda m: m[0] == 26)
print("  CharacterLoad", flush=True)
pump(5, lambda m: m[0] == 180)
if state is None: print("FAIL no ProgressState after the character"); sys.exit(1)
before = state[0]
ok = request("cook 5 essence", bytes([181]) + struct.pack("<i", 5))
pump(3, lambda m: m[0] == 180 and state[0] != before)
print(f"essence {before} -> {state[0]}: {'ok' if ok and state[0] == before + 5 else 'FAIL'}" if ok else
      f"cooking refused (the bar may be full): {'ok' if state[0] == before else 'FAIL'}", flush=True)
request("cook -3 essence", bytes([181]) + struct.pack("<i", -3))
lvl = state[1]
s.sendall(frame(bytes([182, 200])))  # LevelTaken 200: more than any essence allows
pump(2, lambda m: False)
print(f"level after saying 200: {state[1]} ({'ok' if state[1] < 200 else 'FAIL'}; was {lvl})", flush=True)

def spend(label, level, good, neg="", had=()):
    """SkillSpend: u8 level, str good, str negative, u8 n, n x str the skills we had."""
    body = bytes([160, level]) + s16(good) + s16(neg) + bytes([len(had)]) + b"".join(s16(h) for h in had)
    return request(label, body)

# Enough essence for a few levels (one cooking while the bar isn't full), so level 1 is allowed.
request("cook 1000 essence", bytes([181]) + struct.pack("<i", 1000))
pump(2, lambda m: False)
no_point = spend("learn mushroomHealing at level 0", 0, "mushroomHealing", "nightShadows")
no_neg = spend("learn mushroomHealing without a negative skill", 1, "mushroomHealing")
closed = spend("learn runner (tier II) at level 1", 1, "runner", "narrowerFOV")
too_high = spend("learn at level 200", 200, "mushroomHealing", "nightShadows")
good = spend("learn mushroomHealing with nightShadows at level 1", 1, "mushroomHealing", "nightShadows")
again = spend("learn navigator at level 1, having mushroomHealing", 1, "navigator", "", ("mushroomHealing", "nightShadows"))
more = spend("learn navigator at level 2, having mushroomHealing", 2, "navigator", "", ("mushroomHealing", "nightShadows"))
print(f"skill points: {'ok' if good and more and not (no_point or no_neg or closed or too_high or again) else 'FAIL'}", flush=True)

def player_state(morning):
    # u64 owner, vec3 pos, quat rot, u8 state (1 alive), str library, str held light, u8 sprites, u8 more (2: in an after-night), u8 health %, str place, vec3 held flames, f32 lantern
    return frame(bytes([10]) + struct.pack("<Q", my_id) + struct.pack("<fff", 0, 16.0, 0) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([1]) + s16("") + s16("") + bytes([0]) + bytes([2 if morning else 0, 100]) + s16("") + struct.pack("<fff", 0, 0, 0) + struct.pack("<f", 0))

s.sendall(player_state(False)); pump(1, lambda m: False)
noon = request("reset skills outside the morning", bytes([183]))
s.sendall(player_state(True)); pump(1, lambda m: False)
first = request("reset skills in the morning", bytes([183]))
second = request("reset skills again the same day", bytes([183]))
print(f"resets: {'ok' if first and not second and not noon else 'FAIL'}", flush=True)
s.close()
