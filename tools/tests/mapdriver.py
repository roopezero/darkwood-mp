"""Drives the games of the `map` and `map4` tests (tools/run-test.sh map / map4) through their test controls.

    mapdriver.py pre HOST_PORT                          the scenario's PRE_JOIN steps, in the host's game
    mapdriver.py run HOST_PORT CPORT[,CPORT...] WORKDIR wait for every world, run STEPS, dump every game and
                                                        compare each client's dump with the host's

The client games are c1, c2, ... in the order of their ports ("client" is c1). The scenario is the module named
by the environment variable MAP_SCENARIO (default map_scenario: tools/tests/map_scenario.py); it has STEPS and
may have AT_JOIN (run before STEPS), PRE_JOIN, POST, REQUIRE and SETTLE. Who does a step: "host", "c1".."c4", "client" (c1) or "all-clients"
(each client in turn), or "c1+c2.." (those games at once, see run_steps). ("all", "compare", WHO, SECTIONS) (or "both") dumps every game there and then around WHO's
player and compares each client's dump with the host's, with SECTIONS (comma-separated) required not to be empty.
Dumps go to WORKDIR: host.json and client.json with one client, host.json and c1.json.. with more; mid-way ones are
midN-host.json, midN-client.json / midN-c1.json... Exits 1 if a step fails, a wait times out or the dumps differ.
"""
import importlib, os, subprocess, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from ctl import Ctl, good  # noqa: E402
sc = importlib.import_module(os.environ.get("MAP_SCENARIO") or "map_scenario")
SETTLE = getattr(sc, "SETTLE", 1.5)
# MAP_SETTLE=SECS: a shorter (or longer) default wait after each step, for quick runs; steps with their own "settle"
# keep it. The modules' own SETTLE is the reference: a pass with a shorter one is not a pass with the default.
if os.environ.get("MAP_SETTLE"):
    SETTLE = float(os.environ["MAP_SETTLE"])
# A module run alone (a map battery) runs all of its own steps: its AT_JOIN (steps that must come right after the
# join, which map_scenario.py puts first) before its STEPS. A module with only POST steps has no STEPS.
STEPS = list(getattr(sc, "AT_JOIN", [])) + list(getattr(sc, "STEPS", []))
# The full scenario ends its steps by checking that nobody died (a death changes the story flags and resets things the
# dumps compare); a module alone ends the same way.
if STEPS and sc.__name__.startswith("map_scenario_") and [s[:2] for s in STEPS[-2:]] != [("host", "alive"), ("client", "alive")]:
    STEPS += [("host", "alive"), ("client", "alive")]


def log(msg):
    print(f"  [{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def clients(ctls):
    return [k for k in ctls if k != "host" and k != "client"]


def fname(ctls, who):
    """A client's dump file name: "client" with one client game (as the two-game test always had), else c1..."""
    return "client" if len(clients(ctls)) == 1 else who


def dump_all(ctls, tag, x, z, work):
    """Every game's dump around (x, z), into WORK/TAG-host.json (or host.json for tag ""), and so on."""
    pre = f"{tag}-" if tag else ""
    paths, replies = {}, {}
    for who in ["host"] + clients(ctls):
        paths[who] = os.path.join(work, f"{pre}{'host' if who == 'host' else fname(ctls, who)}.json")
    # All at once: one after another, things that move (a running rabbit) were seconds apart between the dumps.
    threads = [threading.Thread(target=lambda w=w: replies.__setitem__(w, ctls[w].cmd('dump', paths[w], x, z))) for w in paths]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    for who in paths:
        log(f"{who} dump: {replies.get(who, 'no reply')}")
    return paths


def compare_all(ctls, paths, require):
    bad = []
    for who in clients(ctls):
        if len(clients(ctls)) > 1:
            print(f"  --- {who} against the host", flush=True)
        # require None: mapcompare.py's own list (a scenario without REQUIRE).
        r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "mapcompare.py"), paths["host"], paths[who]]
                           + ([] if require is None else ["--require", require]))
        if r.returncode:
            bad.append(who)
    return bad


def compare(ctls, around, tag, require, work):
    """Dump every game around one player ("where-me" in WHO's game) and compare each client's dump with the host's;
    the reply is "done ..." or "fail ..." like a step's."""
    where = ctls[around].cmd("do", "where-me")
    if not good(where):
        return f"fail where-me: {where}"
    x, z = where.split()[1:3]
    log(f"dumps around {around}'s player at {x} {z}")
    bad = compare_all(ctls, dump_all(ctls, tag, x, z, work), require)
    return "done dumps SAME" if not bad else f"fail dumps DIFFER ({', '.join(bad)} against the host)"


