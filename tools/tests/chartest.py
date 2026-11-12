"""Characters against a DarkwoodMP host: join (new character), send one, wait for the host to save, rejoin, get it back."""
import json, socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, frame, hello, rstr, rbig, s16, s32
from worldtest import ask as hello_until_answered

PORT = int(sys.argv[1]); KEY = "d" * 64

def join():
    s = socket.create_connection(("127.0.0.1", PORT), timeout=10); r = Reader(s)
    m = hello_until_answered(s, r, hello(key=KEY, name="Keeper"), 30); assert m[0] == 4, f"expected SaveOffer, got {m[0]}"
    h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]; got = off = 0
    while got < size:
        while off < size and off - got < 4 * (256 << 10):
            s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
        m = r.msg(30)
        if m[0] == 6: got += struct.unpack_from("<I", m, 37)[0]
    print(f"  downloaded the offered world {h.hex()[:12]} ({size} bytes)")
    s.sendall(hello(h, key=KEY, name="Keeper"))
    while True:
        m = r.msg(30)
        if m[0] == 2: return s, r

def ask(s, r):
    s.sendall(frame(bytes([24])))
    while True:
        m = r.msg(30)
        if m[0] == 26:
            kind = m[1]; p, at = rbig(m, 2); inv, at = rbig(m, at); hot, at = rbig(m, at); pos = struct.unpack_from("<fff", m, at)
            return kind, p, inv, hot, pos

print("first join:")
s, r = join()
kind, p, inv, hot, pos = ask(s, r)
print(f"  CharacterLoad kind={kind} (0 new, 1 yours) spawn at {pos[0]:.0f},{pos[2]:.0f}")
mine_p = json.dumps({"pos": [100.0, 16.0, 200.0], "health": 42.0, "experience": 77})
mine_inv = json.dumps({"slotCount": 12, "invItems": [{"slotId": 0, "type": "rope", "durability": 1.0, "amount": 3}]})
mine_hot = json.dumps({"slotCount": 4, "invItems": []})
s.sendall(frame(bytes([25]) + s32(mine_p) + s32(mine_inv) + s32(mine_hot)))
print("  sent my character (health 42, 3 ropes); waiting for the host to save")
time.sleep(12)
s.close()
time.sleep(2)
print("second join:")
s, r = join()
kind, p, inv, hot, pos = ask(s, r)
print(f"  CharacterLoad kind={kind}; health={json.loads(p).get('health') if p else None}; inventory={[(i['type'], i['amount']) for i in json.loads(inv)['invItems']] if inv else None}")
s.close()
print("dying for good:")
s, r = join()
kind, p, inv, hot, pos = ask(s, r)
s.sendall(frame(bytes([140])))  # CharacterDead: died for good, watching the others
s.sendall(frame(bytes([25]) + s32(mine_p) + s32(mine_inv) + s32(mine_hot)))  # the watcher's game goes on sending it
print("  sent CharacterDead, then my character again; waiting for the host to save")
time.sleep(12)
s.close()
time.sleep(2)
print("join after dying for good:")
s, r = join()
kind, p, inv, hot, pos = ask(s, r)
print(f"  CharacterLoad kind={kind} (4: dead for good, watch)")
s.close()
assert kind == 4, f"a character that died for good came back as kind {kind}"
