"""Shared progression against a DarkwoodMP host (World.Progression): join, get the character and the shared essence and
level (ProgressState after CharacterLoad), cook essence (EssenceAdd, a request: the shared essence goes up for
everyone), say a level that the essence doesn't allow (the host keeps the shared level), spend skill points (SkillSpend:
refused with no point, without the negative skill tier I needs, from a tier not open yet; allowed with it), reset the
skills (SkillReset: refused outside the morning; in the morning, as our PlayerState says, allowed once and refused the
second time the same day)."""
import socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
import worldtest
from worldtest import frame, s16

PORT = int(sys.argv[1]); KEY = "e" * 64

s, r, my_id, _ = worldtest.join(PORT, "Cook", key=KEY, timeout=30)
state = None

def pump(secs, until):
    global state
    end = time.time() + secs
    while time.time() < end:
        try: m = r.msg(max(0.05, end - time.time()))
        except socket.timeout: return None  # a closed connection raises (EOFError): every later check would be void
        if m[0] == 180:
            state = struct.unpack_from("<iB", m, 1)
            print(f"  ProgressState essence {state[0]} level {state[1]}", flush=True)
        if until(m): return m
    return None

def request(label, inner, expect=None):
    """A Request and its Verdict: FAIL with no verdict, or when expect (True: allowed, False: refused) doesn't match."""
    ok = worldtest.request(s, pump, label, inner)
    if ok is None: print(f"FAIL {label}: no verdict", flush=True)
    elif expect is not None and bool(ok) != expect:
        print(f"FAIL {label}: {'allowed' if ok else 'refused'}, expected {'allowed' if expect else 'refused'}", flush=True)
    return ok

s.sendall(frame(bytes([24])))  # CharacterRequest
pump(30, lambda m: m[0] == 26)
print("  CharacterLoad", flush=True)
pump(5, lambda m: m[0] == 180)
if state is None: print("FAIL no ProgressState after the character"); sys.exit(1)
before = state[0]
ok = request("cook 5 essence", bytes([181]) + struct.pack("<i", 5))
pump(3, lambda m: m[0] == 180 and state[0] != before)
if ok is not None:
    print(f"essence {before} -> {state[0]}: {'ok' if state[0] == before + 5 else 'FAIL'}" if ok else
          f"cooking refused (the bar may be full): {'ok' if state[0] == before else 'FAIL'}", flush=True)
request("cook -3 essence", bytes([181]) + struct.pack("<i", -3), expect=False)
lvl = state[1]
s.sendall(frame(bytes([182, 200])))  # LevelTaken 200: more than any essence allows
pump(2, lambda m: False)
print(f"level after saying 200: {state[1]} ({'ok' if state[1] < 200 else 'FAIL'}; was {lvl})", flush=True)

def spend(label, level, good, neg="", had=(), expect=None):
    """SkillSpend: u8 level, str good, str negative, u8 n, n x str the skills we had."""
    body = bytes([160, level]) + s16(good) + s16(neg) + bytes([len(had)]) + b"".join(s16(h) for h in had)
    return request(label, body, expect)

# Enough essence for a few levels (one cooking while the bar isn't full), so level 1 is allowed.
request("cook 1000 essence", bytes([181]) + struct.pack("<i", 1000))
pump(2, lambda m: False)
no_point = spend("learn mushroomHealing at level 0", 0, "mushroomHealing", "nightShadows", expect=False)
no_neg = spend("learn mushroomHealing without a negative skill", 1, "mushroomHealing", expect=False)
closed = spend("learn runner (tier II) at level 1", 1, "runner", "narrowerFOV", expect=False)
too_high = spend("learn at level 200", 200, "mushroomHealing", "nightShadows", expect=False)
good = spend("learn mushroomHealing with nightShadows at level 1", 1, "mushroomHealing", "nightShadows", expect=True)
again = spend("learn navigator at level 1, having mushroomHealing", 1, "navigator", "", ("mushroomHealing", "nightShadows"), expect=False)
more = spend("learn navigator at level 2, having mushroomHealing", 2, "navigator", "", ("mushroomHealing", "nightShadows"), expect=True)
print(f"skill points: {'ok' if good and more and all(v == 0 for v in (no_point, no_neg, closed, too_high, again)) else 'FAIL'}", flush=True)

def player_state(morning):
    return worldtest.player_state(my_id, more=2 if morning else 0)

s.sendall(player_state(False)); pump(1, lambda m: False)
noon = request("reset skills outside the morning", bytes([183]), expect=False)
s.sendall(player_state(True)); pump(1, lambda m: False)
first = request("reset skills in the morning", bytes([183]), expect=True)
second = request("reset skills again the same day", bytes([183]), expect=False)
print(f"resets: {'ok' if first and second == 0 and noon == 0 else 'FAIL'}", flush=True)
s.close()
