"""A DarkwoodMP host's handshake over TCP (goasis-style framing: u32 len, u8 type, payload): refusals, and a join
that passes the checks (answered with the host's world). The host must be in a world, with Password = test."""
import socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
import worldtest
from worldtest import PROTO

HOST, PORT = "127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 27720
KEY_A = "a" * 64

def hello(version=PROTO, game="darkwood", key=KEY_A, password="test", name="TestBot"):
    return worldtest.hello(key=key, name=name, version=version, game=game, password=password, mod="an older one")

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

def check(label, r, ok, want):
    """FAIL when the messages r aren't what the host should answer (want, for the FAIL line)."""
    if not ok: print(f"FAIL {label}: got {r}, expected {want}", flush=True)

def refused(r, reason):
    """The first answer is Refused with this reason (or one starting with it)."""
    got = [m for m in r if m[0] != "PlayerState"]
    return bool(got) and got[0][0] == "Refused" and got[0][1].startswith(reason)

def closed(r):
    return ("closed",) in r or ("reset",) in r

def attempt(label, msg, reason=None, waits_offer=False):
    """Hello and the host's answer: Refused with this reason (Session.HandleHello), or with none, a SaveOffer."""
    s = socket.create_connection((HOST, PORT), timeout=3)
    s.sendall(msg)
    r = read_msgs(s, waits_offer=waits_offer)
    print(f"{label:32} -> {r}")
    s.close()
    if reason is None: check(label, r, any(m[0] == "SaveOffer" for m in r), "a SaveOffer")
    else: check(label, r, refused(r, reason), f"Refused: {reason}")

attempt("wrong password", hello(password="nope"), "Wrong password.")
attempt("no password", hello(password=""), "This server needs a password.")
attempt("older protocol version", hello(version=PROTO - 1), "The host has DarkwoodMP ")
attempt("other game", hello(game="go-game"), "This server runs a different game.")
attempt("good join (offered the world)", hello(), waits_offer=True)

# Limits before Hello, and the keepalive timeout (Net/TcpBackend.cs): each must end with the host closing the
# connection, between lo and hi seconds after it opened.
def raw(label, data, lo, hi):
    s = socket.create_connection((HOST, PORT), timeout=3)
    t0 = time.time()
    if data:
        s.sendall(data)
    r = read_msgs(s, hi)
    took = time.time() - t0
    print(f"{label:32} -> {r} after {took:.0f}s")
    s.close()
    check(label, r, closed(r) and lo <= took <= hi + 0.5, f"closed after {lo}-{hi} s (took {took:.1f} s)")

raw("too big before Hello", struct.pack("<I", 1 << 20) + bytes(64), 0, 3)        # PreHelloMax 64 KiB
raw("no Hello (closed after 10 s)", b"", 9, 14)                                    # HelloTimeoutMs
raw("Ping, then silent (15 s)", hello() + struct.pack("<IB", 1, 135), 14, 19)     # TimeoutMs, once it has pinged
