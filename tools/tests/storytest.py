"""Plays a client that sets off a DarkwoodMP host's chapter change (World.Chapters) and follows it.

    storytest.py PORT KIND ID INDEX X Z CHAPTER

KIND ID is the ObjRef of a GameEvents whose step INDEX starts CHAPTER, at X Z (the host logs a stand-in one with
--dwmp-test-chapter). It asks for it (StoryRequest, as a Request) from 4000 away, for the wrong chapter, for an event
the host hasn't got while not away, and for the epilogue, which the host must refuse, then beside it, which it must
allow. Then it waits for ChapterStart and WorldReload, sends its character as a game does at ChapterStart, and says
Hello again to see that the host offers its new world."""
import socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
import worldtest
from worldtest import frame, s32

PORT = int(sys.argv[1]); KIND, OID, INDEX = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
AT = (float(sys.argv[5]), float(sys.argv[6])); CHAPTER = int(sys.argv[7])


def join():
    s, r, my_id, offer = worldtest.join(PORT, "Storyteller")
    print(f"offered the host's world ({struct.unpack_from('<I', offer, 33)[0]} bytes)", flush=True)
    return s, r, my_id


s, r, my_id = join()
print("joined as", my_id, flush=True)
pos, early = None, {}


def state(x, z):
    global pos
    pos = (x, z)
    s.sendall(worldtest.player_state(my_id, x, 16.0, z))


def pump(secs, until):
    end = time.time() + secs
    while time.time() < end:
        if pos: state(*pos)
        try: m = r.msg(0.05)
        except socket.timeout: continue
        if m[0] in (86, 31): early.setdefault(m[0], m)  # may come before the verdict
        if until(m): return m
    return None


def request(label, expect, chapter=None, story=1, oid=None, kind=None):
    """StoryRequest as a Request: FAIL with no verdict, or when the verdict isn't expect (True: allowed)."""
    inner = bytes([88, KIND if kind is None else kind]) + struct.pack("<q", OID if oid is None else oid) + bytes([INDEX, story]) + struct.pack("<i", CHAPTER if chapter is None else chapter)
    ok = worldtest.request(s, pump, label, inner)
    if ok is None: print(f"FAIL {label}: no verdict", flush=True)
    elif bool(ok) != expect: print(f"FAIL {label}: {'allowed' if ok else 'refused'}, expected {'allowed' if expect else 'refused'}", flush=True)
    return ok


state(AT[0] + 4000, AT[1]); pump(2, lambda m: False)
request("start the next chapter from 4000 away", False)
state(AT[0] + 40, AT[1]); pump(1, lambda m: False)
request("start a chapter after the next, beside it", False, chapter=CHAPTER + 1)
request("start the next chapter from an event the host hasn't got, not away", False, kind=0, oid=0)
request("start the epilogue (only after the last chapter)", False, story=2, chapter=0)
ok = request("start the next chapter beside its trigger", True)
if ok:
    t0 = time.time()
    m = early.get(86) or pump(30, lambda m: m[0] == 86)
    if m is None: print("FAIL no ChapterStart", flush=True); sys.exit(1)
    print(f"ChapterStart: chapter {struct.unpack_from('<i', m, 1)[0]} ({time.time() - t0:.1f} s)", flush=True)
    s.sendall(frame(bytes([25]) + s32('{"health":100}') + s32('{"invItems":[]}') + s32('{"invItems":[]}') + struct.pack("<i", CHAPTER - 1)))  # from the chapter being left
    m = early.get(31) or pump(400, lambda m: m[0] in (31, 32))
    if m is None: print("FAIL no WorldReload", flush=True); sys.exit(1)
    if m[0] != 31: print("FAIL HostAway from the host instead of WorldReload", flush=True); sys.exit(1)
    print(f"WorldReload from the host ({time.time() - t0:.0f} s after the request)", flush=True)
    s.close()
    for _ in range(30):
        try: s, r, my_id = join(); break
        except RuntimeError as e: print(f"Hello again: {e}", flush=True); time.sleep(3)
    else: print("FAIL never let back in after the WorldReload", flush=True); sys.exit(1)
    print("joined the new chapter", flush=True)
    s.sendall(frame(bytes([24])))
    m = pump(20, lambda m: m[0] == 26)
    if m is None: print("FAIL no CharacterLoad", flush=True)
    else: print(f"CharacterLoad kind {m[1]} (3: ours from the last chapter, beside the host){'' if m[1] == 3 else '  <-- FAIL'}", flush=True)
s.close()
