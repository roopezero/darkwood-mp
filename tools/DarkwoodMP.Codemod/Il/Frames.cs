using System.Text.RegularExpressions;
using Mono.Cecil;

namespace DarkwoodMP.Codemod.Il;

/// The code of a build that runs in a stack frame of its own although no
/// method of that name is written in source: lambdas "<Foo>b__3_0", local
/// functions "<Foo>g__Bar|3_0" and state machines "<Foo>d__12", whose
/// MoveNext is the frame. The game's stack readers count frames
/// (`new StackTrace(2, false)`), so a rewrite must not add one, even in a
/// listed member. A closure class adds no frame: a new block scope can split
/// one without adding any code.
///
/// Frames are counted per owner and kind, because a frame can change its
/// name or place without being new: a local function that starts capturing
/// takes a closure argument, and a lambda moves to the closure class of the
/// scope that now holds what it captures.
static class Frames
{
    static readonly Regex Lambda = new Regex(@"^<[^<>]*>b__");
    static readonly Regex Local = new Regex(@"^<[^<>]*>g__(?<name>[^|]+)\|");
    static readonly Regex Machine = new Regex(@"^<[^<>]*>d__\d+$");
    static readonly Regex LocalMachine = new Regex(
        @"^<<[^<>]*>g__(?<name>[^|]+)\|\d+_\d+>d$");

    /// What each frame of the module is, by "owner | kind": the allowance it
    /// would need ("method <full name>" or "type <full name>") and the name
    /// it is reported by.
    public static Dictionary<string, List<Frame>> Of(
        ModuleDefinition module, Ownership owners, GeneratedNames names)
    {
        var frames = new Dictionary<string, List<Frame>>();
        foreach (var t in IlTools.AllTypes(module))
        {
            foreach (var m in t.Methods)
            {
                var kind = KindOf(m);
                if (kind == null)
                {
                    continue;
                }
                var owner = Owner(owners.ExactOwnersOf(m), owners.OwnersOf(m));
                Add(frames, owner + " | " + kind, new Frame(
                    "method " + m.FullName,
                    names.MemberName(m.Name, m.DeclaringType),
                    m.FullName));
            }
            var machine = MachineKind(t);
            if (machine != null)
            {
                var owner = Owner(owners.ExactOwnersOf(t), owners.OwnersOf(t));
                Add(frames, owner + " | " + machine, new Frame(
                    "type " + t.FullName, names.Of(t), t.FullName));
            }
        }
        return frames;
    }

    static string KindOf(MethodDefinition m)
    {
        if (Lambda.IsMatch(m.Name))
        {
            return "lambda";
        }
        var local = Local.Match(m.Name);
        if (local.Success)
        {
            return "local function " + local.Groups["name"].Value;
        }
        return null;
    }

    static string MachineKind(TypeDefinition t)
    {
        if (Machine.IsMatch(t.Name))
        {
            return "state machine";
        }
        var local = LocalMachine.Match(t.Name);
        if (local.Success)
        {
            return "state machine of local function "
                + local.Groups["name"].Value;
        }
        return null;
    }

    /// The source methods traced to the frame, or when none leads to it, the
    /// names it is made for.
    static string Owner(
        IReadOnlyCollection<string> exact, IReadOnlyCollection<string> named)
    {
        if (exact.Count > 0)
        {
            return string.Join(", ", exact.OrderBy(o => o, StringComparer.Ordinal));
        }
        return string.Join(", ", named.OrderBy(o => o, StringComparer.Ordinal));
    }

    static void Add(Dictionary<string, List<Frame>> frames, string key, Frame f)
    {
        if (!frames.TryGetValue(key, out var list))
        {
            list = new List<Frame>();
            frames[key] = list;
        }
        list.Add(f);
    }
}

/// One frame: the manifest entry that allows it, its name with the member
/// ordinal left out (as IlDiff compares names) and its full name.
sealed record Frame(string Allowance, string Name, string FullName);
