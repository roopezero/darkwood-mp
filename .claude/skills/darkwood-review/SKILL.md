---
name: darkwood-review
description: Review and simplify the whole DarkwoodMP codebase (C#, test scripts, shell tools) with parallel subagents, then fix the major issues found, build and run the tests. Use when asked for a code review, cleanup or simplification of the whole mod, or of a large part of it.
---

# Reviewing and simplifying DarkwoodMP

Three phases, in this order: **review** (read only), **simplify** (behaviour kept), **fix** (the major findings). Each phase fans out to subagents, one per slice of the code; each subagent stays under about 100k tokens of context, so a slice is about 45k tokens of code (170 KB). The main session only partitions, dispatches, triages and runs the tests; it doesn't read the code itself.

Work files go in `.cache/review/` (git-ignored): `part-*.txt` (the slices), `review-*.md` (findings), `simplify-*.md` (what changed), `fix-*.md`.

## 0. Partition

```
python3 .claude/skills/darkwood-review/partition.py .cache/review
```

Slices: `prod*` (the mod), `test*` (MapScenario*, WorldTest and other in-game test code), `tools*` (Python peers, run-test.sh, pack.sh, launchers). Start from a clean `git status`, so every phase's changes can be seen with `git diff`.

## Rules every agent gets (paste them into each prompt)

- Read `CLAUDE.md` first: its "Rules the code depends on" bind every change.
- **Don't change behaviour that other code or the tests depend on by name or text:**
  - wire bytes (`MsgType` payloads, `Protocol.Version`): a simplification never touches them;
  - log line text: `tools/run-test.sh` and the Python tests wait on and grep the game's log lines, so keep every `Plugin.Log` message exactly as it is (search `tools/` for a line before thinking of changing it);
  - `--dwmp-*` arguments, test-control commands (`TestControl.cs`), `MapScenario.Add` step names, `StateDump` section names and keys;
  - Harmony patches: the patch method's parameter names (`__instance`, `__result`, `___field`, and the original's argument names), the patched method names and argument types, `[HarmonyPatch]` targets, and anything found by reflection or by name (`AccessTools.Method(..., "Name")`, `Traverse`);
  - public members used from another slice: if you change one, grep the whole of `src/` and `tools/` first.
- No `Steamworks` type outside `SteamBackend`, `Lobby`, `SteamTransport` (the build checks this).
- **Recursive deletes must be guarded.** No bare `rm -rf "$x"`, `rm -rf "$dir/"*`, `Directory.Delete(path, true)` or `shutil.rmtree(p)` on a path built from a variable. Shell: use the `safe_rm` helper in `tools/lib.sh` (it refuses an empty path, `/`, `$HOME`, and anything that doesn't resolve under one of the allowed roots it's given), with `set -u` on. C#: `WorldFiles.DeleteTree(path)` (refuses anything outside the mod's cache and save-redirect folders). Python: the same check before `rmtree`. A `mktemp -d` directory is removed with a check that it's still under the temp directory.
- Comments: plain, concrete sentences (see CLAUDE.md). Don't add comments that narrate the change.
- **Building:** builds share `obj/`, so serialize them with a lock and don't deploy:
  `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH; flock -o .cache/review/build.lock dotnet build src/DarkwoodMP -c Release -p:DeployToGame=false -nodeReuse:false -p:UseSharedCompilation=false 2>&1 | grep -E "error|warn|Build succeeded" | sort -u | head -40`.
  (`-o` and no node reuse or compiler server matter: a leftover compiler server inherits the lock otherwise and every later build waits for ever.) Other agents edit other files at the same time: an error in a file outside your slice isn't yours; build again a minute later if it blocks you. Python: `python3 -m py_compile FILE`. Shell: `bash -n FILE` (and `shellcheck` if it is installed).
- **Never** launch the game or `tools/run-test.sh`: one game per machine, and the main session runs the tests at the end.
- Don't commit, don't touch `git` state (no stash, checkout, reset).

## 1. Review (read only)

One `general-purpose` agent per slice, all in one message (they run in the background). Prompt: the rules, the slice file, and:

