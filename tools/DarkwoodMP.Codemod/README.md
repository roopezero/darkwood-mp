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
| `ildiff <a.dll> <b.dll> [--manifest touched.json] [--list]` | Compares two builds method by method against a rule's manifest. Exits with 1 when anything outside it differs. `--list` first prints each changed method with the manifest entry it was credited to. |
| `difftest <rule>` | Runs the rule's differential cases in `DiffTest/Cases/<rule>.cases`. Exits with 1 if any differs. Stops with exit code 2 if the file holds no case, has a line before its first `=== name` header or a header with no method. |
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
is rewritten without it, and so does one whose compiled rewrite the rule's own
recheck refuses (`Rule.Recheck`; only unity-null has one). A rule never adds a lambda or a local function, because
the game's stack readers would see the extra frame. `DIR` gets:

- `manifest.json`: the members the rule rewrote, for the IL diff, and under
  `temps` every temporary whose name ends in a digit, for stage 9,
- `queue.jsonl`: one JSON line per site left alone (`file`, `line`, `rule`,
  `reason`, `snippet`),
- `sites.jsonl`: one JSON line per rewritten site (`file`, `line`, `member`,
  `before`, `after`, and `comment` when the rewrite took in the comment
  behind the statement: where it put it, then the comment),
- `census.txt`: per operator, the sites before, rewritten, queued, left for
  later rules and after. The command stops, writing nothing, unless before =
  rewritten + queued + later and the sites after are exactly those not
  rewritten. `census --operators` prints the number of sites left per operator,
- `sites.txt`, for a rule with a site list (`Rule.SiteList`, only unity-null):
  every rewrite with the lines of its sites, its member, the old code from the
  start of its line and the new code as the layout pass left it. When the rule
  rewrites the plugin's own sources (no `--src`, no `--dry-run`), the same list
  goes to the path the rule names, for the stage's commit.

