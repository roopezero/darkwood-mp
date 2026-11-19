# DarkwoodMP.Codemod

The tool behind the formatting pass in `docs/MIGRATION_PLAN.md`. It rewrites the
plugin's sources (`src/DarkwoodMP`) in stages, each of which must leave the
compiled DLL byte for byte the same. It is a .NET 8 console program, in no
solution and not part of the mod's build. **It is deleted with the migration
plan at the end of Phase 8.**

## Building and running

The commands need the core DLL as a reference, so build the plugin first, then
the tool:

```bash
dotnet build src/DarkwoodMP -c Release -p:DeployToGame=false
dotnet build tools/DarkwoodMP.Codemod -c Release
dotnet tools/DarkwoodMP.Codemod/bin/Release/net8.0/DarkwoodMP.Codemod.dll census
```

Run it from anywhere inside the repo (or a worktree of it): it finds the root by
looking for `src/GameReferences.props`. The game must be installed, because the
compilation references its DLLs. Set the environment variable `GameDir` if it is
not in the default place.

## Commands

| Command | What it does |
|---|---|
| `census [--samples]` | Counts what the pass has to change, per construct and context: statements without braces, one-line blocks and bodies, lines over 80 columns, `?:`, `??`, `??=` and `?.`. Writes nothing. `--samples` adds example lines. |
| `braces` | Stage 1. Wraps every braceless statement in a block. Only the new blocks are formatted, and they line up with the code around them. |
| `layout` | Stage 2. Puts the braces of one-line blocks, switch statements, type bodies and namespaces on their own lines, gives an object initializer behind a multi-line argument list one line per member, and runs the Roslyn `Formatter` over every file. |
| `check [--braces] <dirA> <dirB>` | Compares two copies of the sources, file by file: the same comment words in the same order, the same code once whitespace is ignored, and every comment between the same two tokens of code, apart from the moves `layout` makes (see below). Stage 1 adds tokens, so check it with `--braces`: the second copy must be the first with only the new blocks added, and every comment between the same two tokens of code. Exits with 1 on any difference. |
| `rule <name> [--receivers unity\|other] --out DIR [--src DIR] [--dry-run]` | One operator rule (stages 3 to 7). Rewrites the sites it can, in the plugin's sources or in the copy given with `--src`, and writes the files listed under "Operator rules" below to `DIR`. `--dry-run` writes only `DIR`. |
| `ildiff <a.dll> <b.dll> [--manifest touched.json]` | Compares two builds method by method against a rule's manifest. Exits with 1 when anything outside it differs. |
| `difftest <rule>` | Runs the rule's differential cases in `DiffTest/Cases/<rule>.cases`. Exits with 1 if any differs. |
| `selftest [--show NAME]` | Runs the golden cases and the checks below. Exits with 1 if any fails. `--show` prints what the stages make of one case. |

`Prototype/Wrap.cs` is the prototype's width pass for stage 10. It compiles, but
no command runs it yet.

## What stops a command

`census`, `braces`, `layout` and `check` build the compilation the way the real
build does: the same files, language version, symbols and references. They stop
with exit code 2, before anything is written, if:

- the core DLL or another reference is missing (the message says what to do),
- the sources have a compile error,
- `UnityEngine.Object` doesn't resolve.

`braces` and `layout` rewrite every file in memory first. They write nothing
unless all of these hold:

- the comment words of every file are unchanged,
- the rewritten sources compile with no errors,
- the DLL emitted in memory is byte-identical to the one before,
- a second run of the stage changes nothing,
- nothing the stage is responsible for is left (no braceless statement, and
  after `layout` no one-line block or body),
- after `braces`, every file holds the same code apart from the new blocks: an
  else-if is still an else-if, each new block holds one statement, and every
  comment is between the same two tokens of code,