> Read every file in `.cache/review/part-NAME.txt`, whole. Don't edit any source file. Write `.cache/review/review-NAME.md` with:
> 1. **Major issues**: bugs that would hit players or the user (wrong sync, desync, crash, exception, a client acting on the host's world, an authority check missing or bypassable, a save written in the wrong place, a dangerous delete, a resource leak, a thread race, a protocol mismatch between C# and the Python peers). Each: `file:line`, what goes wrong and when (a concrete scenario), and the fix. Only what you checked against the code; mark a guess as unverified.
> 2. **Minor issues**: same form, lower stakes.
> 3. **Simplifications**: dead code (grep the repo to prove it unused), duplicated code that can share a helper, needless state, convoluted control flow, comments that are wrong or narrate history. Each with file:line and the change.
> Keep it under 250 lines. Reply with only the counts and the three most important majors, one line each.

Then the main session reads the `review-*.md` files' **major** sections (not the code) and makes `.cache/review/majors.md`: deduplicated, grouped by the files they touch, each group small enough for one agent, groups touching disjoint files. Always add a **deletes** group: every recursive delete in the repo (`grep -rnE 'rm -r|rm -fr|Directory.Delete|rmtree' src tools scripts pack.sh`), moved onto the guarded helpers, which it creates. Drop findings that are guesses or that contradict a decision in CLAUDE.md.

## 2. Simplify (edits, behaviour kept)

One agent per slice again, all at once; each edits **only the files of its slice** (which keeps them from colliding). Prompt: the rules and:

> Simplify the files in `.cache/review/part-NAME.txt`, keeping what they do. Start from the "Simplifications" in `.cache/review/review-NAME.md` (check each before applying it), then your own pass: remove dead code (proved unused by grep across `src/` and `tools/`), merge duplicates into a helper, flatten needless nesting and state, fix wrong comments. Don't fix bugs in this phase unless the fix is a one-liner that is clearly right; the majors are fixed next. Don't change anything in the "don't change" list. Leave recursive deletes alone in this phase: one fix group (below) does all of them, so the helpers are made once. Build (with the lock) until your files compile with no new warnings. Write `.cache/review/simplify-NAME.md`: what you changed, one line each, and the lines removed (git diff --stat on your files). Reply with that summary in under 15 lines.

After all finish, the main session builds once (with deploy), and fixes or dispatches any error.

## 3. Fix the majors

One agent per group in `majors.md`, all at once (the groups touch disjoint files). Prompt: the rules, the group's findings, and:

> Check each finding against the code as it is now (the simplify pass changed it). Fix the real ones with the smallest correct change, following CLAUDE.md's patterns (authority, replay, relays, ObjRef). If a fix changes a message's bytes, bump `Protocol.Version` and update the Python peer in `tools/tests/` that reads it (say so in your reply: other groups might collide). If a finding is wrong, say why. Build with the lock. Write `.cache/review/fix-GROUP.md`: each finding, fixed / not a bug / left (why), and which test covers it. Reply with that in under 15 lines.

## 3b. Verify the diff (regressions)

The simplify pass can break things the compiler doesn't catch (one run left a local function that called itself: a stack overflow on every join). So before testing, split `git diff` (plus new files) by file into chunks of about 800 changed lines (`git diff --numstat`), and give each chunk to an agent:

> Review `git diff -- <files>` (and the new files) for regressions: behaviour changed where the simplify pass meant to keep it (self-recursion, inverted or dropped conditions, a removed call still needed, a helper that does slightly less than the code it replaced, changed order of side effects, wrong default arguments), changed log text that tools/ greps (`git diff | grep '^-.*Log'` against `grep -rn` in tools/), changed wire bytes, renamed Harmony parameters or targets, removed members still used by name (reflection, Traverse, AccessTools, test-control commands, step names in tools/tests). For each fix from phase 3 in your files (see `fix-*.md`), check it does what it says without breaking the honest path. Fix clear regressions in your files (build with the lock). Write `verify-N.md`; reply in under 15 lines.

## 4. Build, test, report

1. `dotnet build src/DarkwoodMP -c Release` (deploys): no errors, no new warnings.
2. Tests, in the background, one run at a time (see `darkwood-test`): `tools/run-test.sh all`, then `tools/run-test.sh map` if the fixes touched sync. Every item must end with `no exceptions` and `your saves: unchanged`. A failure: read its log, check it against `git stash`-free reasoning (compare with the last release's behaviour in the CHANGELOG), and fix it or dispatch an agent to.
3. Docs: if behaviour players see changed, update `docs/` and `CHANGELOG.md` (see CLAUDE.md "Docs"); keep `docs/DEVELOPMENT.md` true for any tool that changed.
4. Report to the user: the majors fixed (one line each), the ones left and why, lines removed by the simplify pass (`git diff --stat | tail -1`), and the test results. Don't commit unless asked (or the `darkwood-release` skill is run).
