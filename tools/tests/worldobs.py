"""Joins a DarkwoodMP host as an observer and records its shared-world messages (time, doors, containers, spawns).

worldobs.py PORT SECS OUT: watches for at most SECS; SIGTERM (the runner, once the host's script is done) ends it
early, and what was recorded is written either way."""
import json, pickle, signal, socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, ask, frame, hello, rstr, rbig, s16

PORT, SECS, OUT = int(sys.argv[1]), float(sys.argv[2]), sys.argv[3]
# --ask-location=NAME: 5 s after joining, ask the host where that separate location stands (LocationAsk), as a
# joiner's game does before loading it; the answer (LocationSlot) is printed.
ASK_LOC = next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--ask-location=")), None)
stop = False
def on_term(*_):
    global stop; stop = True
signal.signal(signal.SIGTERM, on_term)
s = socket.create_connection(("127.0.0.1", PORT), timeout=10); r = Reader(s)
m = ask(s, r, hello(name="Observer"), 20); assert m[0] == 4, m[0]
h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]; got = off = 0
while got < size:
    while off < size and off - got < 4 * (256 << 10):
        s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
    m = r.msg(20)
    if m[0] == 6: got += struct.unpack_from("<I", m, 37)[0]
s.sendall(hello(h, name="Observer"))
while True:
    m = r.msg(20)
    if m[0] == 2: my_id = struct.unpack_from("<Q", m, 1)[0]; break
print("joined as", my_id, flush=True)
host_pos = None
ACTIONS = {41: "NpcDied", 21: "TrapFired", 22: "TrapSwitched", 23: "TrapDisarm", 31: "ItemOn", 32: "ItemOff", 33: "ItemUnlock", 34: "ItemSearched", 35: "PadlockUnlock", 1: "DoorOpen", 2: "DoorClose", 3: "DoorBarricade", 4: "DoorDestroyBarricade", 5: "DoorDestroy", 6: "DoorHit",
           7: "DoorLock", 8: "DoorUnlock", 9: "DoorUndestroy", 11: "WindowBarricade", 12: "WindowDestroyBarricade", 13: "WindowHit",
           51: "ItemHit", 52: "ItemBroken", 53: "ItemRepaired", 61: "Explode",
           90: "InteractiveSwitch", 91: "Removed", 92: "FeederUsed", 93: "LiquidLit",
           130: "VineHit", 131: "ShadowArmorDied"}
