"""A DarkwoodMP host's handshake over TCP (goasis-style framing: u32 len, u8 type, payload): refusals, and a join
that passes the checks (answered with the host's world). The host must be in a world, with Password = test."""
import socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import PROTO

HOST, PORT = "127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 27720
KEY_A = "a" * 64
KEY_B = "b" * 64

def s16(s):
    b = s.encode()
    return struct.pack("<H", len(b)) + b

def hello(version=PROTO, game="darkwood", key=KEY_A, password="test", name="TestBot"):
    p = struct.pack("<BH", 1, version) + s16(game) + s16(key) + s16(password) + s16("an older one") + s16(name) + bytes(32)
    return struct.pack("<I", len(p)) + p

def read_msgs(sock, wait=1.5, waits_offer=False):
    sock.settimeout(10 if waits_offer else wait)
    buf, out = b"", []
    end = time.time() + wait
    try:
        while time.time() < end:
            d = sock.recv(65536)
            if not d:
                out.append(("closed",))
                break
            buf += d
            while len(buf) >= 4:
                n = struct.unpack_from("<I", buf)[0]
                if len(buf) < 4 + n:
                    break
                m, buf = buf[4:4 + n], buf[4 + n:]
                t = m[0]
                if t == 135:
                    continue  # Ping (keepalive)
                if t == 2:
                    out.append(("Welcome", struct.unpack_from("<Q", m, 1)[0]))
                elif t == 3:
                    n2 = struct.unpack_from("<H", m, 1)[0]
                    out.append(("Refused", m[3:3 + n2].decode()))
                elif t == 12:
                    pid = struct.unpack_from("<Q", m, 1)[0]
                    n2 = struct.unpack_from("<H", m, 9)[0]
                    out.append(("PlayerInfo", pid, m[11:11 + n2].decode()))
                elif t == 4:
                    out.append(("SaveOffer", struct.unpack_from("<I", m, 33)[0]))
                    if waits_offer: end = time.time() + 0.5
                elif t == 34:
                    n2 = struct.unpack_from("<H", m, 1)[0]
                    out.append(("JoinWait", m[3:3 + n2].decode()))
                    if waits_offer: end = max(end, time.time() + 8)  # the host is making its world: the offer follows
                elif t == 10:
                    if ("PlayerState",) not in out:
                        out.append(("PlayerState",))
                else:
                    out.append(("type", t))
    except socket.timeout:
        pass
    except ConnectionResetError:
        out.append(("reset",))
    return out

def attempt(label, msg, keep=False, waits_offer=False):
    s = socket.create_connection((HOST, PORT), timeout=3)
    s.sendall(msg)
    r = read_msgs(s, waits_offer=waits_offer)
    print(f"{label:32} -> {r}")
    if keep:
        return s
    s.close()

attempt("wrong password", hello(password="nope"))
attempt("no password", hello(password=""))
attempt("older protocol version", hello(version=PROTO - 1))
attempt("other game", hello(game="go-game"))
attempt("good join (offered the world)", hello(), waits_offer=True)

# Limits before Hello, and the keepalive timeout (Net/TcpBackend.cs).
def raw(label, data, wait):
    s = socket.create_connection((HOST, PORT), timeout=3)
    t0 = time.time()
    if data:
        s.sendall(data)
    r = read_msgs(s, wait)
    print(f"{label:32} -> {r} after {time.time() - t0:.0f}s")
    s.close()

raw("too big before Hello", struct.pack("<I", 1 << 20) + bytes(64), 3)
raw("no Hello (closed after 10 s)", b"", 14)
raw("Ping, then silent (15 s)", hello() + struct.pack("<IB", 1, 135), 19)
