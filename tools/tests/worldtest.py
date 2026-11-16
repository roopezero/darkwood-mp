"""World-sharing tests for DarkwoodMP, and the helpers the other tests share (framing, Hello, str fields, joining,
PlayerState, Requests).

  worldtest.py fetch <port> <outfile>   act as a client: download the host's world, check its hash, save it
  worldtest.py serve <port> <infile>    act as a host: offer the saved world, serve it, welcome the client
"""
import hashlib, json, math, os, pickle, re, socket, struct, sys, time, zlib
T0 = time.time()  # our clock for EnemyState starts here

GAME = "darkwood"
# The protocol version, read from the mod's source so these tests never drift from it.
_PACKETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src", "DarkwoodMP", "Net", "Packets.cs")
PROTO = int(re.search(r"ushort Version = (\d+);", open(_PACKETS).read()).group(1))
ENEMIES = "--enemies" in sys.argv
REPLAY = sys.argv[sys.argv.index("--replay") + 1] if "--replay" in sys.argv else None
CHARS = "--chars" in sys.argv
RELOAD = "--reload" in sys.argv
MENU = "--menu" in sys.argv    # with --chars: the client goes back to its main menu, leaves, and joins again
AWAY = "--away" in sys.argv    # 20 s in: HostAway (the host went back to its menu)
DROP = "--drop" in sys.argv    # 20 s in: just close the connection (the host quit or crashed)
CHAPTER = "--chapter" in sys.argv  # with --reload: ChapterStart first (the client sends its character), then WorldReload
END = int(sys.argv[sys.argv.index("--end") + 1]) if "--end" in sys.argv else 0  # 5 s in: GameEnd (1 the epilogue, 2 the end screen)
AUTH = "--authority" in sys.argv  # refuse the client's door action (with the door's state to roll back to)

def s16(s):
    b = s.encode() if isinstance(s, str) else s
    return struct.pack("<H", len(b)) + b

def frame(p):
    return struct.pack("<I", len(p)) + p

def hello(h=bytes(32), key="c" * 64, name="WorldBot", version=PROTO, game=GAME, password="", mod="0.4.0"):
    """Hello: u16 protocol, str game, str key, str password, str mod version, str name, 32 bytes world hash."""
    return frame(struct.pack("<BH", 1, version) + s16(game) + s16(key) + s16(password) + s16(mod) + s16(name) + h)

def ask(s, r, hb, timeout=10):
    """Send Hello and return the host's answer. JoinWait (34: it's loading, watching the prologue's intro, or in a dream) means ask again soon."""
    for _ in range(100):
        s.sendall(hb)
        m = r.msg(timeout)
        if m[0] != 34:
            return m
        print("  host says wait:", rstr(m, 1)[0], flush=True)
        time.sleep(3)
    raise RuntimeError("the host never let us in")

def join(port, name, key="c" * 64, timeout=20):
    """Join a host as a client: Hello until answered, download the offered world (4 pieces ahead), Hello again with its
    hash and wait for Welcome. Returns (socket, Reader, our player id, the SaveOffer). Raises RuntimeError if the host
    answers the first Hello with anything but a SaveOffer."""
    s = socket.create_connection(("127.0.0.1", port), timeout=10); r = Reader(s)
    offer = ask(s, r, hello(key=key, name=name), timeout)
    if offer[0] != 4:
        s.close(); raise RuntimeError(f"message {offer[0]} {rstr(offer, 1)[0] if offer[0] in (3, 34) else ''}")
    h = offer[1:33]; size = struct.unpack_from("<I", offer, 33)[0]; got = off = 0
    while got < size:
        while off < size and off - got < 4 * (256 << 10):
            s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
        m = r.msg(timeout)
        if m[0] == 6: got += struct.unpack_from("<I", m, 37)[0]
    s.sendall(hello(h, key=key, name=name))
    while True:
        m = r.msg(timeout)
        if m[0] == 2: return s, r, struct.unpack_from("<Q", m, 1)[0], offer

def player_state(owner, x=0.0, y=16.0, z=0.0, more=0, health=100):
    """PlayerState (10): u64 owner, vec3 pos, quat rot, u8 state (1 alive), str library, str held light, u8 sprites,
    u8 more (2: in an after-night), u8 health %, str place, vec3 held flames, f32 lantern, f32 beam radius, f32 beam brightness. Framed."""
    return frame(bytes([10]) + struct.pack("<Q", owner) + struct.pack("<fff", x, y, z) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071)
                 + bytes([1]) + s16("") + s16("") + bytes([0, more, health]) + s16("") + struct.pack("<fff", 0, 0, 0) + struct.pack("<fff", 0, 0, 0))