KINDS = {1: "Saved", 2: "Spawned", 3: "Enemy", 4: "Placed"}
recorded, times = [], []
slot_seen, slots_done = [], False
locked = False
t0 = time.time(); nxt = 0
while time.time() - t0 < SECS and not stop:
    if ASK_LOC and time.time() - t0 > 5:
        s.sendall(frame(bytes([115, 0]) + s16(ASK_LOC))); print(f"LocationAsk {ASK_LOC}", flush=True); ASK_LOC = None
    if host_pos and time.time() >= nxt:  # stand next to the host's character, so we're "near" what it does
        x, y, z = host_pos
        s.sendall(frame(bytes([10]) + struct.pack("<Q", my_id) + struct.pack("<fff", x + 40, y, z) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([1]) + s16("") + s16("") + bytes([0, 0, 100]) + s16("") + struct.pack("<fff", 0, 0, 0) + struct.pack("<f", 0)))
        nxt = time.time() + 0.1
    try: m = r.msg(0.05)
    except socket.timeout: continue
    t = m[0]
    if t == 10 and struct.unpack_from("<Q", m, 1)[0] == 1:
        host_pos = struct.unpack_from("<fff", m, 9)
    elif t == 116:
        name, o = rstr(m, 1); x, y, z = struct.unpack_from("<fff", m, o); rot = struct.unpack_from("<h", m, o + 12)[0]
        print(f"LocationSlot {name} at {x:.0f},{z:.0f}, turned {rot}", flush=True)
    elif t == 20:
        day, minute = struct.unpack_from("<IH", m, 1); times.append((round(time.time() - t0, 1), day, minute)); recorded.append(m)
    elif t == 21:
        a = m[1]; k = m[2]; oid = struct.unpack_from("<q", m, 3)[0]; x, y = struct.unpack_from("<ff", m, 11)
        print(f"WorldAction {ACTIONS.get(a, a)} {KINDS.get(k, k)}:{oid} ({x:.0f},{y:.0f})", flush=True); recorded.append(m)
    elif t == 22:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; js, _ = rbig(m, 10)
        items = [(i["type"], i["amount"]) for i in json.loads(js).get("invItems", [])]
        print(f"ContainerState {KINDS.get(k, k)}:{oid}: {items}", flush=True); recorded.append(m)
        if k == 1 and not locked:  # hold the container the host just looted
            locked = True
            s.sendall(frame(bytes([28]) + bytes([k]) + struct.pack("<q", oid) + bytes([1])))
            print(f"  locked {KINDS[k]}:{oid}", flush=True)
    elif t == 27:
        n = struct.unpack_from("<H", m, 1)[0]; at = 3; keys = []
        for _ in range(n):
            key, at = rstr(m, at); val, at = rstr(m, at); keys.append(f"{key}={val[:40]}")
        print(f"FlagsDelta {n}: {keys}", flush=True); recorded.append(m)
    elif t == 29:
        print("ContainerBusy", flush=True)
    elif t == 23:
        oid = struct.unpack_from("<q", m, 1)[0]; prefab, at = rstr(m, 10); pos = struct.unpack_from("<fff", m, at); at += 12 + 16
        js, _ = rbig(m, at); items = [(i["type"], i["amount"]) for i in json.loads(js).get("invItems", [])] if js else []
        print(f"ObjSpawn #{oid} {prefab} at {pos[0]:.0f},{pos[2]:.0f}: {items}", flush=True); recorded.append(m)
    elif t == 33:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; x, y, z = struct.unpack_from("<fff", m, 10); last = m[38]
        if last or not any(isinstance(e, bytes) and e[0] == 33 and e[1:10] == m[1:10] for e in recorded[-3:]):
            print(f"ObjMove {KINDS.get(k, k)}:{oid} to {x:.0f},{z:.0f}{' (let go)' if last else ''}", flush=True)
        recorded.append(m)
    elif t == 35:
        by = struct.unpack_from("<Q", m, 1)[0]; typ, at = rstr(m, 9); x, y, z = struct.unpack_from("<fff", m, at)
        print(f"ItemThrown {typ} by #{by} from {x:.0f},{z:.0f}", flush=True); recorded.append(m)
    elif t == 36:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; what = m[10]; secs = struct.unpack_from("<f", m, 11)[0]; on = m[15]
        print(f"Burning {KINDS.get(k, k)}:{oid} ({ {1: 'enemy', 2: 'door', 3: 'window', 4: 'item'}.get(what, what)}) {'on' if on else 'off'} for {secs:.0f}s", flush=True); recorded.append(m)
    elif t == 37:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; n = m[10]; at = 11; desc = []
        for _ in range(n):
            slot = struct.unpack_from("<H", m, at)[0]; at += 2; old, at = rstr(m, at); new, at = rstr(m, at)
            desc.append(f"{slot}: '{old}' -> {json.loads(new)['type'] + ' x' + str(json.loads(new)['amount']) if new else 'empty'}")
            slot_seen.append((k, oid, slot, old, new))
        print(f"ContainerSlots {KINDS.get(k, k)}:{oid}: {desc}", flush=True); recorded.append(m)
        # The host emptied the slot it filled: now our turn. Fill it (that stands), and claim the next slot held
        # something it never held (too late: refused). Then upgrade the workbench.
        if not slots_done and len(slot_seen) >= 2 and slot_seen[-1][4] == "":
            slots_done = True
            k, oid, slot, _, filled = slot_seen[-2][0], slot_seen[-2][1], slot_seen[-2][2], None, slot_seen[-2][4]
            p = bytes([37, k]) + struct.pack("<q", oid) + bytes([2])
            p += struct.pack("<H", slot) + s16("") + s16(filled)
            p += struct.pack("<H", slot + 1) + s16("bogus|1|1000|0|0") + s16("")
            s.sendall(frame(p))
            s.sendall(frame(bytes([39]) + struct.pack("<i", 5)))
            print(f"  sent: fill slot {slot}, empty slot {slot + 1} (too late), workbench level 5", flush=True)
            # Sort & Stack pressed by us (StorageSort in a Request): the host sorts its storage and sends us every slot.
            s.sendall(frame(bytes([40]) + struct.pack("<I", 1) + bytes([0xCA, k]) + struct.pack("<q", oid)))
            print("  sent: Sort & Stack (request 1)", flush=True)
            # For the client test: a refusal of a change it never made, giving back what it "put in".
            item = json.loads(filled); item["amount"] = 1
            recorded.append(bytes([39]) + struct.pack("<i", 5))  # the upgrade, as the host passes it on
            recorded.append(bytes([38, k]) + struct.pack("<q", oid) + struct.pack("<H", slot + 2) + s16("") + s16("") + s16(json.dumps(item)))
    elif t == 41:  # Verdict: u32 seq, u8 ok, str reason, str result
        seq = struct.unpack_from("<I", m, 1)[0]; ok = m[5]; reason, _ = rstr(m, 6)
        print(f"Verdict {seq}: {'ok' if ok else 'refused: ' + reason}", flush=True)
    elif t == 38:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; slot = struct.unpack_from("<H", m, 10)[0]
        print(f"ContainerSlotRefused {KINDS.get(k, k)}:{oid} slot {slot}", flush=True)
    elif t == 44:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; what = m[10]; fuel = struct.unpack_from("<f", m, 11)[0]; on = m[15]
        print(f"Fuel {KINDS.get(k, k)}:{oid} ({ {1: 'generator', 2: 'saw'}.get(what, what)}): {fuel:.1f}{', running' if on else ''}", flush=True); recorded.append(m)
    elif t == 66:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; opt = struct.unpack_from("<i", m, 10)[0]
        print(f"Construct {KINDS.get(k, k)}:{oid} option {opt}", flush=True); recorded.append(m)
    elif t == 67:
        kind = m[1]; k = m[2]; oid = struct.unpack_from("<q", m, 3)[0]; idx = m[11]; name, _ = rstr(m, 12)
        print(f"EventFired {({1: 'trigger ', 3: 'world trigger '}[kind] + KINDS.get(k, str(k)) + ':' + str(oid) + ' #' + str(idx)) if kind in (1, 3) else 'world event ' + name}", flush=True); recorded.append(m)
    elif t == 0xD0:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; x, z = struct.unpack_from("<ff", m, 10); name, _ = rstr(m, 18)
        print(f"EggDestroyed {KINDS.get(k, k)}:{oid} ({name}) at {x:.0f},{z:.0f}", flush=True); recorded.append(m)
    elif t == 121:
        src, dst = m[1], m[2]; pid, sid = struct.unpack_from("<qq", m, 3); js, _ = rbig(m, 19)
        items = [(i["type"], i["amount"]) for i in json.loads(js).get("invItems", [])]
        print(f"PorterDelivered hideout {src} -> {dst}: package Spawned:{pid} spawner Spawned:{sid} {items}", flush=True); recorded.append(m)
    elif t == 122:
        k = m[1]; oid = struct.unpack_from("<q", m, 2)[0]; k2 = m[10]; oid2 = struct.unpack_from("<q", m, 11)[0]
        print(f"OvenLit {KINDS.get(k, k)}:{oid}" + (f", out {KINDS.get(k2, k2)}:{oid2}" if k2 else ""), flush=True); recorded.append(m)
    elif t == 0xD1:
        n = struct.unpack_from("<H", m, 1)[0]
        refs = [f"{KINDS.get(m[3 + i * 17], m[3 + i * 17])}:{struct.unpack_from('<q', m, 4 + i * 17)[0]}" for i in range(n)]
        print(f"InfectionCleared {n}: {' '.join(refs)}", flush=True); recorded.append(m)
    elif t == 0xD2:
        oid = struct.unpack_from("<q", m, 1)[0]; x, y, z = struct.unpack_from("<fff", m, 9)
        print(f"InfectionSpawned Spawned:{oid} at {x:.0f},{z:.0f}", flush=True); recorded.append(m)
    elif t == 39:
        print(f"WorkbenchLevel {struct.unpack_from('<i', m, 1)[0]}", flush=True); recorded.append(m)
    elif t in (62, 65):  # NightScenario, Weather: sent every 10 s; keep each new one
        if any(isinstance(e, bytes) and e == m for e in recorded): continue
        if t == 62: print(f"NightScenario '{rstr(m, 1)[0]}'", flush=True)
        else:
            f = m[1]; start, dur, pre, light = struct.unpack_from("<ffff", m, 2); fog = struct.unpack_from("<iiii", m, 18)
            print(f"Weather rain {'yes' if f & 1 else 'no'} at {start:.0f} for {dur:.0f}, lightning {pre:.0f}/{light:.0f}, fog {fog[1]}/{fog[0]}-{fog[3]}/{fog[2]}", flush=True)
        recorded.append(m)
    elif t == 63:
        if m[1] == 1:
            sc, at = rstr(m, 2); ev, at = rstr(m, at); where, _ = rstr(m, at)
            print(f"NightEvent {sc}/{ev} at '{where}'", flush=True)
        else:
            print(f"NightEvent shadow lights: generator {KINDS.get(m[2], m[2])}:{struct.unpack_from('<q', m, 3)[0]}, lamp {KINDS.get(m[11], m[11])}:{struct.unpack_from('<q', m, 12)[0]}", flush=True)
        recorded.append(m)
    elif t == 64:
        if m[1] == 1:
            n = struct.unpack_from("<H", m, 2)[0]; at = 4; full = 0
            for _ in range(n):
                at += 8 + 2 + 2 + 12; js, at = rstr(m, at); full += bool(js and json.loads(js).get("invItems"))
            print(f"RandomObjects {n} of the dawn ({full} holding something)", flush=True)
        else:
            oid = struct.unpack_from("<q", m, 2)[0]; prefab, at = rstr(m, 10)
            print(f"RandomObjects night object {prefab} #{oid}", flush=True)
        recorded.append(m)
print("time syncs:", len(times), "first/last:", times[:1], times[-1:])
pickle.dump(recorded, open(OUT, "wb"))