- after `layout`, every file holds the same code apart from whitespace, and
  every comment is between the same two tokens of code. Four moves are the
  stage's own, because they keep a comment with the code it explains:
  - a comment behind the closing brace of a block of several statements goes on
    the line of the block's header (`if`, `else`, `catch`, ...), unless the
    block has no header, holds another comment or the header's line already
    ends in a line comment,
  - any other comment behind a block's closing brace goes in front of it,
  - a comment behind the `break;` of a case section on one line goes to the
    statement before the break, when that is the section's only other
    statement,
  - a comment at the end of a line of several statements goes on a line of its
    own above the first of them.

Writing the files can still fail after every gate has passed: a read-only file
or folder, a full disk. Each new text goes to a temp file beside its source
first (`*.cs.codemod-tmp`), and the sources are replaced only when all of those
are complete. So a failed write stops the command with exit code 2, names the
file and leaves every source as it was. If replacing the sources fails part of
the way through, the message says how many were already replaced and to restore
them with `git checkout -- src/DarkwoodMP` in the repo's root. The temp files
are deleted in both cases.

`selftest` compiles its own snippets instead of the plugin. It needs neither the
game nor the core DLL.

## Proving a stage

The check inside the tool is only a first filter. The proof that counts for a
whitespace stage is a real build of the commit before and the commit after:

```bash
tools/verify-identical.sh HEAD~1 HEAD
```

Exit code 0 means every DLL is identical to the byte.

## Operator rules

`codemod rule` plans every site on the original tree, replaces the planned
statements, compiles the file with its tree replaced, and formats the result
with the layout stage's pass, so the new code has its braces and blocks on their
own lines. A site whose rewrite doesn't compile goes to the queue and the file
is rewritten without it. A rule never adds a lambda or a local function, because
the game's stack readers would see the extra frame. `DIR` gets:

- `manifest.json`: the members the rule rewrote, for the IL diff,
- `queue.jsonl`: one JSON line per site left alone (`file`, `line`, `rule`,
  `reason`, `snippet`),
- `sites.jsonl`: one JSON line per rewritten site (`file`, `line`, `member`,
  `before`, `after`),
- `census.txt`: per operator, the sites before, rewritten, queued, left for
  later rules and after. The command stops, writing nothing, unless before =
  rewritten + queued + later and the sites after are exactly those not
  rewritten. `census --operators` prints the number of sites left per operator.

The proof of a rule's commit:

```bash
tools/il-compare.sh HEAD~1 HEAD DIR/manifest.json
```

It builds both commits deterministically and runs `ildiff`: every changed method
must belong to a member in the manifest (lambdas, local functions and state
machines count for their owner), no type, field, property or event may appear or
go unless the manifest allows it, parameter names, attributes, default values
and constants stay as they were, and a manifest member's string constants may
only change if the rule recorded a reason.

## Differential tests

`DiffTest/Cases/<rule>.cases` holds small methods, each written once in its
original form, with every operand going through `Log("name", value)`. The
harness compiles the original and the form the rule produces, runs both over
every combination of bools, numbers, nulls and a fake `UnityObject` whose `==`
treats a destroyed object as null, and compares the results and the logged
order of evaluation. The only allowed difference is a destroyed object under
the rule named `unity-null`, and there the rewritten code must behave exactly
as the original did for null.

## Golden cases

`SelfTest/Golden/*.golden` holds one case per file: an `=== input` section and
what the stages must make of it, in `=== braces` and `=== layout` sections. A
case without a `=== braces` section expects `braces` to leave the input alone.
For every case the self test also checks that a second run changes nothing, the
comment words stay the same, `braces` adds only blocks, `layout` changes only
whitespace, and the input and both outputs compile to the same bytes. It also
feeds the check behind `check --braces` a list of wrong rewrites
(`SelfTest/BracedCodeCases.cs`), and the comment word, same-code and comment
place checks behind `layout` and plain `check` lists of their own
(`SelfTest/LayoutCheckCases.cs`). Each must refuse all of them. Last, in a temp
folder, it makes the writing of a stage's files fail (a read-only file, a
read-only folder, a replace that fails at the start or part of the way through)
and checks what is left behind (`SelfTest/OutputFilesCases.cs`).

To add a case, write the `=== input` section, read the output of
`selftest --show NAME` line by line, and only then save it as the expected text.
