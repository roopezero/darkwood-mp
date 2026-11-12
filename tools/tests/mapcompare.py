"""Compares the host's and the client's dumps of the shared world (StateDump.cs, the `map` test).

    mapcompare.py HOST.json CLIENT.json [--require SECTION,SECTION...] [--ignore SECTION.FIELD,...]

Entries are matched by key (their ObjRef); Placed entries whose key differs a little (the object lies a unit
or two differently) are paired by name and position. Numbers must be equal, except fields with a tolerance
(positions, the hour, durability, fuel). "local" (each player's own things) is printed, not compared.
Prints a report per section and exits 1 on any difference, or if a required section is empty in both.
"""
import json, re, sys

POS_TOL = 1.5
SECTION_POS_TOL = {"enemies": 60.0, "enemylooks": 60.0,  # enemies move: the client shows them a little behind the host
                   "npcpos": 60.0}  # and so do NPCs (NpcSync: twice a second, when they moved 30)
FIELD_TOL = {"hour": 0.26, "durability": 0.051, "fuel": 1.01}
DEFAULT_REQUIRED = ["doors", "containers", "items", "story", "time"]
# The host sends a client only the enemies within NetEnemies.RelevantRange (3000) of that client's player, so an enemy
# only in the host's dump, this far or more from the client's player (its dump's local player pos), isn't missing.
# The margin covers an enemy that left the range while its EnemyDespawn was on the way.
SECTION_RANGE = {"enemies": 3000.0 + 300.0, "enemylooks": 3000.0 + 300.0}


def base(key):
    return re.sub(r"#\d+$", "", key)


def diff(a, b, path, pos_tol, out, ignore):
    """Differences between two values, as strings, into out."""
    field = path.rsplit(".", 1)[-1]
    if field in ignore:
        return
    if isinstance(a, dict) and isinstance(b, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a or k not in b:
                out.append(f"{path}.{k}: host {a.get(k, '-')!r} client {b.get(k, '-')!r}")
            else:
                diff(a[k], b[k], f"{path}.{k}", pos_tol, out, ignore)
    elif field == "pos" and isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        if any(abs(x - y) > pos_tol for x, y in zip(a, b)):
            out.append(f"{path}: host {a} client {b}")
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            out.append(f"{path}: host {len(a)} long {short(a)} client {len(b)} long {short(b)}")
        else:
            for i, (x, y) in enumerate(zip(a, b)):
                diff(x, y, f"{path}[{i}]", pos_tol, out, ignore)
    elif isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool):
        if abs(a - b) > FIELD_TOL.get(field, 1e-6):
            out.append(f"{path}: host {a} client {b}")
    elif a != b:
        out.append(f"{path}: host {a!r} client {b!r}")


def short(v):
    s = json.dumps(v)
    return s if len(s) < 120 else s[:117] + "..."


def pair(h, c, pos_tol):
    """Match host and client entries: by key, then Placed ones by name and nearest position."""
    pairs, only_h, only_c = [], [], []
    left_c = dict(c)
    for k in sorted(h):
        if k in left_c:
            pairs.append((k, h[k], left_c.pop(k)))
        else:
            only_h.append(k)
    still = []
    for k in only_h:
        e = h[k]
        best = None
        if base(k).startswith("Placed:") and isinstance(e, dict) and "pos" in e:
            for ck, ce in left_c.items():
                if not base(ck).startswith("Placed:") or not isinstance(ce, dict) or ce.get("name") != e.get("name") or "pos" not in ce:
                    continue
                d = max(abs(x - y) for x, y in zip(e["pos"], ce["pos"]))
                if d <= max(pos_tol, 50) and (best is None or d < best[0]):
                    best = (d, ck)
        if best:
            pairs.append((f"{k} ~ {best[1]}", e, left_c.pop(best[1])))
        else:
            still.append(k)
    return pairs, still, sorted(left_c)


def main():
    args = sys.argv[1:]
    required, ignore = list(DEFAULT_REQUIRED), set()
    if "--require" in args:
        i = args.index("--require"); required = [s for s in args[i + 1].split(",") if s]; del args[i:i + 2]
    if "--ignore" in args:
        i = args.index("--ignore"); ignore = set(args[i + 1].split(",")); del args[i:i + 2]
    host, client = (json.load(open(p)) for p in args[:2])
    hs, cs = host.get("sections", {}), client.get("sections", {})
    bad = 0
    print(f"host: {host.get('role')} around {host.get('centre')}; client: {client.get('role')} around {client.get('centre')}")
    for name in sorted(set(hs) | set(cs)):
        h, c = hs.get(name, {}), cs.get(name, {})
        if "error" in h or "error" in c:
            print(f"  {name}: ERROR host {h.get('error', '-')} client {c.get('error', '-')}")
            bad += 1
            continue
        pos_tol = SECTION_POS_TOL.get(name, POS_TOL)
        pairs, only_h, only_c = pair(h, c, pos_tol)
        diffs = []
        for k, a, b in pairs:
            out = []
            diff(a, b, k, pos_tol, out, ignore)
            diffs += out
        far = []
        me = ((client.get("local", {}).get("player") or {}).get("player") or {}).get("pos")
        if name in SECTION_RANGE and me:
            def dist(e):
                p = e.get("pos") if isinstance(e, dict) else None
                return ((p[0] - me[0]) ** 2 + (p[1] - me[1]) ** 2) ** 0.5 if p else 0.0
            far = [k for k in only_h if dist(h[k]) >= SECTION_RANGE[name]]
            only_h = [k for k in only_h if k not in far]
        diffs += [f"{k}: only in the host's game {short(h[k])}" for k in only_h]
        diffs += [f"{k}: only in the client's game {short(c[k])}" for k in only_c]
        empty = not h and not c and name in required
        ok = not diffs and not empty
        bad += 0 if ok else 1
        print(f"  {name}: {len(pairs)} matched, {len(diffs)} differences{' (EMPTY in both)' if empty else ''}"
              f"{f', {len(far)} beyond the client player range' if far else ''}{'' if ok else '  <-- FAIL'}")
        for d in diffs[:25]:
            print(f"      {d}")
        if len(diffs) > 25:
            print(f"      ... and {len(diffs) - 25} more")
    for who, d in (("host", host), ("client", client)):
        loc = d.get("local", {})
        print(f"  local ({who}): " + "; ".join(f"{k} {short(v)}" for k, v in sorted(loc.items())))
    print("dumps: SAME" if not bad else f"dumps: {bad} section(s) DIFFER")
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
