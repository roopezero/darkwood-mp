"""Plays a remote player against a DarkwoodMP host: joins, stands at a spot, watches enemies, hits one.

combattest.py PORT X Z SECS [UNTIL]: plays for at most SECS. UNTIL, a comma-separated list, ends it as soon as all
of these have happened: verdict (the host's answer to our hit), bitten (an enemy's hit reached us), blood, sound,
reload (WorldReload), chapter (ChapterStart), end (GameEnd, which also ends it), combat (CombatMusic on), and with the host's --dwmp-test-blast: blast, aura, flame (PlayerDamage
from the test's enemy bomb, damage aura and flame), boom (EnemyExplosion), scream and effect (EnemyEffect) and
afternight (AfterNightEnded); with --dwmp-test-fx: psound (a PlayerSounds from the host's
player), hurt (its blood), burn (it started and stopped burning). Once welcomed it also sends its own player fx
(sounds, a shot, blood, burning on and off), which the host shows on its puppet. With cues in UNTIL, once bitten (and
the hit answered) it also sends a swing and an aim (PlayerCue), turns invisible for 6 s (counting bites after the
first 1.5 s), then shows its wards and forest skills for 2 s with a torch in hand (for 3 s); cues is done after that.
SIGTERM ends it too, with the same summary. Exits 1 (a FAIL line) if something in UNTIL never happened."""
import signal, socket, struct, sys, time
sys.path.insert(0, sys.argv[0].rsplit("/", 1)[0])
from worldtest import Reader, ask, frame, hello, rstr, s16  # same folder

PORT = int(sys.argv[1]); X, Z = float(sys.argv[2]), float(sys.argv[3]); SECS = float(sys.argv[4])
UNTIL = set(sys.argv[5].split(",")) if len(sys.argv) > 5 and sys.argv[5] else set()
stop = False
def on_term(*_):
    global stop; stop = True
signal.signal(signal.SIGTERM, on_term)
s = socket.create_connection(("127.0.0.1", PORT), timeout=10)
r = Reader(s)
m = ask(s, r, hello(name="Fighter"), 20)
assert m[0] == 4, f"expected SaveOffer, got {m[0]}"
h = m[1:33]; size = struct.unpack_from("<I", m, 33)[0]
got = 0; off = 0
while got < size:
    while off < size and off - got < 4 * (256 << 10):
        s.sendall(frame(bytes([5]) + h + struct.pack("<II", off, 256 << 10))); off += 256 << 10
    m = r.msg(20)
    if m[0] == 6:
        got += struct.unpack_from("<I", m, 37)[0]
print(f"world downloaded ({got} bytes)", flush=True)
s.sendall(hello(h, name="Fighter"))
while True:
    m = r.msg(20)
    if m[0] == 2:
        my_id = struct.unpack_from("<Q", m, 1)[0]; print("welcomed as", my_id, flush=True); break

held = ""  # the lit thing in hand
away_bits = 0  # 1 away (a separate location or a dream)
away_place = ""  # where, when away: the location's name ("dream:<name>" in a dream)
state_bits = 1  # 1 alive, 8 invisible, 16 shadow ward, 32 forest spirit ward, 64 friend / 128 enemy of the forest
def player_state():
    # u64 owner, vec3 pos, quat rot (Euler(90,0,0)), u8 state, str library, str held light, u8 sprites, u8 more state (1 away), u8 health %, str place, vec3 held flames (on the torch: the host logs where it put them), f32 lantern light radius (none)
    return frame(bytes([10]) + struct.pack("<Q", my_id) + struct.pack("<fff", X, 16.0, Z) + struct.pack("<ffff", 0.7071, 0, 0, 0.7071) + bytes([state_bits]) + s16("") + s16(held) + bytes([0]) + bytes([away_bits, 100]) + s16(away_place) + struct.pack("<fff", *((14.0, 22.0, 0.0) if held else (0.0, 0.0, 0.0))) + struct.pack("<f", 0))