def run_steps(ctls, steps, work=None):
    failed = 0
    n = 0
    for n_step, step in enumerate(steps, 1):
        opts = step[-1] if isinstance(step[-1], dict) else {}
        words = [str(w) for w in (step[:-1] if opts else step)]
        who, name, args = words[0], words[1], words[2:]
        t0 = time.time()
        if who in ("both", "all") and name == "compare":
            # ("all", "compare", WHO, SECTIONS): every game's dump there and then, around WHO's player, compared.
            n += 1
            reply = compare(ctls, args[0], f"mid{n}", args[1] if len(args) > 1 else "", work or ".")
            ok = good(reply)
        elif "+" in who:
            # ("c1+c2+c3", STEP, ARGS...): the step in those games at once; an "AT" argument becomes an epoch second a
            # few seconds ahead, for the games to act at the same moment (they share this machine's clock). Options
            # "winners": N (exactly N replies start "done got"), "min_winners": N (at least N).
            games = who.split("+")
            at = f"{time.time() + float(opts.get('lead', 6)):.3f}"
            targs = [at if w == "AT" else w for w in args]
            replies = {}
            threads = [threading.Thread(target=lambda g=g: replies.__setitem__(g, ctls[g].cmd("do", name, *targs, timeout=180)))
                       for g in games if g in ctls]
            for t in threads:
                t.start()
            for t in threads:
                t.join()
            ok = len(replies) == len(games) and all(good(r) for r in replies.values())
            reply = "; ".join(f"{g}: {replies.get(g, 'no game')}" for g in games)
            got = sum(1 for r in replies.values() if r.startswith("done got"))
            if "winners" in opts and got != int(opts["winners"]):
                ok, reply = False, f"{got} got it, wanted {opts['winners']}: {reply}"
            if "min_winners" in opts and got < int(opts["min_winners"]):
                ok, reply = False, f"{got} got it, wanted at least {opts['min_winners']}: {reply}"
        elif who == "all-clients":
            replies = [(c, ctls[c].cmd("do", name, *args, timeout=180)) for c in clients(ctls)]
            ok = all(good(r) for _, r in replies)
            reply = "; ".join(f"{c}: {r}" for c, r in replies)
        elif who not in ctls:
            reply, ok = f"fail no game {who} (there are {', '.join(ctls)})", False
        else:
            reply = ctls[who].cmd("do", name, *args, timeout=180)
            ok = good(reply)
        log(f"{who:6} {name} {' '.join(args)} -> {reply} ({time.time() - t0:.1f} s)")
        if not ok and not opts.get("may_fail"):
            failed += 1
            print(f"  FAILED STEP {n_step}: {who} {name} {' '.join(args)} -> {reply}", flush=True)
        time.sleep(float(opts.get("settle", SETTLE)))
    return failed


def wait(c, who, event, secs, *more):
    reply = c.cmd("wait", event, secs, *more, timeout=secs + 30)
    log(f"{who} {event}{' ' + ' '.join(map(str, more)) if more else ''}: {reply}")
    return good(reply)


def wait_at_rest(host, most=40):
    """Wait until the dropped items around the host's test area have stopped moving. The test world hands out its
    kit with full bags, so some of it is dropped as the world is built and rolls for a while; the client loads the
    host's save as it is when it joins, and each game then lets those things roll on by itself (nobody moved them,
    so nobody shares where they stop). A client game started for the test takes about 15 s to start, enough for
    them to stop; a client game kept from the previous map item joins at once, and the dumps differed by 2-16."""
    import json, tempfile
    where = host.cmd("do", "where")
    if not good(where):
        print(f"  (no test area to watch for rolling things: {where})", flush=True)
        return
    x, z = where.split()[1:3]
    path = os.path.join(tempfile.mkdtemp(prefix="dwmp-rest-"), "host.json")
    t0, last = time.time(), None
    while True:
        host.cmd("dump", path, x, z)
        with open(path) as f:
            now = {k: v.get("pos") for k, v in json.load(f).get("sections", {}).get("dropped", {}).items()}
        if now == last:
            print(f"  phase: the host's {len(now)} dropped item(s) at rest after {time.time() - t0:.0f} s", flush=True)
            return
        if time.time() - t0 > most:
            print(f"  (the host's dropped items still moving after {most} s: joining anyway)", flush=True)
            return
        last = now
        time.sleep(2)


def main():
    mode = sys.argv[1]
    if mode == "pre":
        host = Ctl(int(sys.argv[2]))
        if not wait(host, "host", "world-ready", 120):
            sys.exit(1)
        failed = run_steps({"host": host}, getattr(sc, "PRE_JOIN", []))
        wait_at_rest(host)
        sys.exit(1 if failed else 0)

    hp, cports, work = int(sys.argv[2]), [int(p) for p in sys.argv[3].split(",")], sys.argv[4]
    os.makedirs(work, exist_ok=True)
    host = Ctl(hp)
    ctls = {"host": host}
    for i, p in enumerate(cports):
        ctls[f"c{i + 1}"] = Ctl(p, connect_for=180)  # the client's game may still be starting
    ctls["client"] = ctls["c1"]
    t0 = time.time()
    for c in clients(ctls):
        if not wait(ctls[c], c, "world-ready", 300):
            print(f"  FAILED: {c}'s world wasn't ready in 300 s", flush=True)
            sys.exit(1)
    if not wait(host, "host", "player-joined", 180, len(cports)):
        print("  FAILED: the host had no player in after 180 s", flush=True)
        sys.exit(1)
    print(f"  phase: client game start and join in {time.time() - t0:.0f} s", flush=True)
    # Every game picks things nearest to the same spot: the host's test area.
    where = host.cmd("do", "where")
    log(f"host's test area: {where}")
    x, z = where.split()[1:3]
    for c in clients(ctls):
        ctls[c].cmd("do", "centre", x, z)
    t0 = time.time()
    failed = run_steps(ctls, STEPS, work)
    print(f"  phase: {len(STEPS)} steps in {time.time() - t0:.0f} s", flush=True)

    # The last dumps: around the test area, every client against the host.
    t0 = time.time()
    req = getattr(sc, "REQUIRE", None)
    bad = compare_all(ctls, dump_all(ctls, "", x, z, work), None if req is None else ",".join(req))
    print(f"  phase: final dumps and compare in {time.time() - t0:.0f} s", flush=True)
    # Steps that change the world for good (a death) run after the dumps.
    t0 = time.time()
    post = getattr(sc, "POST", [])
    failed += run_steps(ctls, post)
    print(f"  phase: {len(post)} post steps in {time.time() - t0:.0f} s", flush=True)
    if failed:
        print(f"steps: {failed} FAILED")
    if bad:
        print(f"final dumps DIFFER: {', '.join(bad)} against the host")
    sys.exit(1 if failed or bad else 0)


if __name__ == "__main__":
    main()
