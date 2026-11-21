# How the code is organised

The conventions DarkwoodMP's code follows: how it's split into projects,
where logic and state live, how Harmony patches, names and formatting look,
and what the unit tests cover. [TECHNICAL.md](TECHNICAL.md) describes the
multiplayer design itself (authority, prediction, the protocol);
[DEVELOPMENT.md](DEVELOPMENT.md) covers building and running the tests.

**Status:** this is the target layout. The code is moving to it one subsystem
at a time. New code follows it from the start; existing code is moved when
it's next changed or in a planned refactor pass. Until a subsystem has moved,
its old form (static classes, logic inside patches) still applies to it. The
core, analyzer and unit test projects exist. The core has the rules and the
network code (`Session`, the packets, `TcpBackend` and the server browser);
the plugin keeps the Steam backend. The map test steps are in
`src/DarkwoodMP.MapTests`, a second plugin. `ModHost` holds the `Session`,
its backends and the server browser; `CoopSession`, `Coop.Current` and
`WorldScope` exist, and `MoveCheck` is the first subsystem to have moved
into `CoopSession`.
[MIGRATION_PLAN.md](MIGRATION_PLAN.md) has the order and the progress.

## The idea: a core of rules, and a thin shell around the game

Most of what the mod does is a decision ("may this client open this door?",
"did this player move too fast?", "who does this enemy go for?") wrapped in
code that talks to the game (reading a `Door`, sending a message, moving the
player). Today the two are mixed, so a decision can only be checked by
starting Darkwood and having a scripted peer trigger it, which takes minutes.

The code is split in two:

- **The core** holds the decisions. It takes plain data and returns a result.
  It never touches Unity, the game, Harmony, BepInEx or Steam, so it runs in a
  unit test in milliseconds.
- **The shell** is everything that touches the game: Harmony patches,
  `MonoBehaviour`s, UI, the Steam backend. It reads the game, asks the core,
  and does what the core decided.

The in-game tests stay for what only the game can show (patches, physics,
culling, scripted events); the unit tests take over the decisions and the
network flow.

## Projects

| Project | Target | References | What's in it |
|---|---|---|---|
| `src/DarkwoodMP.Core` | netstandard2.0 | nothing from Unity or the game | rules, their facts and verdicts, the session and protocol (`Session`, `Packets`, `TcpBackend`, `PortMapper`, `ServerInfo`, `ServerBrowser`, `PlayerKey`), small math types |
| `src/DarkwoodMP` | net472 | Core, Unity, the game, BepInEx, Harmony, Steamworks | the plugin: patches, shell classes, `CoopSession`, UI, `SteamBackend` / `Lobby` / `SteamTransport` |
| `src/DarkwoodMP.MapTests` | net472 | the plugin | the in-game map test steps (`MapScenario*`); deployed to the test game folders only, never in the players' zips |
| `src/DarkwoodMP.Analyzers` | netstandard2.0 | Roslyn | the build checks for the formatting rules below |
| `tests/DarkwoodMP.Core.Tests` | net8.0 | Core only | unit tests (xUnit, Shouldly) |
| `tests/DarkwoodMP.Analyzers.Tests` | net8.0 | the analyzer, Roslyn | unit tests of the formatting rules, each run on small pieces of code (xUnit, Shouldly) |

The core can't name a Unity or game type: it doesn't reference them, so that's
a compile error, not a rule to remember. `PortMapper`, `ServerInfo`,
`ServerBrowser` and `PlayerKey` go into the core with the network code because
`TcpBackend` and the server browser need them, and they touch Unity only
through the log, which comes in as `ILog`. The plugin ships
`DarkwoodMP.Core.dll` next to `DarkwoodMP.dll` in its plugin folder, where
BepInEx finds it.

### The startup check

The two DLLs must come from the same build. `CoreCheck` runs first in
`Plugin.Awake`, before anything loads the core: if `DarkwoodMP.Core.dll` is
missing or has another version than the plugin, it logs why and the mod turns
itself off, so the game still plays. The first call into the core is guarded
the same way, for a core that passes the check and still can't be used. For
that to work, `Plugin` has no field of a core type and `Awake` names none: code
that needs one is in a method of its own with
`[MethodImpl(MethodImplOptions.NoInlining)]`. Otherwise Mono fails to load
`Plugin`, or to compile `Awake`, before the check can run.

