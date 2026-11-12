"""Plays a client that sets off a DarkwoodMP host's chapter change (World.Chapters) and follows it.

    storytest.py PORT KIND ID INDEX X Z CHAPTER

KIND ID is the ObjRef of a GameEvents whose step INDEX starts CHAPTER, at X Z (the host logs a stand-in one with
--dwmp-test-chapter). It asks for it (StoryRequest, as a Request) from 4000 away, for the wrong chapter, for an event
the host hasn't got while not away, and for the epilogue, which the host must refuse, then beside it, which it must
allow. Then it waits for ChapterStart and WorldReload, sends its character as a game does at ChapterStart, and says
Hello again to see that the host offers its new world."""
import socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, ask, frame, hello, rstr, s16, s32

PORT = int(sys.argv[1]); KIND, OID, INDEX = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
AT = (float(sys.argv[5]), float(sys.argv[6])); CHAPTER = int(sys.argv[7])


def join():
    s = socket.create_connection(("127.0.0.1", PORT), timeout=10); r = Reader(s)
    m = ask(s, r, hello(name="Storyteller"), 20)
    if m[0] != 4: s.close(); raise RuntimeError(f"message {m[0]} {rstr(m, 1)[0] if m[0] in (3, 34) else ''}")
    print(f"offered the host's world ({struct.unpack_from('<I', m, 33)[0]} bytes)", flush=True)
    h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]; got = off = 0
    while got < size:
        while off < size and off - got < 4 * (256 << 10):
            s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
        m = r.msg(20)
        if m[0] == 6: got += struct.unpack_from("<I", m, 37)[0]
    s.sendall(hello(h, name="Storyteller"))
    while True:
        m = r.msg(20)
        if m[0] == 2: return s, r, struct.unpack_from("<Q", m, 1)[0]


s, r, my_id = join()
print("joined as", my_id, flush=True)
pos, seq, early = None, 0, {}


def state(x, z):
    global pos
    pos = (x, z)
    s.sendall(frame(bytes([10]) + struct.pack("<Q", my_id) + struct.pack("<fff", x, 16.0, z) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([1]) + s16("") + s16("") + bytes([0, 0, 100]) + s16("") + struct.pack("<fff", 0, 0, 0) + struct.pack("<f", 0)))


def pump(secs, until):
    end = time.time() + secs
    while time.time() < end:
        if pos: state(*pos)
        try: m = r.msg(0.05)
        except socket.timeout: continue
        if m[0] in (86, 31): early.setdefault(m[0], m)  # may come before the verdict
        if until(m): return m
    return None


def request(label, chapter=None, story=1, oid=None, kind=None):
    global seq
    seq += 1
    print(label, flush=True)
    inner = bytes([88, KIND if kind is None else kind]) + struct.pack("<q", OID if oid is None else oid) + bytes([INDEX, story]) + struct.pack("<i", CHAPTER if chapter is None else chapter)
    s.sendall(frame(bytes([40]) + struct.pack("<I", seq) + inner))
    m = pump(5, lambda m: m[0] == 41 and struct.unpack_from("<I", m, 1)[0] == seq)
    if m is None: print("  -> no verdict", flush=True); return None
    ok = m[5]; reason, _ = rstr(m, 6)
    print(f"  -> {'ok' if ok else 'refused'}{': ' + reason if reason else ''}", flush=True)
    return ok


state(AT[0] + 4000, AT[1]); pump(2, lambda m: False)
request("start the next chapter from 4000 away")
state(AT[0] + 40, AT[1]); pump(1, lambda m: False)
request("start a chapter after the next, beside it", chapter=CHAPTER + 1)
request("start the next chapter from an event the host hasn't got, not away", kind=0, oid=0)
request("start the epilogue (only after the last chapter)", story=2, chapter=0)
ok = request("start the next chapter beside its trigger")
if ok:
    t0 = time.time()
    m = early.get(86) or pump(30, lambda m: m[0] == 86)
    if m is None: print("no ChapterStart", flush=True); sys.exit(0)
    print(f"ChapterStart: chapter {struct.unpack_from('<i', m, 1)[0]} ({time.time() - t0:.1f} s)", flush=True)
    s.sendall(frame(bytes([25]) + s32('{"health":100}') + s32('{"invItems":[]}') + s32('{"invItems":[]}')))
    m = early.get(31) or pump(400, lambda m: m[0] in (31, 32))
    if m is None: print("no WorldReload", flush=True); sys.exit(0)
    print(f"{'WorldReload' if m[0] == 31 else 'HostAway'} from the host ({time.time() - t0:.0f} s after the request)", flush=True)
    s.close()
    for _ in range(30):
        try: s, r, my_id = join(); break
        except RuntimeError as e: print(f"Hello again: {e}", flush=True); time.sleep(3)
    else: sys.exit(0)
    print("joined the new chapter", flush=True)
    s.sendall(frame(bytes([24])))
    m = pump(20, lambda m: m[0] == 26)
    print(f"CharacterLoad kind {m[1]} (3: ours from the last chapter, beside the host)" if m else "no CharacterLoad", flush=True)
s.close()