_seq = 0

def request(s, pump, label, inner):
    """Send inner as a Request (40: u32 seq + the message), pump(secs, until) up to 5 s for its Verdict (41: u32 seq,
    u8 ok, str reason, str result) and print it. Returns the ok byte, or None with no verdict."""
    global _seq
    _seq += 1; seq = _seq
    print(label, flush=True)
    s.sendall(frame(bytes([40]) + struct.pack("<I", seq) + inner))
    m = pump(5, lambda m: m[0] == 41 and struct.unpack_from("<I", m, 1)[0] == seq)
    if m is None: print("  -> no verdict", flush=True); return None
    ok = m[5]; reason, _ = rstr(m, 6)
    print(f"  -> {'ok' if ok else 'refused'}{': ' + reason if reason else ''}", flush=True)
    return ok

def world_hash(profile_json, files):
    h = hashlib.sha256(profile_json.encode())
    for name, data in files:
        h.update(struct.pack("<H", len(name)) + name.encode() + struct.pack("<I", len(data)))
        h.update(data)
    return h.digest()

class Reader:
    def __init__(self, sock):
        self.s, self.buf = sock, b""
    def msg(self, timeout):
        self.s.settimeout(timeout)
        while True:
            if len(self.buf) >= 4:
                n = struct.unpack_from("<I", self.buf)[0]
                if len(self.buf) >= 4 + n:
                    m, self.buf = self.buf[4:4 + n], self.buf[4 + n:]
                    if m == bytes([135]):
                        continue  # Ping (TCP keepalive). Scripts never send one, so the game doesn't time them out.
                    return m
            d = self.s.recv(1 << 20)
            if not d:
                raise EOFError("closed")
            self.buf += d

def rstr(m, at):
    n = struct.unpack_from("<H", m, at)[0]
    return m[at + 2:at + 2 + n].decode(), at + 2 + n

def rbig(m, at):
    """bigstr: u32 length + UTF-8 (the JSON fields)."""
    n = struct.unpack_from("<I", m, at)[0]
    return m[at + 4:at + 4 + n].decode(), at + 4 + n

def s32(s):
    b = s.encode()
    return struct.pack("<I", len(b)) + b

def fetch(port, out):
    s = socket.create_connection(("127.0.0.1", port), timeout=5)
    r = Reader(s)
    m = ask(s, r, hello())
    if m[0] != 4:
        print("expected SaveOffer, got type", m[0], m[1:200]); return
    h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]
    pj, at = rbig(m, 37)
    n = m[at]; at += 1
    names = []
    for _ in range(n):
        name, at = rstr(m, at); fs = struct.unpack_from("<I", m, at)[0]; at += 4
        names.append((name, fs))
    packing = m[at]; raw_size = struct.unpack_from("<I", m, at + 1)[0]
    print(f"SaveOffer: hash {h.hex()[:12]} size {size} (packing {packing}, {raw_size} unpacked) files {names}")
    print(f"profile json: {pj[:160]}...")
    blob = bytearray(size); got = 0; off = 0; t0 = time.time()
    while got < size:
        if off < size:
            s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
        m = r.msg(10)
        if m[0] in (4, 34): continue  # the same offer again, or a wait: answers to the Hellos ask() sent
        if m[0] != 6: print("unexpected", m[0]); return
        o, k = struct.unpack_from("<II", m, 33)
        blob[o:o + k] = m[41:41 + k]; got += k
    print(f"downloaded {got} bytes in {time.time() - t0:.2f}s")
    if packing == 1:
        blob = zlib.decompress(bytes(blob), -15)
        print(f"unpacked to {len(blob)} bytes")
    assert len(blob) == raw_size, (len(blob), raw_size)
    files, at = [], 0
    for name, fs in names:
        files.append((name, bytes(blob[at:at + fs]))); at += fs
    ok = world_hash(pj, files) == h
    print("hash check:", "OK" if ok else "MISMATCH")
    s.sendall(hello(h))
    m = r.msg(10)
    while m[0] in (4, 34): m = r.msg(10)
    print("after Hello with hash: type", m[0], "(2 = Welcome)", struct.unpack_from("<Q", m, 1)[0] if m[0] == 2 else m[1:80])
    pickle.dump({"hash": h, "profile": pj, "files": files}, open(out, "wb"))
    s.close()