### The map tests

`src/DarkwoodMP.MapTests` is a second BepInEx plugin. It declares the main
plugin as a dependency, so BepInEx loads it second, and it reaches the
plugin's internal classes through `InternalsVisibleTo`. It does nothing unless
the game was started with `--dwmp-test-control`. The main plugin never names a
map step: the steps reach it through a small `ITestSteps` interface, which
the map tests plugin puts in `Plugin.TestSteps` at its `Awake` (null in a
player's game). It applies the steps' Harmony patches itself, so the main
plugin's "All N patches applied" counts only the mod's own. The DLL
is deployed to the test game folders only, and `pack.sh` fails if it ends up
in a zip.

The Steamworks rule from `CLAUDE.md` holds as before: only `SteamBackend`,
`Lobby` and `SteamTransport` (in the plugin) name Steamworks types. The core
can't, for the same reason it can't name Unity's.

### Math types

The core has its own small `Vec2` and `Vec3` structs instead of Unity's
`Vector2` and `Vector3`, and a `Seconds` struct for times. The wire format
carries 3D positions, so `Packets` uses `Vec3`; a rule that works on the
top-down plane (reach, speed) uses `Vec2`. The game's ground plane is x-z, not
x-y, so `Vec2` has the fields `X` and `Z`, and `Vec3.Ground` gives that pair.
The shell converts at the edge: `pos.ToVec2()`, `pos.ToVec3()`, `v.ToUnity()`.

## Rules: facts in, a verdict out

A decision is a static method on a `...Rules` class in the core. It takes a
`...Facts` struct (what the decision looks at) and returns a `Verdict`.

```csharp
// Core: World/Doors/DoorFacts.cs

/// What the host knows about a door when a client asks to change it.
public readonly struct DoorFacts
{
    public bool Exists { get; init; }
    public bool Broken { get; init; }
    public bool Locked { get; init; }
    public bool Blocked { get; init; }

    /// From the requesting player, on the ground plane.
    public float Distance { get; init; }
}
```

```csharp
// Core: World/Doors/DoorRules.cs

public static class DoorRules
{
    public const float Reach = 600f;
    public const float ShotReach = 1200f;

    public static Verdict MayOpen(DoorFacts door)
    {
        if (!door.Exists)
        {
            return Verdict.Refuse(Refusal.NoSuchDoor);
        }
        if (door.Distance > Reach)
        {
            return Verdict.Refuse(Refusal.OutOfReach);
        }
        if (door.Broken)
        {
            return Verdict.Refuse(Refusal.DoorBroken);
        }
        if (door.Locked)
        {
            return Verdict.Refuse(Refusal.DoorLocked);
        }
        if (door.Blocked)
        {
            return Verdict.Refuse(Refusal.DoorBlocked);
        }
        return Verdict.Allow;
    }
}
```

- **Facts are plain data.** The shell fills them in; a test writes them in one
  line. The struct also documents exactly what the rule looks at.
- **A rule with up to three inputs may take them as plain arguments** instead
  of a facts struct.
- **A verdict carries a named reason** (`Refusal.DoorLocked`), not text. The
  text for each reason is in one table in the core (`Refusals.Text`), which
  the shell logs and sends. Tests compare reasons, so rewording a message
  doesn't break them, and every refusal the host can give is in one list.
- **A refusal can carry a detail**, for the texts that name a value:
  `Verdict.Refuse(Refusal.NoKey, detail)`. `Refusals.Text` puts the detail
  into the sentence (`nobody has the key (...)`,
  `a hit of 1500 is more than any weapon does`). A rule builds the detail only
  when it refuses, since formatting a number allocates, and tests still
  compare the named reason.
- **Limits are named constants** in the rules class (`Reach`, `ShotReach`,
  `MaxSpeed`), never a bare number in a condition.
- **Where one decision leads to several things happening** (refuse, send the
  state to roll back to, relay to the others), the rule returns a list of
  effects instead of a single verdict, and the shell carries them out in
  order. Use this only where it's needed; a single verdict is the default.
- **Rules are stateless.** Where a decision needs memory (the movement check's
  half-second measurement per player), the core class holding it is an
  instance, never static.

## The shell

The shell's job is to turn the game into facts and a verdict back into the
game.

```csharp
// Plugin: World/Doors/Doors.cs

var door = ObjRef.Resolve<Door>(obj);
var verdict = DoorRules.MayOpen(FactsOf(door, requester));
if (verdict.Refused)
{
    _authority.Refuse(verdict.Reason);
    return false;
}
```

- **One `FactsOf(...)` per game type.** It's the only code that reads that game
  object for a rule.
- The shell has no rules of its own: an `if` that decides something belongs
  in the core.

## State: three lifetimes

State lives in one of three homes, by how long it has to last:

| Home | Lives as long as | Holds |
|---|---|---|
| `ModHost`, owned by `Plugin` | the process | settings, windows, the `Session` object and its backends: what the main menu needs when no session is running |
| `CoopSession` | one session | who is connected, requests waiting for an answer, and the subsystems |
| `WorldScope`, owned by `CoopSession` | one loaded world | what's tied to the loaded scene |

- **The world can change while a session goes on.** A chapter change loads a
  new world, so `CoopSession` replaces its `WorldScope` at each world load.
  Hosting from the pause menu does the opposite: the session starts in a world
  that's already loaded.
- **An inner lifetime may hold a reference to an outer one, never the
  reverse.** Session-level code reads the current `WorldScope` each time and
  doesn't keep it in a field.
- **Anything gathered before hosting starts** is either process state or is
  rebuilt by scanning the world when the session starts.
- **Process state is on `ModHost`, not on `Plugin`,** because `Plugin` has to
  stay loadable when the core is missing (see "The startup check").

Every subsystem is an instance owned by `CoopSession`, created when a session
starts and dropped when it ends.

```csharp
sealed class CoopSession : IDisposable
{
    public readonly Authority Authority;
    public readonly Doors Doors;
    public readonly MoveCheck Moves;

    public CoopSession(Session net)
    {
        Authority = new Authority(net);
        Doors = new Doors(net, Authority);
        Moves = new MoveCheck(net);
    }

    public void Dispose()
    {
        // Unhook game and network events.
    }
}
```

- The next session starts from new objects, so nothing has to be reset by
  hand. A static that someone forgot to clear is how things from the last game
  used to leak into the next one.
- **No new statics.** The one exception is `Coop.Current`, the running session
  (`null` when there's none), which Harmony patches (always static) use to
  reach it.
- A subsystem that moves into `CoopSession` drops its `OnSessionEnded`
  cleanup.

## Harmony patches

A patch only hands its typed arguments to one shell method. It has no logic
and no state of its own.

```csharp
// Plugin: World/Doors/DoorPatches.cs

[HarmonyPatch(typeof(Door), nameof(Door.open))]
static class DoorOpen
{
    static void Prefix(Door __instance, out DoorState __state)
    {
        __state = Doors.Before(__instance);
    }

    static void Postfix(
        Door __instance, Vector3 openerPosition, DoorState __state)
    {
        Doors.Opened(__instance, openerPosition, __state);
    }
}
```

- The shared work (checking there's a session, counting nested calls so only
  the outermost is sent, capturing the state before, sending) is written once
  in the shell, not copied into each patch. Whether anything changed and
  should be sent is decided in the core.
- A subsystem's patches are together in `<Subsystem>Patches.cs`.
- A patch class is named after the game method it patches (`DoorOpen`), with
  no `Patch` suffix.
- One patch per game method, with that method's own typed arguments; not one
  patch over a list of methods.
- The `CLAUDE.md` Harmony rules still hold: argument types for overloaded
  methods, private methods and coroutine bodies by name.

## Files and folders

Folders are by feature, and the same path is used in both projects:

```
src/DarkwoodMP.Core/World/Doors/DoorRules.cs
src/DarkwoodMP.Core/World/Doors/DoorFacts.cs
src/DarkwoodMP/World/Doors/Doors.cs
src/DarkwoodMP/World/Doors/DoorPatches.cs
tests/DarkwoodMP.Core.Tests/World/Doors/DoorRulesTests.cs
```

- **About 600 lines per file at most**, and one main type per file. A file
  that grows past that is split along what it does.
- The in-game test steps are in `src/DarkwoodMP.MapTests`, not in the plugin.

## Naming

- **Short, real words** for variables and parameters: `pos`, `door`, `damage`,
  `from`, `reach`. No compound names like `measureStartPositionSeconds`.
- **Single letters only in short scopes**: a loop index, a short lambda.
- **Let the type carry the meaning and the unit** (`Vec2 accepted`,
  `Seconds since`), and the class carry whose it is (inside `PlayerMovement`,
  a field is `Accepted`, not `AcceptedPlayerPosition`).
- **Name things for what they mean in the game, and say which version.** A
  player's position can be real (in their own game), reported (what they
  sent) or accepted (what the host let it be): `Reported` and `Accepted`,
  never `Good` or a bare `Position` that could be any of them.
- **At most three parameters.** More become a facts struct.
- **No `bool` parameter that switches behaviour.** Split it into two methods:
  `SendReliable(...)` and `SendUnreliable(...)`, not
  `Send(..., reliable: true)`.
- **If a call needs named arguments to be readable, change the method** (the
  two rules above), not the call. Calls into the game's own methods are the
  exception.
- **A class or field whose role isn't obvious gets a one-line `///` comment**
  saying what it is.
- **Casing as usual in C#:** `PascalCase` for types and public members (fields
  too), `_camelCase` for private fields, `camelCase` for locals and
  parameters. The game's own names follow Unity's style (`door.destroyed`,
  `locked.keyType`) and stay as they are.

