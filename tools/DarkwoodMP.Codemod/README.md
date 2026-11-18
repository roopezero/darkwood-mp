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
| `layout` | Stage 2. Puts the braces of one-line blocks, switch statements, type bodies and namespaces on their own lines, then runs the Roslyn `Formatter` over every file. |
| `check [--braces] <dirA> <dirB>` | Compares two copies of the sources, file by file: the same comment words in the same order, and the same code once whitespace is ignored. Stage 1 adds tokens, so check it with `--braces`: the second copy must be the first with only the new blocks added, and every comment between the same two tokens of code. Exits with 1 on any difference. |
| `selftest [--show NAME]` | Runs the golden cases below. Exits with 1 if any fails. `--show` prints what the stages make of one case. |

`Il/` (the IL diff) and `Prototype/` (the width pass and the first operator
rule) are the prototype's code for the later stages of the pass. They compile,
but no command runs them yet.

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
- after `layout`, every file holds the same code apart from whitespace.

`selftest` compiles its own snippets instead of the plugin. It needs neither the
game nor the core DLL.

## Proving a stage

The check inside the tool is only a first filter. The proof that counts for a
whitespace stage is a real build of the commit before and the commit after:

```bash
tools/verify-identical.sh HEAD~1 HEAD
```

Exit code 0 means every DLL is identical to the byte.

## Golden cases

`SelfTest/Golden/*.golden` holds one case per file: an `=== input` section and
what the stages must make of it, in `=== braces` and `=== layout` sections. A
case without a `=== braces` section expects `braces` to leave the input alone.
For every case the self test also checks that a second run changes nothing, the
comment words stay the same, `braces` adds only blocks, `layout` changes only
whitespace, and the input and both outputs compile to the same bytes. It also
feeds the check behind `check --braces` a list of wrong rewrites
(`SelfTest/BracedCodeCases.cs`), which it must refuse.

To add a case, write the `=== input` section, read the output of
`selftest --show NAME` line by line, and only then save it as the expected text.
