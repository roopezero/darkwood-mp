"""A home router for the port-mapping test (Net/PortMapper.cs), on 127.0.0.1, so the real router is never asked.

    fakerouter.py MODE PORT SECONDS

MODE upnp: a UPnP IGD. Its description is at http://127.0.0.1:PORT/rootDesc.xml (the game is started with
    --dwmp-upnp-test 127.0.0.1:PORT, which skips SSDP). It takes only permanent leases (error 725 for any other,
    as some routers do), and says its external address is 100.64.1.2 (carrier-grade NAT, which the game must warn about).
MODE pmp: no UPnP (the description is a 404), but NAT-PMP on UDP 127.0.0.1:PORT. External address 203.0.113.7;
    it gives TCP external port 30000 and UDP 30001 (a router may pick other ports than asked).

Runs until SIGTERM or SECONDS, then prints what it was asked and a verdict: the game must have opened the game's
port for TCP and UDP to 127.0.0.1, then removed both mappings.
"""
import re, signal, socket, struct, sys, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MODE, PORT, SECS = sys.argv[1], int(sys.argv[2]), float(sys.argv[3])
GAME_PORT = 27720
SERVICE = "urn:schemas-upnp-org:service:WANIPConnection:1"
log = []           # what the game asked, in order
maps = {}          # (proto) -> (external, internal port, client)
added, removed = set(), set()
lock = threading.Lock()
wrong = []         # mappings that weren't of the game's port to this computer


def opened(proto, external, internal, client):
    maps[proto] = (external, internal, client)
    added.add(proto)
    if internal != GAME_PORT or client != "127.0.0.1":
        wrong.append(f"{proto} {internal} -> {client}")

DESC = f"""<?xml version="1.0"?>
<root xmlns="urn:schemas-upnp-org:device-1-0"><device>
<deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>
<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>
<serviceList><service><serviceType>{SERVICE}</serviceType><controlURL>/ctl/IPConn</controlURL></service></serviceList>
</device></deviceList></device></deviceList></device></root>"""


def envelope(inner):
    return ('<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" '
            f's:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/"><s:Body>{inner}</s:Body></s:Envelope>')


def fault(code, desc):
    return envelope('<s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail>'
                    f'<UPnPError xmlns="urn:schemas-upnp-org:control-1-0"><errorCode>{code}</errorCode>'
                    f'<errorDescription>{desc}</errorDescription></UPnPError></detail></s:Fault>')


class Igd(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def reply(self, status, body):
        b = body.encode()
        self.send_response(status)
        self.send_header("Content-Type", 'text/xml; charset="utf-8"')
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def do_GET(self):
        if MODE == "upnp" and self.path == "/rootDesc.xml":
            self.reply(200, DESC)
        else:
            self.reply(404, "")

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        action = self.headers.get("SOAPAction", "").strip('"').split("#")[-1]
        f = dict(re.findall(r"<(New\w+)>([^<]*)</New\w+>", body))
        with lock:
            log.append(f"{action} {f}")
            if action == "GetExternalIPAddress":
                return self.reply(200, envelope(f'<u:GetExternalIPAddressResponse xmlns:u="{SERVICE}">'
                                                '<NewExternalIPAddress>100.64.1.2</NewExternalIPAddress></u:GetExternalIPAddressResponse>'))
            if action == "AddPortMapping":
                if f.get("NewLeaseDuration") != "0":
                    return self.reply(500, fault(725, "OnlyPermanentLeasesSupported"))
                opened(f["NewProtocol"], int(f["NewExternalPort"]), int(f["NewInternalPort"]), f["NewInternalClient"])
                return self.reply(200, envelope(f'<u:AddPortMappingResponse xmlns:u="{SERVICE}"/>'))
            if action == "DeletePortMapping":
                if maps.pop(f.get("NewProtocol"), None) is None:
                    return self.reply(500, fault(714, "NoSuchEntryInArray"))
                removed.add(f["NewProtocol"])
                return self.reply(200, envelope(f'<u:DeletePortMappingResponse xmlns:u="{SERVICE}"/>'))
        self.reply(500, fault(401, "Invalid Action"))


def natpmp():
    u = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    u.bind(("127.0.0.1", PORT))
    while True:
        q, frm = u.recvfrom(64)
        if len(q) < 2 or q[0] != 0:
            continue
        op = q[1]
        with lock:
            if op == 0:
                log.append("pmp external address")
                u.sendto(struct.pack(">BBHI4s", 0, 128, 0, 1, socket.inet_aton("203.0.113.7")), frm)
            elif op in (1, 2) and len(q) >= 12:
                internal, external, life = struct.unpack(">HHI", q[4:12])
                proto = "UDP" if op == 1 else "TCP"
                log.append(f"pmp {proto} internal {internal} external {external} lifetime {life}")
                if life == 0:
                    if maps.pop(proto, None) is not None:
                        removed.add(proto)
                    given = 0
                else:
                    given = 30000 if proto == "TCP" else 30001
                    opened(proto, given, internal, frm[0])
                u.sendto(struct.pack(">BBHIHHI", 0, 128 + op, 0, 1, internal, given, life), frm)


def finish(*_):
    with lock:
        print(f"fake router ({MODE}) was asked:")
        for line in log:
            print("  " + line)
        print(f"  opened: {sorted(added)}  removed: {sorted(removed)}  still open: {maps}")
        ok = added == {"TCP", "UDP"} and removed == {"TCP", "UDP"} and not maps and not wrong
        print(f"port mapping ({MODE}): {'PASS' if ok else 'FAIL'}" + (f" (wrong mappings: {wrong})" if wrong else ""))
    sys.stdout.flush()
    sys.exit(0)


signal.signal(signal.SIGTERM, finish)
signal.signal(signal.SIGALRM, finish)
signal.setitimer(signal.ITIMER_REAL, SECS)
http = ThreadingHTTPServer(("127.0.0.1", PORT), Igd)
threading.Thread(target=http.serve_forever, daemon=True).start()
threading.Thread(target=natpmp, daemon=True).start()
print(f"fake router ({MODE}) on 127.0.0.1:{PORT}", flush=True)
while True:
    signal.pause()