`line` is always a line of the tree the rule read, before its rewrite. For the
queue of a stage's commit, run the rule again on that commit with `--dry-run`
(the stage's second run does this): it lists the same queued sites at their
lines in the rewritten files. That is the queue to keep with the stage.

A rule can plan edits that sit inside each other, such as a lambda whose body
it rewrites in a branch of a `?:` it rewrites too (`Rules/NestedEdits.cs`).
Only the outermost edit is proposed, with the texts of the inner ones in place
and all of their sites, so one run does both. A statement's edit may take in
the comment behind it on its line, and then places it: a comment that starts
with the text of a branch and a colon (`// null: ...`) goes behind the
statement that holds that value; any other goes on a line of its own above the
new code, or for an expression body stays on the member's line.

### ternary-direct (stage 3)

A `?:` that is the whole value of its context becomes `if`/`else`
(codemod.md section 7.1), with a `?:` in a branch as an `else if` or an `if`
inside the branch:

- a declaration: `T x; if (c) { x = a; } else { x = b; }`, or
  `T x = k; if (c) { x = a; }` when the last value is a literal and `T` is
  the natural type. `var` is spelled out, and the spelling must bind back to
  the same type. A type of the global namespace gets `global::` when the file
  writes it so (the game's `Button`, `EventTrigger`) or when a public type of
  `System`, `UnityEngine` or a namespace the file imports has its name
  (`Cursor`);
- an assignment, the target written in each branch. The condition then runs
  before the target's parts, so they go through `Purity` (hazard 2). A
  compound assignment reads its target first, so nothing in it may be a call
  (hazard 3), and `x += c ? 1 : -1` becomes `x += 1` and `x -= 1` when `x` is
  a built-in number and the number has the type of its negation. A string
  constant in the target is there once per branch, and the manifest records
  it;
- a return: `if (c) { return a; } return b;`; a `throw` branch becomes a
  throw statement;
- an expression body of a method, accessor, operator or local function (a
  block), of a property or an indexer (a get accessor), or of a lambda (a
  block). A void member or an `Action` whose body assigns gets a block that
  assigns on every path;
- an `if` condition (not an `else if`) or a `foreach` source, through a
  temporary of the natural type in front of the statement, named after what
  both branches have in common (`runs` for `StoryStepRuns()` and `Runs()`).

Each value keeps the natural type: where the natural type converts to the
target's type by more than identity (a tuple's element names don't count), a
branch of another type is cast to it first (hazard 1), and so is one that takes
its type from where it is written (`default`, `new()`, a lambda). A null is
cast too when that conversion is user-defined: `o[k] = c ? s : null` on a
`JObject` runs JToken's conversion from string for the null as well, and
stores a JValue of type String, where `o[k] = null` would store a null token.
Nothing is hoisted out of an argument, an operand or an interpolation hole:
those, the right of `&&`, `||`, `??` and `??=`, and initializers are left for
later rules, and so is every site inside an interpolation hole, such as a
lambda's body there, because its rewrite would put statements in the hole.
The queue gets several declarators, `const`, `using` and `ref` declarations,
anonymous types, ref `?:`, a constant condition, a directive or a comment on
`?` or `:` inside the statement, a branch declaring a variable whose name is
used outside it, a nested `?:` of another type, a target that holds another
operator or a lambda, an expression tree, an assignment that is its body's
value, `else if`, loop and `for` conditions, switch expression arms and field
or property initializers.

### ternary-hoist (stage 4)

A `?:` in an argument, an operand or an interpolation hole moves into a
temporary of its natural type in front of its statement (codemod.md section
7.1): `F(x, c ? a : b);` becomes `T t; if (c) { t = a; } else { t = b; }
F(x, t);`, or `T t = k; if (c) { t = a; }` when the last value is a literal.
A hole keeps its alignment and format, `{(c ? x : y):0.0}` becoming
`{t:0.0}`, and an object argument still gets the double the `?:` made. The
statement can be an expression statement, a declaration (after the
declarators before it), a `return`, `yield return` or `throw`, an `if`
condition, a `foreach` source, a `switch`, `lock` or `using`, in a block,
a switch section or a lambda's block. The temporary is named after the
parameter it is passed to, or after what both branches have in common. A
`?:` that is the whole value of its context is ternary-direct's: the rule
leaves it, so the sites ternary-direct queued aren't queued twice.

`Rules/HoistPath.cs` walks from the `?:` up to its statement and collects
what runs before it there: earlier operands and arguments, the receiver of
a call or the delegate it invokes, an assignment's target parts, earlier
holes and declarators. A parent it doesn't know sends the site to the
queue. `Rules/HoistHazards.cs` then allows the move only when nothing can
notice it (guide section 4.6): the `?:` has no effects and nothing before
it has any, or it has effects and everything before it is a literal,
`this`, a readonly field, a getter that only reads one, or a local or
parameter it doesn't write and no lambda captures. On top of `Purity`, it
counts as a call the `ToString` that making a value into text runs (an
earlier hole, a string `+`, string's `Format`, `Concat` and `Join`), unless
the value is plain text (`Rules/Formatting.cs`: a string, a number, a bool,
a char, an enum, a date or a .NET or Unity value type `KnownPure` lists),
because a handler or a `+` of strings runs it before later values; a
user-defined conversion the compiler applies to an earlier operand; and a
volatile read, which keeps later reads behind it. When the `?:` has
effects, a member read through an object that may be null stays in front
of it too, because it would throw only after those effects. When it has
none, nothing is checked about exceptions: if both the `?:` and something
before it throw, the one that comes out first can change, such as an
`InvalidCastException` in the `?:` before an `IndexOutOfRangeException`
in an earlier operand (guide section 4.6 doesn't cover this either).
Several sites in one statement move in the order they run, and a `?:` that
moved no longer counts as running in the operand that held it.

A `?:` inside a branch of one that moves goes in front of that branch's
statement (its store, or the throw a throw branch becomes), one in the
first condition in front of the whole `if`, with the hazards of that new
statement, so a second run finds nothing more to do. Wire flags,
`(a ? 1 : 0) | (b ? 2 : 0)` with constant whole numbers, become
`int flags = 0; if (a) { flags |= 1; } if (b) { flags |= 2; }` inside the
cast that was around the chain (`Rules/HoistFlags.cs`); another operand is
or-ed in where it was, or starts the flags. The statement keeps the column
it starts at, so the lines that continue it stay where they were.

The queue gets: a call earlier in the statement (hazard 4), whether or not
the `?:` has effects; a mutable value read earlier when the `?:` has effects
(hazard 8) or reads a volatile field; a read that can throw when the `?:`
has effects; a value made into text by code that isn't plain; an earlier
conversion; a volatile read earlier; an initializer, which runs after the
object, collection or array it fills is made and its earlier elements have
run; code that may not run (the right side of `&&`, `||`, `??` and `??=`, a
branch of another `?:`, after `?.`, a lambda body, a switch expression arm,
a `when` clause, an `else if`, loop or `for` header); an expression body,
`checked()`, a
deconstruction, a statement inside an interpolation hole, a `foreach`
source or other header whose `?:` declares a variable, an `await` in the
statement, a `const` declaration, a constant condition, every branch a
throw, an anonymous type, and the checks every rewrite makes (a comment on
`?` or `:`, a directive, a site that doesn't compile). A call earlier is
named by its operand when that is a call, otherwise by the first call,
getter or operator in it, in the order they run, with the type that
declares a user-defined operator, or by the value made into text; every
name is cut to about 60 characters. Every rule's queue names it this way.

A getter declared in the plugin that does nothing but read a field
(`W => _w`, `get { return _w; }`) counts as free of effects for every rule
(`KnownPure.FieldOf`), as an auto-property does, and as stable when the
field is readonly.

### coalesce (stage 5)

A `??` chain or a `??=` whose null check is by reference becomes ifs
(codemod.md section 7.2, `Rules/Coalesce*.cs`). The right side of `??` runs
only when everything before it was null, so it always stays inside the if
that tests for that:

- a declaration whose type is the type of the `??` itself:
  `T x = a; if (x == null) { x = b; }`, one if per value of a chain
  (`var` is spelled out), and a throw last becomes a throw statement;
- `x = a ?? b;` for a local or a parameter x: `x = a; if (x == null) { x =
  b; }`, so x is the temporary, when nothing can see x while the chain
  runs: no lambda or local function captures x, it is no `ref`, no later
  value reads it (nor the first, when that is an opened `?.`), and in a
  `try` only literals and locals follow the first store. When x is the
  first value itself, `if (x == null) { x = b; }`;
- `x = f(x ?? b);` for such an x that nothing else in the statement reads,
  outside any `try`: `if (x == null) { x = b; } x = f(x);`, and
  `x ?? (x = v)` on a local or a parameter is `x ??= v`;
- anywhere else, an assignment, a return, an argument, an operand, a hole, an
  `if` condition or a `foreach` source: the `??` moves into a temporary of
  its natural type in front of its statement, with the hazard checks of
  ternary-hoist (`HoistPath`, `HoistHazards`). An assignment to a field
  becomes `T t = a; if (t == null) { t = b; } _f = t;`, so the field is
  written once, with the final value, and no other thread sees one between;
- a `Nullable<U>` first value whose result is another type:
  `U x; if (a.HasValue) { x = a.Value; } else { x = b; }`, or
  `U x = k; if (a.HasValue) { x = a.Value; }` for a literal `k`, with a
  temporary for `a` unless it is a local or a parameter. A Nullable result
  is filled with `if (!x.HasValue)`;
- a first value `r?.m` of one link, on a receiver checked by reference and
  a member that isn't a Nullable, is opened, and that `?.` site goes with
  the `??`: `T x = null; if (r != null) { x = r.m; } if (x == null) { x = c; }`,
  or `int n = 0; if (r != null) { n = r.Count; }` for `r?.Count ?? 0`.
  A receiver other than a local, a parameter or `this` is read once,
  through a temporary. Any other `?.` stays as written in the first value,
  for stage 6 or 7;
- `x ??= v;`: `if (x == null) { x = v; }`. Used as a value,
  `(x ??= v).Add(i)` becomes the same in front of its statement and then
  `x.Add(i)` for a local or a parameter, and for a field or a property
  `T t = x; if (t == null) { t = v; x = t; }` and then `t.Add(i)`, so it is
  read once and written once as before.

Temporaries are named in the order they are declared (`TempNames.Pick`),
after the parameter the value is passed to when that says what it is (not
`Math.Max`'s `val2` or `ReferenceEquals`'s `objA`), the target it is stored
to, or what it is (`Rules/ValueNames.cs`): a get call after what it gets
(`getObjects()` gives `objects`, `SawField.GetValue(x)` gives `saw`), a
call named for what it does after the rest of its name or the type of its
value (`Get(slot)` of a `Record` gives `record`,
`DeserializeObject<SaveState>(s)` gives `saveState`, as `object` is a
keyword), a trimmed text after the text (`line.Trim()` gives
`trimmedLine`), an element after the singular of its collection, its type
or `line` for a line of a split text (`objects[i]` of a `RandomObject`
gives `randomObject`), and an element that `First`, `Last`, `Single` or
`ElementAt` picks after the `Select` in front of it or its type (`slot`).
Any other call is named after its method (`a.Child()` gives `child`). A
run of capitals in front of a name goes small as a whole (`IPv4Mask` gives
`ipv4Mask`), and a contextual keyword that is one only in front of an
attribute or in a type's declaration stays a name (`method`, `type`,
`record`). Only a name a new local could clash with is
taken: not one after a dot, nor a tuple element's or one in front of a
colon (an argument's, a tuple literal's or a property pattern's). A hint
that another variable or member has is passed over, because a number would
make the temporary read as a copy of it. A number goes on a second version
of another temporary, of the target or of the first value, and on the
first hint when other variables or members have every hint (`slot2`
beside an `int slot`).

The left side's static type decides (codemod.md section 6). A tested value
or a result that is a Unity object is left for stage 7 (`unity-null`), not
queued, and so is a `?.` on a Unity receiver. A type with an `==` of its own
goes to the queue; string's and a delegate's count as reference checks
against null, and a value type is never tested with `==`. Each tested value
must go into the temporary by an identity, a reference or a nullable
conversion, which keep a null a null and run no code; otherwise a single
`??` with a Nullable first value is unwrapped, and anything else is queued.

The queue gets: what ternary-hoist's hazards refuse (a call earlier in the
statement, a mutable value read earlier when the `??` has effects, code
that may not run, an expression body, an initializer, an `await` in the
statement), a `??` or `??=` inside another one or holding one, with one
reason when the inner one is in a right side and another when it is in the
first value, which runs every time, a variable declared inside
the `??`, a directive or a comment inside it, `dynamic`, a type with an
`==` of its own, a left side that converts by more than a reference
conversion, a chain whose first values would have to be unwrapped, a
`??=` target whose parts would run twice (an element, a member of anything
but `this`, a type, a local or a parameter), and a `??=` of a Nullable used
as a value, whose value is the unwrapped one.

### conditional-access (stage 6)

A `?.` chain whose tested values are not Unity objects becomes ifs
(codemod.md section 7.3, `Rules/ConditionalAccessRule.cs` and
`Rules/Access*.cs`). Every value a `?.` tests is read once: a receiver
that isn't a local, a parameter or `this` goes into a temporary named
after it, and so does a local or a parameter that is a ref or that a
lambda or a local function captures, which another thread can write
between two reads; an event or a delegate goes into one named `handler`,
which is then called as a delegate (`handler(a)` for `?.Invoke(a)`), and
each inner `?.` of a chain tests a temporary of the step in front of it:

- a statement: `x?.F(a);` becomes `if (x != null) { x.F(a); }`, with an if
  per `?.` of a chain, so the arguments run only for a receiver that isn't
  null;
- a declaration: `T n = null; if (x != null) { n = x.F; }`, `var` spelled
  out with the natural type (`int?` for `x?.Count`), when the value goes
  into `T` by an identity or a reference conversion, for a declaration of
  one variable that isn't a `using` one (others take the hoist below, so
  a second initializer still runs and the resource is still disposed);
- an assignment of one `?.` to a target without an element, a constant or
  a call, whose parts may run after the receiver (hazard 2): `if (x !=
  null) { t = x.F; } else { t = null; }`, so a field or a property is
  written once, with the final value, when the value goes into the target
  by an identity or a reference conversion (a user-defined one would no
  longer run for a null `x`);
- a return: `if (x != null) { return x.F; } return null;`, when the value
  is returned by an identity or a reference conversion;
- an expression body: a block first, holding the statement form for a void
  member or lambda and the return form for any other, a get accessor for a
  property or an indexer;
- a comparison on one `?.`: a fixed translation in place, in parentheses
  where the code around it would bind it another way (see below);
- anywhere else, and wherever the forms above don't fit, the chain moves
  into a temporary of its natural type: `T t = null; if (x != null) { t =
  x.F; }` in front of its statement, or in front of the value of an
  expression body that becomes a block (a lambda's too), with the hazard
  checks of ternary-hoist (`HoistPath`, `HoistHazards`).

The comparisons keep their place, so they work in a loop condition, a
`when` clause or the right side of `&&`, where no statement can go:

```text
x?.F == v, v not null    ->  x != null && x.F == v
x?.F != v, v not null    ->  x == null || x.F != v
x?.F > v (<, >=, <=)     ->  x != null && x.F > v
x?.F == null             ->  x == null || x.F == null
x?.F != null             ->  x != null && x.F != null
x?.B == true             ->  x != null && x.B        (!= true: x == null || !x.B)
x?.B == false            ->  x != null && !x.B       (!= false: x == null || x.B)
x?.B ?? false            ->  x != null && x.B
x?.B ?? true             ->  x == null || x.B
```

They hold only when `v` has no effects and can't throw: a constant,
`typeof`, a local, a parameter or `this`; when no user-defined conversion
runs on either side; and when the operator is built in, lifted, or the
`==` of string, .NET or Unity, which `UnityEngine.Object` declares (an
`==` that a class deriving from it declares of its own counts as any other
user-defined one). The original runs a conversion for
a null `x` too: Unity's `bool` operator makes `x?.Go == false` true for a
null `x`. A user-defined operator that isn't lifted gets the null itself:
with an `==(int? a, Meter b)` of Meter's, `x?.Count == m` is what that
operator says for a null `x`, not false, and so is `x?.M == null` with an
`==(Meter? a, Meter? b)`. Such comparisons move into a temporary instead.
The same check sends to the hoist a member whose type's own `==` the
comparison calls (a reference type's operator is never lifted), unless
that `==` is .NET's or the one `UnityEngine.Object` declares. Where the
reference `==` is
called instead, as for `h?.It == this` with an `It` of a type that
derives from this one's, both forms compare references and the
translation holds.
"Not null" means a value type that isn't a Nullable, a constant that
isn't null, `typeof`, or a `this` that isn't a Unity object: Unity's `==`
counts a destroyed `this` as null, so `h?.Go == this` is true for a null
`h` and a destroyed `this`, and it moves into a temporary. `== null` on a
member of a value type that isn't a Nullable and on a chain go to the
hoist too. A receiver that isn't a local goes into a temporary in front
of the statement first. These edits replace an expression, so
`RulePlan.ProposeExpression` checks their texts as expressions.

A chain that tests a Unity object anywhere is left whole for stage 7
(`unity-null`), not queued; a Unity member in a comparison keeps Unity's
`==`. A receiver whose type has an `==` of its own goes to the queue
unless that `==` is .NET's (`Type`, `MethodBase`, `FieldInfo`), which
treats only null as equal to null, and so do a Nullable receiver, a type
parameter and `dynamic`. An interface and `object` stay reference checks,
as `?.` makes them.

The comment behind a rewritten statement goes on a line of its own above
the new code, at the statement's indentation, and the new code's first line
starts there too, so the formatter doesn't line the comment up under a
comment behind the line above.

The queue gets what ternary-hoist's hazards refuse (a call earlier in the
statement, a mutable value read earlier when the `?.` has effects, code
that may not run, an initializer, a for initializer, whose variable lives
in the header, a value made into text by code that isn't plain), a `?.`
inside the receiver or the later part of another one and the one that
holds it (both, so a second run finds the same), a variable declared
inside it, a directive or a comment inside it, a statement form inside an
interpolation hole, an `await` in the statement, an async member's or
lambda's expression body and the checks every rewrite makes.

The proof of a rule's commit:

```bash
tools/il-compare.sh HEAD~1 HEAD DIR/manifest.json [--list]
```

It builds both commits deterministically and runs `ildiff`: every changed method
must belong to a member in the manifest (lambdas, local functions and state
machines count for the overload whose code leads to them, or, when none does,
only when every overload of the name is listed), no member may gain a lambda,
local function or state machine, listed or not, because the game's stack
readers would see the extra frame (the manifest can allow one by name:
`method <full name>`, or `type <full name>` for a state machine; a new closure
class adds no frame and counts for its owner), no type, field, property or event may appear or
go unless the manifest allows it, the fields of a struct keep their order, parameter names, attributes, default values
and constants stay as they were, and a manifest member's string constants may
only change if the rule recorded a reason for that member. Each overload is a
member of its own: a record for one overload covers only that overload, a
record for a name every overload of it. Two conversion operators of a type can
differ only in what they return, so their keys carry the return type as well:
`N.T::op_Implicit(N.T):System.Int32`.

### unity-null (stage 7)

Every `?.`, `?[`, `??` and `??=` that tests a Unity object, a value whose
static type derives from `UnityEngine.Object`, becomes the ifs of stages 5
and 6 (guide section 4.7, codemod.md section 6, `Rules/UnityNullRule.cs`).
The checks are `x != null` and `x == null` on a value of the Unity type, so
they call Unity's `==`, which counts a destroyed object as null; the rule
never writes `is null`, `ReferenceEquals` or `(object)x`. This is the one
stage that changes what the code does: code that ran on a destroyed object is
now skipped, `a ?? b` gives `b` for a destroyed `a`, and `x ??= v` replaces a
destroyed `x`.

The rule runs the coalesce planner and then the access planner over each
file, both with Unity's checks (`NullCheck.Unity`), so the forms, names and
hazard checks of stages 5 and 6 hold here too:

- a `??` chain or a `??=` whose tested values or whose type are Unity objects
  gets the forms of stage 5. A first value `r?.m` is opened with its `??`,
  with Unity's check on a Unity receiver and the reference check on any
  other;
- a `?.` chain that tests a Unity object anywhere, the receiver or a value
  an inner `?.` tests, gets the forms of stage 6 with an if per `?.`:
  Unity's check on each Unity value and the reference check on any other, so
  a plain receiver with a Unity member is tested by reference and the member
  with Unity's `==`. The fixed comparisons keep their place
  (`go != null && go.count > k`), and `go?.active ?? false` goes with its
  `?.`, since that `??` tests no Unity object.

A site that tests no Unity object isn't the rule's: a `?.` on an interface or
on `object`, a `??` of strings, the sites stages 5 and 6 queued. Those are
left for the queue's stage, not queued again. Besides what stages 5 and 6
refuse (a call earlier in the statement, code that may not run, an
initializer, an `await` in the statement, ...), the queue gets:

- a class deriving from `UnityEngine.Object` that declares an `==` of its
  own, which `x != null` would call, and a Unity object of a type parameter;
- a `??` whose type isn't a Unity type, as in `object o = go ?? (object)"x"`:
  every value is tested in a temporary of that type, which would test the
  Unity object by reference;
- a `?.` and a `??` or `??=` that test Unity objects, one inside the other.
  The rewrite of the outer one would move the inner one into a statement,
  where a second run would rewrite it, so both are queued, as two `?.` or two
  `??` one inside the other are. The `?.` that a `??` opens is the
  exception, and when that `??` goes to the queue, its `?.` goes after it.

So one run is a fixed point: a second run rewrites nothing and lists the same
queue. `Recheck` then counts, in each compiled rewrite, the null checks that
call the `==` of `UnityEngine.Object`: they must grow by the number of Unity
objects its sites test, and the others by no more than the number of other
values they test (`x != null && x.B` tests `x` for both sites of
`x?.B ?? false`). A rewrite that tests a Unity object through a variable of
another type, or with `is null`, is refused, and its sites go to the queue
with the counts as the reason. The site list goes to
`tools/DarkwoodMP.Codemod/sites/unity-null.txt`.

The check after the stage (guide section 4.7) is Microsoft's detector, in a
temporary copy of the tree only: add `Microsoft.Unity.Analyzers` 1.28.0 to
the plugin's project, raise UNT0007, UNT0008, UNT0023 and UNT0029 to warning
in `.editorconfig`, build, and every report left must be at a site in the
queue. Never commit the package or the severities.

## Differential tests

`DiffTest/Cases/<rule>.cases` holds small methods, each written once in its
original form, with every operand going through `Log("name", value)`. The
harness compiles the original and the form the rule produces, runs both over
every combination of bools, integers of every size with their type's limits
(a wrong type in an intermediate result, `n + 1` against `n + 1L`, shows only
at `int.MaxValue`, and so does a lost `checked`), every value of an enum and
one it doesn't name, other numbers (with NaN and the infinities for `double`
and `float`), nulls and a fake `UnityObject` whose `==` treats a destroyed
object as null. The fake object comes alive and destroyed, each alone and with
an alive `Next`, and every object has a `Count` of its own, so a wrong object
in a result, the log or a `?.` chain shows. It compares the results (an
exception by its type and message), the logged order of evaluation and what
each run left in its object arguments and in the static fields of the case and
of the types written inside it. A collection, returned or logged, is compared
element by element, a struct or class written in the case field by field
(nested ones too, up to six levels deep), and an alive `UnityObject` by its
`Count` and `Next`; a destroyed one shows only as `destroyed`. A returned
iterator or coroutine is run, for at most 1,000 values, and a returned task is
waited for (a `ValueTask` too), so their bodies run in the row they belong
to. A `Run` declared `async void` stops the command: the part after its first
await would run during later rows, so it must return `Task`. So does a `Run`
that returns a delegate, whose body would never run (call it inside `Run`),
and a value logged, returned or left behind that the harness can't show, so
that a difference in it would go unseen: it shows numbers, bools, chars,
strings, enums, collections, tuples, the fake `UnityObject`, structs and
classes written in the case and types with a `ToString` of their own, apart
from tasks. The only
allowed difference is a destroyed object under the rule named `unity-null`, and
there the rewritten code must behave exactly as the original did for null and
leave the destroyed object as it was. The log may name the destroyed object
where the original logged null, but with one destroyed input the result must
match to the letter, so a rewrite that lets the object through is caught. A
parameter the code passes on without checking it, as `b` in `a ?? b`, is
marked `[PassesThrough]`: a destroyed `b` must then come back as it did
before, and isn't replaced by null in the original's run. Without the mark
the right rewrite of `a ?? b` fails.

Under every rule but unity-null, the fake `UnityObject` doesn't derive from
`UnityEngine.Object`, so the rules see it as a type with an `==` of its own.
Which sites the coalesce rule counts as Unity objects, and leaves for stage
7, is checked only by the golden case `coalesce/04-types`. Under unity-null
the harness compiles the fixture with the symbol `UNITY_NULL`: the fake then
derives from a stub `UnityEngine.Object` that declares the `==`, `!=` and
`bool` operators, as Unity's does, so the rule sees a Unity object and its
`x != null` calls that `==`. There the fake also has `Take(int)`, which logs
and adds to its `Count`, a bool `Big` that logs its read, and a delegate
property `Heard`. A case must hold a site the rule
rewrites, so a case about a site that must stay as written has one of each.
The coalesce cases guard the refusals of code that may not run (the right
of `&&` or `||`, a `?:` branch, the part after `?.`, a lambda body), the
type a `var` is spelled with, the refusal of a value that converts by code
of its own, and these checks of the forms that write into a local or a
parameter x:

- `x = a ?? b`: a plain `=` to a local or a parameter; the form of `x = x
  ?? b` only when the first value is x; no lambda captures x and x is no
  `ref`; neither a later value nor an opened `?.` first value reads x; and
  in a `try` the first value is no opened `?.` and only literals and
  locals follow it;
- `x = f(x ?? b)`: a plain `=` to the variable the `??` tests, which
  nothing else in the statement reads, no lambda captures and is no `ref`,
  outside any `try`;
- `x ?? (x = v)`: the store is to x itself, and no value follows it.

A hand-made break of any one of these fails one of the cases. A `??` on
the right of another one is queued as nested before the check of code that
may not run sees it, so for this rule that refusal is checked only by the
golden case `ternary-hoist/06-may-not-run`.

The conditional-access cases run every form over null and non-null
receivers: arguments that run only for a receiver that isn't null, a
receiver property that returns null on its second read (a rewrite that
reads it twice throws), events and delegates, the natural type of a
declaration, a declaration of two variables and a using declaration,
chains, a setter that logs each set, returns, expression bodies and
lambdas, every fixed comparison and its parentheses, a compared value with
effects or a null (which must still run and compare), a compared value
that is a property, a loop condition, element reads and .NET's `==`, a
user-defined conversion of the `?.`'s value, of the compared value or into
an assignment's target, an operator of the compared value's own that
takes a Nullable and one that a member compared with null calls, a
compared `this` under the reference `==`, receivers that are another
type's property or a call, a value read or a call earlier in the
statement, an assignment of a chain to a target that wasn't null, an
assignment whose target runs a getter and one whose receiver changes a
part of its target. Reading the receiver twice (a call's or another
type's property's too), writing `x?.F != v` as `x != null && x.F != v`,
translating a compared value with effects (a property too) or a
comparison that converts by code of its own or whose operator gets the
null itself (with `== null` too), moving a `?.` with effects past an
earlier read of a value it changes or one without effects past an earlier
call, the assignment form for a chain or for a target that takes the
value by a user-defined conversion, running the receiver before the
target's getter or a part of the target it changes, and the declaration
form for a declaration of two variables or a using declaration each fail
a case. The golden cases pin what a single thread can't show, and the
texts: a ref parameter or ref local, a local or a parameter that a lambda
or a local function captures and a field of this, also written
`this._box`, go into a temporary; a statement in an interpolation hole,
with an await or in an else-if condition goes to the queue; a compared
field or property moves into a temporary, and so do a compared `this` of
a Unity object and a member whose Unity type declares an `==` of its own;
and the queue names the call in an earlier operand.

The unity-null cases run the forms of both planners over null, alive and
destroyed objects: statements whose arguments run only for an alive
receiver, a receiver that is a property or a call and is read once, a
delegate after a Unity receiver, declarations of the natural type, chains,
a plain receiver with a Unity member, assignments that write the target
once, returns, bodies and lambdas, arguments and holes, every fixed
comparison, a loop condition, a compared value with effects, `??` in a
declaration, a chain, a field, a return, an argument and a local, the `?.`
a `??` opens, `??=` alone and as a value, and `x?.Count != 5`. A case
returns what its sites make rather than the objects it tests, since the
harness compares what each run left in those; a site that must stay as
written has a receiver the case makes itself, which is never destroyed,
because such a site sees a destroyed object as alive. Emitting `is not null`
with the recheck switched off fails the destroyed rows of the cases. The
golden cases pin the texts and the queue: a Unity class with an `==` of its
own, a `??` of `object`, a type parameter, the nested sites and the
refusals of stages 5 and 6.

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

An operator rule's cases are in `SelfTest/Rules/<rule>/*.golden`: an
`=== input` section in the layout stage's form, the file the rule must make of
it in `=== output`, and the sites it must leave in the queue in `=== queue`,
one `line: reason` each. For every case the self test also checks that a
second run of the rule changes nothing and that the IL diff of the input and
the output passes with the rule's own manifest.

To add a case, write the `=== input` section, read the output of
`selftest --show NAME` line by line (`--show RULE/NAME` for a rule's case), and
only then save it as the expected text.