def serve(port, inp):
    w = pickle.load(open(inp, "rb"))
    raw = b"".join(d for _, d in w["files"])
    z = zlib.compressobj(6, zlib.DEFLATED, -15); blob = z.compress(raw) + z.flush()  # packed as the host packs it (Deflate)
    ls = socket.socket(); ls.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    ls.bind(("127.0.0.1", port)); ls.listen(1); ls.settimeout(150)
    print("fake host listening on", port, flush=True)
    c, addr = ls.accept()
    print("client connected from", addr, flush=True)
    r = Reader(c); states = 0; t_end = None
    seen_fx = {}         # the client's PlayerSounds/PlayerShot/PlayerHurt, counted by type
    pos = (0, 0, 0)      # where the client's player is now
    nxt = 0              # when to send the next EnemyState
    fx_step = 0          # our player's fx: 0 not sent, 1 on, 2 off
    real_at = 0          # when the client's player first left the loading spot
    efx = efx_off = despawned = corpse_gone = replayed = sent_back = checked = refused_event = False
    char_sent = 0        # when we answered the client's CharRequest
    while True:
        try:
            m = r.msg(30 if t_end else 150)
        except (socket.timeout, EOFError) as e:
            print("stopped:", type(e).__name__, flush=True); break
        t = m[0]
        if t == 1:
            ch = m[-32:]
            if ch == w["hash"]:
                print("Hello with our world's hash -> Welcome", flush=True)
                c.sendall(frame(bytes([2]) + struct.pack("<Q", 1000)))
                c.sendall(frame(bytes([12]) + struct.pack("<Q", 1) + s16("FakeHost")))
                c.sendall(frame(bytes([251]) + struct.pack("<H", 30)))  # StackSize: the client stacks to 30 until it leaves
            else:
                print(f"Hello with hash {ch.hex()[:12]} -> SaveOffer", flush=True)
                p = bytes([4]) + w["hash"] + struct.pack("<I", len(blob)) + s32(w["profile"]) + bytes([len(w["files"])])
                for name, d in w["files"]:
                    p += s16(name) + struct.pack("<I", len(d))
                p += bytes([1]) + struct.pack("<I", len(raw))  # packing 1 (Deflate), unpacked size
                c.sendall(frame(p))
        elif t == 5:
            o, k = struct.unpack_from("<II", m, 33)
            piece = blob[o:o + k]
            c.sendall(frame(bytes([6]) + w["hash"] + struct.pack("<II", o, len(piece)) + piece))
        elif t == 10:
            states += 1; pos = struct.unpack_from("<fff", m, 9)
            if states == 1:
                x, y, z = struct.unpack_from("<fff", m, 9)
                print(f"first PlayerState from the client: the world is loaded (pos {x:.0f}, {y:.0f}, {z:.0f})", flush=True)
                t_end = time.time(); px, pz = x, z
                if ENEMIES:
                    def spawn(i, uid, path, name, ox):
                        c.sendall(frame(bytes([14]) + struct.pack("<Ii", i, uid) + s16(path) + s16(name) + struct.pack("<fff", px + ox, 16, pz) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([0])))  # no shadow armour
                    spawn(1, 707, "Characters/Dog", "Dog", 150)
                    spawn(2, 0, "Characters/DogMutated", "DogMutated", -150)
                    print("sent EnemySpawn for a save Dog and a new DogMutated", flush=True)
            if ENEMIES and t_end:
                el = time.time() - t_end
                if el < 6 and time.time() >= nxt:
                    nxt = time.time() + 0.1
                    p = struct.pack("<fH", time.time() - T0, 2)  # the host's clock, then how many
                    for i, ox in ((1, 150), (2, -150)):
                        a = el + i
                        p += struct.pack("<I", i) + struct.pack("<fff", px + ox + 60 * math.cos(a), 16, pz + 60 * math.sin(a)) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([0 if i == 1 and el > 3 else 1]) + struct.pack("<f", 1.0) + bytes([0])
                    c.sendall(frame(bytes([15]) + p))
                    # Our own player, beside theirs, and what it sounds and looks like (PlayerSounds, PlayerShot, PlayerHurt).
                    c.sendall(player_state(1, pos[0] + 80, 16, pos[2]))
                    # A second after the client says where its player really is (its first states come from the loading
                    # spot), so our puppet has got there and our sounds are near enough to be heard; by 4 s in at the latest.
                    if pos[0] < 70000 and not real_at: real_at = time.time()
                    if fx_step == 0 and ((real_at and time.time() - real_at > 1) or el > 4):
                        print(f"our player at {pos[0] + 80:.0f},{pos[2]:.0f}, beside the client's", flush=True)
                        c.sendall(frame(bytes([50]) + struct.pack("<Q", 1) + bytes([2]) + s16("player_melee_hit") + struct.pack("<f", 1.0) + s16("footsteps_grass") + struct.pack("<f", 0.5)))
                        c.sendall(frame(bytes([51]) + struct.pack("<Q", 1) + s16("") + s16("") + bytes([1]) + struct.pack("<fff", 0, 10, 0)))
                        c.sendall(frame(bytes([52]) + struct.pack("<Q", 1) + bytes([1]) + s16("FX/Bloodsplats/Shotsplat_stay") + struct.pack("<ff", 0, 45)))
                        c.sendall(frame(bytes([52]) + struct.pack("<Q", 1) + bytes([2]) + s16("fire_flamesV2_burningEnemy_01") + s16("fire_loop_01")))
                        fx_step = 1
                    elif fx_step == 1 and el > 5.5:
                        c.sendall(frame(bytes([52]) + struct.pack("<Q", 1) + bytes([3])))
                        print("sent our player's fx: sounds, a shot, blood, burning on and off", flush=True)
                        fx_step = 2
                    # The host's enemies' cues: once our player is where it really is, combat music on, the banshee's
                    # scream and the forest spirit's effect beside it, the DogMutated's emitter; then music off and the end
                    # of the host's after-night.
                    if not efx and ((real_at and time.time() - real_at > 1.5) or el > 4.5):
                        efx = True
                        qx, _, qz = pos
                        c.sendall(frame(bytes([78, 1])))
                        c.sendall(frame(bytes([79, 1]) + s16("FX/BansheeScream") + struct.pack("<ffff", qx - 150, 16, qz, 0)))
                        c.sendall(frame(bytes([79, 2]) + s16("ForestSpirit_fastSpawnEff") + struct.pack("<fff", qx + 100, 16, qz)))
                        c.sendall(frame(bytes([79, 3]) + struct.pack("<Ii", 2, 1)))
                        print("sent CombatMusic on and three EnemyEffects", flush=True)
                    if el > 5.5 and not efx_off:
                        efx_off = True
                        c.sendall(frame(bytes([78, 0]))); c.sendall(frame(bytes([80])))
                        print("sent CombatMusic off and AfterNightEnded", flush=True)
                elif el >= 6 and not despawned:
                    c.sendall(frame(bytes([16]) + struct.pack("<I", 2) + bytes([0]))); despawned = True
                    c.sendall(frame(bytes([16]) + struct.pack("<I", 1) + bytes([1])))
                    print("sent EnemyDespawn for the DogMutated, and for the Dog (dead since 3 s: its corpse stays)", flush=True)
                    # A corpse the client never saw die (dead when it joined, or one it walked away from): the host sends
                    # it all at once, spawned, dead and kept; it must settle at once and be searchable.
                    spawn(3, 0, "Characters/Dog", "Dog", 300)
                    c.sendall(frame(bytes([15]) + struct.pack("<fH", time.time() - T0, 1) + struct.pack("<I", 3) + struct.pack("<fff", px + 300, 16, pz) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([0]) + struct.pack("<f", 0.0) + bytes([0])))
                    c.sendall(frame(bytes([16]) + struct.pack("<I", 3) + bytes([1])))
                    print("sent a Dog's corpse at once (EnemySpawn, a dead EnemyState, EnemyDespawn keeping it)", flush=True)
                elif el >= 7.5 and not corpse_gone:
                    corpse_gone = True
                    c.sendall(frame(bytes([16]) + struct.pack("<I", 3) + bytes([0])))
                    print("sent EnemyDespawn for that corpse, not kept (the host's is gone)", flush=True)
            if REPLAY and t_end and time.time() - t_end > 5 and not replayed:
                replayed = True
                recs = pickle.load(open(REPLAY, "rb"))
                syncs = [m for m in recs if m[0] == 20]
                day = struct.unpack_from("<I", syncs[0], 1)[0] if syncs else 2
                for minute in (600, 605):
                    c.sendall(frame(bytes([20]) + struct.pack("<IH", day, minute))); time.sleep(1.0)
                for m2 in recs:
                    if m2[0] != 20:
                        c.sendall(frame(m2)); time.sleep(0.4)
                print(f"replayed {len([m for m in recs if m[0] != 20])} world messages and 2 time syncs", flush=True)
                time.sleep(3)  # for the game to apply the last of them
            # Once the client has its character (so it has one to save before leaving), and a moment more.
            if RELOAD and t_end and char_sent and time.time() - char_sent > 2:
                if CHAPTER:
                    c.sendall(frame(bytes([86]) + struct.pack("<i", 2)))
                    print("sent ChapterStart (chapter 2)", flush=True)
                    saved, until = False, time.time() + 4
                    while time.time() < until:
                        try: m2 = r.msg(0.2)
                        except socket.timeout: continue
                        if m2[0] == 25: saved = True
                    print(f"character sent at ChapterStart: {saved}", flush=True)
                c.sendall(frame(bytes([31])))
                print("sent WorldReload; waiting for the client to come back", flush=True)
                got_save = False
                try:
                    while True:
                        m2 = r.msg(10)
                        if m2[0] == 25: got_save = True
                except Exception as e:
                    print(f"old connection ended ({type(e).__name__}); character saved before leaving: {got_save}", flush=True)
                c2, _ = ls.accept(); r2 = Reader(c2)
                m2 = r2.msg(30)
                print(f"client came back: message type {m2[0]} (1 = Hello)", flush=True)
                break
            if MENU and t_end and char_sent:
                # The game goes back to its main menu by itself (--dwmp-test-client-menu): it must send its character,
                # close the connection, and then join again on a new one.
                got_save = False
                try:
                    while True:
                        m2 = r.msg(30)
                        if m2[0] == 25: got_save = True
                except Exception as e:
                    print(f"the client closed its connection ({type(e).__name__}); character saved before leaving: {got_save}", flush=True)
                c.close()
                c2, _ = ls.accept(); r2 = Reader(c2)
                m2 = r2.msg(30)
                print(f"client came back: message type {m2[0]} (1 = Hello)", flush=True)
                c2.close()
                break
            if END and t_end and time.time() - t_end > 5:
                if END == 1:
                    # The shared epilogue: phase 0, the client goes in and stays connected; then phase 1 (the host
                    # went to the credits), and it leaves for the credits and the main menu.
                    c.sendall(frame(bytes([87, 1, 0])))
                    print("sent GameEnd (the epilogue, phase 0: played together)", flush=True)
                    t_ep = time.time(); n = 0
                    c.settimeout(1)
                    while time.time() - t_ep < 40:
                        try: m2 = r.msg(1); n += 1
                        except socket.timeout: continue
                        except Exception as e:
                            print(f"the client closed its connection during the epilogue ({type(e).__name__})", flush=True); break
                    print(f"the client stayed connected for {time.time() - t_ep:.0f} s of the epilogue ({n} messages)", flush=True)
                    c.sendall(frame(bytes([87, 1, 1])))
                    print("sent GameEnd (the epilogue, phase 1: to the credits)", flush=True)
                else:
                    c.sendall(frame(bytes([87, END, 1])))
                    print("sent GameEnd (the end screen)", flush=True)
                try:
                    while True: r.msg(10)
                except Exception as e:
                    print(f"the client closed its connection ({type(e).__name__})", flush=True)
                c.close()  # the runner waits for the game to reach its menu
                break
            if (AWAY or DROP) and t_end and time.time() - t_end > 5:
                if AWAY:
                    c.sendall(frame(bytes([32])))
                    print("sent HostAway", flush=True)
                    try:
                        while True: r.msg(10)
                    except Exception as e:
                        print(f"the client closed its connection ({type(e).__name__})", flush=True)
                else:
                    print("closing the connection", flush=True)
                c.close()  # the runner waits for the game to reach its menu
                break
            if time.time() - t_end > (10 if ENEMIES else 8 if REPLAY else 60 if CHARS or AUTH else 40 if AWAY or DROP or END else 3):
                break
        elif t == 24 and CHARS:
            print("client asked for its character -> new", flush=True)
            char_sent = time.time()
            c.sendall(frame(bytes([26, 0]) + s32("") + s32("") + s32("") + struct.pack("<fff", 0, 0, 0)))
        elif t == 25 and CHARS:
            ps, at = rbig(m, 1); inv, at = rbig(m, at); hot, at = rbig(m, at)
            chapter = struct.unpack_from("<i", m, at)[0]  # the chapter of the client's world
            P = json.loads(ps); I = json.loads(inv)
            items = [(i["type"], i["amount"]) for i in I["invItems"]]
            print(f"client saved its character: pos {P['pos'][0]:.0f},{P['pos'][2]:.0f} health {P['health']:.0f} level {P['currentLevel']} items {items} slots {I['slotCount']} chapter {chapter}", flush=True)
            if not sent_back:
                sent_back = True
                P["pos"][0] += 300; P["health"] = 50.0; I["slotCount"] -= 2
                I["invItems"] = [{"slotId": 0, "type": "rope", "durability": 1.0, "amount": 3}]
                c.sendall(frame(bytes([26, 1]) + s32(json.dumps(P)) + s32(json.dumps(I)) + s32(hot) + struct.pack("<fff", 0, 0, 0)))
                print("sent it back changed: 300 east, health 50, 3 ropes", flush=True)
            elif not checked:
                checked = True; t_end = time.time() - 100  # done
        elif t == 40:  # a Request: refuse door actions (rolling the door back), allow the rest
            seq = struct.unpack_from("<I", m, 1)[0]; inner = m[5:]
            if AUTH and inner[0] == 21 and inner[1] in (1, 2):
                a, k, oid = inner[1], inner[2], struct.unpack_from("<q", inner, 3)[0]
                flags = 0 if a == 1 else 1  # as it was: closed if they opened it, open if they closed it
                c.sendall(frame(bytes([42, k]) + struct.pack("<q", oid) + bytes([1, flags]) + struct.pack("<ii", 60, 0) + s16("")))
                c.sendall(frame(bytes([41]) + struct.pack("<I", seq) + bytes([0]) + s16("test: refused") + s16("")))
                print(f"refused the client's {'DoorOpen' if a == 1 else 'DoorClose'} of {k}:{oid} (request {seq})", flush=True)
                t_end = time.time() - 30  # 30 seconds more: the rollback, then a scripted event and a dialogue
            elif AUTH and inner[0] == 68 and inner[10] == 1:  # TalkLock: someone else is talking to them
                c.sendall(frame(bytes([41]) + struct.pack("<I", seq) + bytes([0]) + s16("Somebody is talking to them") + s16("")))
                print(f"refused the client's TalkLock of {inner[1]}:{struct.unpack_from('<q', inner, 2)[0]} (request {seq})", flush=True)
            elif AUTH and inner[0] == 67 and inner[1] == 1 and not refused_event:
                # The client's trigger: refused (it "had already happened here"). The client should run none of it.
                refused_event = True
                c.sendall(frame(bytes([41]) + struct.pack("<I", seq) + bytes([0]) + s16("test: that has already happened") + s16("")))
                print(f"refused the client's EventFired {inner[1:12].hex()} (request {seq})", flush=True)
            else:
                if inner[0] in (66, 67):
                    print(f"client asked: {'Construct' if inner[0] == 66 else 'EventFired'} {inner[1:12].hex()} (allowed)", flush=True)
                c.sendall(frame(bytes([41]) + struct.pack("<I", seq) + bytes([1]) + s16("") + s16("")))
        elif t == 0xC8:  # ContainerSlotsAsk: ObjRef (u8 kind, i64 id). Answer ContainerSlotsAll with "": we haven't got it
            ref = m[1:10]
            c.sendall(frame(bytes([0xC9]) + ref + s32("")))
            print(f"client asked what {ref[0]}:{struct.unpack_from('<q', ref, 1)[0]} holds -> not here", flush=True)
        elif t in (50, 51, 52):  # PlayerSounds, PlayerShot, PlayerHurt: what our player sounded and looked like
            seen_fx[t] = seen_fx.get(t, 0) + 1
        elif t in (17, 19):
            print("client sent", "EnemyHit" if t == 17 else "PlayerNoise", flush=True)
    print("PlayerStates received:", states, flush=True)
    if seen_fx: print("the client's player fx (50 sounds, 51 shots, 52 hurt):", seen_fx, flush=True)

if __name__ == "__main__":
    {"fetch": fetch, "serve": serve}[sys.argv[1]](int(sys.argv[2]), sys.argv[3])