An example with these rules:

```csharp
/// What the host remembers about one client's movement, measured over
/// half a second.
sealed class PlayerMovement
{
    /// Where the host let them be.
    public Vec2 Accepted;

    /// Where the current measurement began, and when.
    public Vec2 MeasureFrom;
    public Seconds MeasureAt;

    public Seconds PutBackAt;
}
```

## Formatting

- **Every `if`, `else`, `for`, `foreach`, `while`, `do`, `using` and `lock`
  has braces**, even around a single statement.
- **No one-line blocks.** The braces go on their own lines, so
  `if (x) { return; }` is written over four lines.
- **Braces on their own line** (the usual C# style), not at the end of the
  line as in Go.
- **A blank line after a block.** A statement, member or accessor that holds
  a `{ }` pair spread over more than one line, anywhere in it (a body, an
  accessor list, an initializer, a lambda's body, a `switch`), is followed by
  a blank line, unless the next token is a `}` or the file ends:
  ```csharp
  AsPlayer(() =>
  {
      door.Open();
  });

  Plugin.Log.LogInfo("opened");
  ```
  - So `});`, `};` and a `})` with `.ToList();` on the next line end
    statements that need one, a `case`'s `{ }` block needs one before the
    next label, and a multi-line `get` before its `set`. A `}` in the middle
    of a statement that goes on, as in `}, "Discard", () =>`, isn't checked.
    A `{ }` on one line (`{ get; set; }`, `new Door { Open = true }`)
    doesn't count, and neither do the braces of an interpolation in
    `$"..."`.
  - An `if`/`else` chain, a `try` and a `do`/`while` are one statement each,
    so the blank line goes after the last `}` (or after `while (...);`),
    never before `else`, `catch`, `finally` or `while`.
  - A statement is one in a block or a `case`, local functions included,
    inside lambdas and accessors too. A member is one in a type's or a
    namespace's body, and an accessor is a `get`, `set`, `init`, `add` or
    `remove`. A file's own types and namespaces are members of the global
    namespace, so they count, and so do top-level statements.
  - A comment or a directive line (`#region`, `#endif`, `#pragma`) isn't a
    blank line: the blank line goes before it. A statement that starts on
    the line where the one before ends, as in `} Run();`, is left to IDE0055
    and DW0002.
- **`=>` is allowed**, for lambdas (`list.RemoveAll(x => x.Until < now)`)
  and for expression-bodied members
  (`public bool Refused => Reason != Refusal.None;`). It isn't a block, so
  the one-line rule doesn't apply to it.
- **At most 80 columns per line**, comments included. A long call or
  signature breaks after `(` with the arguments indented one level, as in
  the `Postfix` above.
- **No ternary `?:`.** Write an `if` / `else`.
- **No `??`, `??=` or `?.`.** Check for `null` with an `if`. Besides being
  shorthand, on Unity objects these are wrong: Unity reports a destroyed
  object as `== null`, but `??` and `?.` skip that check and use the
  destroyed object anyway.

The build enforces these. `.editorconfig` makes a missing brace and a brace
at the end of a line errors (with `EnforceCodeStyleInBuild`), and
`src/DarkwoodMP.Analyzers` fails the build on a one-line block, a missing
blank line after a block, a line over 80 columns, `?:`, `??`, `??=` and `?.`
(rules DW0001 to DW0007). They are errors in every project, the plugin
included: one formatting pass has been over all of its code. `tests/DarkwoodMP.Analyzers.Tests` runs each rule on small pieces of
code, with a case that fires and one that stays quiet.

## Unit tests

`tests/DarkwoodMP.Core.Tests` uses xUnit and Shouldly. Tests are grouped by
the class and then the method they test: one `<Class>Tests` class, with a
nested class per method, and each test named as a short sentence that starts
with a verb:

```csharp
public class DoorRulesTests
{
    public class MayOpen
    {
        [Fact]
        public void Refuses_a_locked_door()
        {
            var door = new DoorFacts { Exists = true, Locked = true };

            var verdict = DoorRules.MayOpen(door);

            verdict.Reason.ShouldBe(Refusal.DoorLocked);
        }

        [Fact]
        public void Refuses_a_door_out_of_reach()
        {
            var door = new DoorFacts { Exists = true, Distance = 700 };

            var verdict = DoorRules.MayOpen(door);

            verdict.Reason.ShouldBe(Refusal.OutOfReach);
        }
    }
}
```

The runner then lists the rules one by one, so `DoorRulesTests+MayOpen`
reads as every reason a door can be refused:

```
DoorRulesTests+MayOpen.Refuses_a_locked_door
DoorRulesTests+MayOpen.Refuses_a_door_out_of_reach
DoorRulesTests+MayHit.Refuses_more_damage_than_any_weapon
MoveRulesTests+Check.Puts_back_a_player_faster_than_running
SessionTests+Hello.Refuses_an_old_protocol
```

- **One test class per class under test**, in the mirrored folder
  (`tests/DarkwoodMP.Core.Tests/World/Doors/DoorRulesTests.cs`), with one
  nested class per method it tests.
- **The analyzer's tests** (`tests/DarkwoodMP.Analyzers.Tests`) follow the
  same rules, except that `FormattingAnalyzerTests` has one nested class per
  rule (`FormattingAnalyzerTests+BlankLine`): each test runs the whole
  analyzer on a piece of code and checks one rule's findings.
- **A test's name starts with a verb** (`Refuses_...`, `Allows_...`,
  `Puts_back_...`) and says the behaviour, not the inputs:
  `Refuses_a_locked_door`, not `Locked_true_returns_false`.
- **Underscores between words in test names only.** Everything else uses the
  usual casing.
- **One behaviour per test.** A name that needs "and" is two tests.
- Each test has three parts, separated by a blank line: the facts, the call,
  the check.

What's tested:

- **Every `...Rules` class.** A bug fixed in a rule comes with a test that
  fails before the fix.
- **The network in one process.** `Session`, `Packets` and `TcpBackend` are in
  the core (Unity's clock and log come in through `IClock` and `ILog`), and a
  `LoopbackBackend` (in the test project) connects a host and clients in
  memory, and delivers each message on the receiver's next poll, so a test
  decides the order and moves a fake clock. Tests cover the handshake and
  its refusals, relaying (to the others, not back to the sender), the request
  and verdict cycle, and each message's bytes. Tests over real TCP on
  localhost cover the framing and the info query.
- The test project's folders follow the core's.

What isn't: patches, the shell's `FactsOf`, physics, culling and scripted
events. Those need the game, and the in-game tests (scripted Python peers and
the map test) cover them. `tools/run-test.sh` runs the core's unit tests
first and stops if any fail, before it starts the game.
