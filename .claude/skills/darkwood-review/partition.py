#!/usr/bin/env python3
"""Splits the repo's code into slices of about BYTES each (default 170000, roughly 45k tokens) for review agents.

Writes OUT/part-NAME.txt (one path per line) and prints each slice's name, size and file count.
Production C#, test C# (the unit tests, MapScenario*, WorldTest, ...) and tools/scripts are kept in separate slices, in path order,
so one slice is a few neighbouring files.

    python3 .claude/skills/darkwood-review/partition.py OUT [BYTES]
"""
import os, re, subprocess, sys

out = sys.argv[1]
cap = int(sys.argv[2]) if len(sys.argv) > 2 else 170000
root = subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip()
files = subprocess.check_output(["git", "ls-files", "src", "tests", "tools", "scripts", "pack.sh"], cwd=root, text=True).split()
files = sorted(f for f in files if f.endswith((".cs", ".py", ".sh", ".csproj")))
test_re = re.compile(r"DarkwoodMP\.MapTests/|DarkwoodMP/Testing/|/(MapScenario|WorldTest|TestWorld|StateDump|AwayTest|DragTest|OptionsTest|TestControl|ModProfiler|LoadProfiler)")
groups = {"prod": [], "test": [], "tools": []}
for f in files:
    if f.startswith("tests/"):
        g = "test"
    else:
        g = "tools" if not f.startswith("src/") else "test" if test_re.search(f) else "prod"
    groups[g].append((os.path.getsize(os.path.join(root, f)), f))

os.makedirs(out, exist_ok=True)
for name, rows in groups.items():
    parts, cur, size = [], [], 0
    limit = cap * 0.7 if name == "tools" else cap  # tools files are denser to review (shell, protocol code)
    for n, f in rows:
        if cur and size + n > limit:
            parts.append(cur); cur, size = [], 0
        cur.append((n, f)); size += n
    if cur:
        parts.append(cur)
    for i, p in enumerate(parts, 1):
        with open(os.path.join(out, f"part-{name}{i}.txt"), "w") as fh:
            fh.write("\n".join(f for _, f in p) + "\n")
        print(f"{name}{i}\t{sum(n for n, _ in p)}\t{len(p)} files")
