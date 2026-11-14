"""Characters against a DarkwoodMP host: join (new character), send one, wait for the host to save, rejoin, get it back."""
import json, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import frame, rbig, s32
from worldtest import join as join_host

PORT = int(sys.argv[1]); KEY = "d" * 64

def join():
    s, r, _, offer = join_host(PORT, "Keeper", key=KEY, timeout=30)
    print(f"  downloaded the offered world {offer[1:33].hex()[:12]} ({struct.unpack_from('<I', offer, 33)[0]} bytes)")
    return s, r

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
s.sendall(frame(bytes([25]) + s32(mine_p) + s32(mine_inv) + s32(mine_hot) + struct.pack("<i", 1)))  # i32: our world is in chapter 1
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
s.sendall(frame(bytes([25]) + s32(mine_p) + s32(mine_inv) + s32(mine_hot) + struct.pack("<i", 1)))  # (i32: chapter 1) the watcher's game goes on sending it
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