counts, enemies, damage, sounds = {}, {}, [], 0
t0 = time.time(); next_state = 0; hit_sent = False; next_noise = 0; noises = 0
seen = set()
fx_at = t0 + 2.0; fx_step = 0; fx_got = {}  # our own fx, once the host has our puppet
def own_fx(t, body): return frame(bytes([t]) + struct.pack("<Q", my_id) + body)
cue_at = None; invisible_bites = 0; away_at = None
while time.time() - t0 < SECS and not stop and not (UNTIL and UNTIL <= seen):
    now = time.time()
    if now >= next_state:
        s.sendall(player_state()); next_state = now + 0.05
    if now >= next_noise:  # footsteps: noise where we stand, as the game's own (range 150, volume 1)
        s.sendall(frame(bytes([19]) + struct.pack("<fff", X, 16.0, Z) + struct.pack("<ff", 150.0, 1.0) + bytes([0]))); next_noise = now + 1.0; noises += 1
    if fx_step < 3 and now >= fx_at:
        if fx_step == 0:
            s.sendall(own_fx(50, bytes([2]) + s16("player_melee_hit") + struct.pack("<f", 1.0) + s16("footsteps_grass") + struct.pack("<f", 0.5)))
            s.sendall(own_fx(51, s16("") + s16("") + bytes([1]) + struct.pack("<fff", 0, 10, 0)))
            s.sendall(own_fx(52, bytes([1]) + s16("FX/Bloodsplats/Shotsplat_stay") + struct.pack("<ff", 0, 45)))
        elif fx_step == 1:
            s.sendall(own_fx(52, bytes([2]) + s16("fire_flamesV2_burningEnemy_01") + s16("fire_loop_01")))
        else:
            s.sendall(own_fx(52, bytes([3])))
            print("sent our player fx: sounds, a shot, blood, burning on and off", flush=True)
        fx_step += 1; fx_at = now + 1.5
    # The hit, once an enemy is known: after it has bitten us (it's on us), or half way through at the latest.
    if not hit_sent and enemies and (damage or now - t0 > SECS / 2):
        eid = min(enemies, key=lambda e: (enemies[e]["pos"][0] - X) ** 2 + (enemies[e]["pos"][2] - Z) ** 2)
        print(f"hitting enemy {eid} ({enemies[eid]['name']}) for 25", flush=True)
        s.sendall(frame(bytes([40]) + struct.pack("<I", 1) + bytes([17]) + struct.pack("<If", eid, 25.0) + bytes([2 | 4 | 8, 0]) + s16("")))  # a Request: the host decides
        hit_sent = True
    if "cues" in UNTIL and cue_at is None and "bitten" in seen and "verdict" in seen:
        cue_at = now
        s.sendall(frame(bytes([70, 0]))); s.sendall(frame(bytes([70, 1])))
        print("sent a swing and an aim (PlayerCue); turning invisible", flush=True)
        state_bits = 1 | 8; next_state = 0
    elif cue_at is not None and "cues" not in seen:
        if state_bits & 8 and now - cue_at > 6:
            print(f"bites while invisible (after 1.5 s): {invisible_bites}; visible again, with wards, Friend of the Forest and a torch", flush=True)
            state_bits = 1 | 16 | 32 | 64; held = "torch"; next_state = 0
        elif state_bits & 16 and now - cue_at > 8:
            state_bits = 1; next_state = 0  # wards off; the torch stays a second (the host logs whether it lights the proxy)
        elif state_bits == 1 and held and now - cue_at > 9:
            held = ""; next_state = 0; seen.add("cues")
    if "away" in UNTIL and "cues" in seen and "away" not in seen:  # away for 3 s: the host's enemies leave our proxy be
        if away_at is None: away_at = now; away_bits = 1; away_place = "outside_test_location"; next_state = 0; print("away (a separate location or a dream) for 3 s", flush=True)
        elif away_bits and now - away_at > 3: away_bits = 0; away_place = ""; next_state = 0; print("back in the world", flush=True)
        elif not away_bits and now - away_at > 6: seen.add("away")
    try:
        m = r.msg(0.02)
    except socket.timeout:
        continue
    except (EOFError, ConnectionResetError):
        print("the host closed the connection", flush=True); break
    t = m[0]; counts[t] = counts.get(t, 0) + 1
    if t == 14:
        eid, uid = struct.unpack_from("<Ii", m, 1)
        path, at = rstr(m, 9); name, at = rstr(m, at)
        pos = struct.unpack_from("<fff", m, at)
        enemies[eid] = {"name": name, "pos": pos}
        print(f"EnemySpawn {eid}: {name} (save id {uid}, prefab {path}) at {pos[0]:.0f},{pos[2]:.0f}", flush=True)
    elif t == 15:
        n = struct.unpack_from("<H", m, 5)[0]; at = 7  # after f32 the host's clock
        for _ in range(n):
            eid = struct.unpack_from("<I", m, at)[0]; pos = struct.unpack_from("<fff", m, at + 4); at += 4 + 12 + 16
            alive = m[at] & 1; at += 1 + 4  # bit 1: in play, bit 2: asleep
            an = m[at]; at += 1 + an * 11
            if eid in enemies:
                enemies[eid]["pos"] = pos; enemies[eid]["alive"] = alive
    elif t == 13:
        d = struct.unpack_from("<f", m, 1)[0]; who, at = rstr(m, 6); damage.append((round(d), who))
        test = {"DarkwoodMP_TestBlast": "blast", "DarkwoodMP_TestAura": "aura", "DarkwoodMP_TestFlame": "flame"}.get(who)
        seen.add(test or "bitten")
        if not test and state_bits & 8 and cue_at is not None and now - cue_at > 1.5: invisible_bites += 1
        n_eff = m[at]
        print(f"PlayerDamage {d:.0f} from {who}" + (f" (+{n_eff} effects)" if n_eff else ""), flush=True)
        if test: print(f"area damage ({test}): {d:.0f} from {who}" + (f", {n_eff} effects" if n_eff else ""), flush=True)
    elif t == 47:
        pos = struct.unpack_from("<fff", m, 1); sound, at = rstr(m, 13); fx, _ = rstr(m, at); seen.add("boom")
        print(f"EnemyExplosion at {pos[0]:.0f},{pos[2]:.0f}: sound {sound!r}, effect {fx!r}", flush=True)
    elif t == 78:
        print(f"CombatMusic: {'in combat' if m[1] else 'out of combat'}", flush=True)
        if m[1]: seen.add("combat")
    elif t == 79:
        kind = m[1]
        if kind == 3:
            eid, tog = struct.unpack_from("<Ii", m, 2); print(f"EnemyEffect: enemy {eid}'s emitter {tog}", flush=True)
        else:
            name, at = rstr(m, 2); pos = struct.unpack_from("<fff", m, at)
            print(f"EnemyEffect: {name} at {pos[0]:.0f},{pos[2]:.0f}", flush=True)
            seen.add("scream" if name == "FX/BansheeScream" else "effect")
    elif t == 80:
        print("AfterNightEnded from the host", flush=True); seen.add("afternight")
    elif t == 18:
        sounds += 1; seen.add("sound")
    elif t == 30:
        seen.add("blood")
    elif t == 50:  # PlayerSounds: u64 player, u8 n, n x (str id, f32 volume)
        n = m[9]; at = 10
        for _ in range(n):
            a, at = rstr(m, at); at += 4; fx_got.setdefault("sounds", []).append(a)
        seen.add("psound")
    elif t == 51:
        fx_got["shots"] = fx_got.get("shots", 0) + 1
    elif t == 52:  # PlayerHurt: u64 player, u8 what, then what's
        what = m[9]
        if what == 1:
            prefab, _ = rstr(m, 10); fx_got.setdefault("blood", []).append(prefab); seen.add("hurt")
        elif what == 2:
            flames, at = rstr(m, 10); sound, _ = rstr(m, at); fx_got["burn on"] = (flames, sound)
        elif what == 3:
            fx_got["burn off"] = True
        if "burn on" in fx_got and "burn off" in fx_got: seen.add("burn")
    elif t == 41:
        ok = m[5]; reason, at = rstr(m, 6); result, _ = rstr(m, at)
        print(f"Verdict on the hit: {'ok' if ok else 'refused: ' + reason}{', damage ' + result if result else ''}", flush=True)
        seen.add("verdict")
    elif t == 31:
        print("WorldReload from the host", flush=True); seen.add("reload")
    elif t == 32:
        print("HostAway from the host: leaving, as a game does", flush=True)
        s.close(); break
    elif t == 86:
        print(f"ChapterStart from the host: chapter {struct.unpack_from('<i', m, 1)[0]}", flush=True); seen.add("chapter")
    elif t == 87:
        print(f"GameEnd from the host: {({1: 'the epilogue', 2: 'the end screen'}).get(m[1], m[1])}; leaving, as a game does", flush=True)
        seen.add("end"); s.close(); break
print(f"played {time.time() - t0:.0f} s" + (f"; everything in {sorted(UNTIL)} happened" if UNTIL and UNTIL <= seen else ""))
print("noises sent:", noises)
print("message counts:", counts)
print("sounds from enemies:", sounds, "blood splats:", counts.get(30, 0))
print("damage taken:", damage)
snd = fx_got.pop("sounds", [])
print(f"the host's player fx: {len(snd)} sounds ({', '.join(sorted(set(snd))[:8])})", fx_got)
for e, v in enemies.items():
    p = v["pos"]; print(f"  enemy {e} {v['name']} last at {p[0]:.0f},{p[2]:.0f} alive={v.get('alive')}")
# What was waited for and never came is a failure (on stderr too: the runner filters what this prints).
if UNTIL - seen:
    print(f"FAIL never happened: {', '.join(sorted(UNTIL - seen))}", flush=True)
    print(f"  combattest.py: FAIL never happened: {', '.join(sorted(UNTIL - seen))}", file=sys.stderr, flush=True)
    sys.exit(1)
