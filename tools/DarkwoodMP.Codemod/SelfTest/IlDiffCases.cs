using DarkwoodMP.Codemod.Il;

namespace DarkwoodMP.Codemod.SelfTest;

/// Each case plants one difference between two tiny assemblies and says what
/// the IL diff must make of it. The empty string means it must pass.
static class IlDiffCases
{
    const string Base = """
        using System;
        using System.Collections.Generic;
        namespace N
        {
            public class C
            {
                public const int Limit = 5;
                public int Field;
                public int Prop { get; set; }
                public event Action Fired;
                [Obsolete("old")] public void Attr() { Fired(); }
                public int Foo(int x) { return x + 1; }
                public int Foo(string s) { return s.Length; }
                public string Say(int x) { return "hello" + x; }
                public string Two(int x) { return "first" + x + "second"; }
                public string Pick(bool c) { return c ? "up" : "down"; }
                public string Pick(int c) { return c > 0 ? "yes" : "no"; }
                public int Def(int x = 1) { return x; }
                public int Lam(int x) { Func<int, int> f = y => y + x; return f(1); }
                public IEnumerable<int> Iter(int x) { yield return x; }
                public int Loc(int x) { int Inner(int y) { return y + 1; } return Inner(x); }
                public int Over(int x) { Func<int, int> f = y => y - x; return f(1); }
                public int Over(string s) { Func<int, int> f = y => y - s.Length; return f(1); }
                public int OverS(int x) { Func<int, int> f = y => y * 2; return f(x); }
                public int OverS(string s) { Func<int, int> f = y => y * 3; return f(s.Length); }
                public IEnumerable<int> IterOver(int x) { yield return x * 2; }
                public IEnumerable<int> IterOver(string s) { yield return s.Length; }
                public int LocOver(int x) { int In(int y) { return y + 4; } return In(x); }
                public int LocOver(string s) { int In(int y) { return y + 5; } return In(s.Length); }
            }
            public struct P { public int A; public int B; }
            public class Ops
            {
                public static implicit operator int(Ops c) { return 41; }
                public static implicit operator long(Ops c) { return 42; }
            }
            public class Q { public int X1; public int X2; }
            public class Gen
            {
                int _n;
                public const int Version = 29;
                public const int Step = 5;
                [NonSerialized] public int Mark;
                public int Next(int x) { return x + Step; }
                public Func<int> Cap(int x) { return () => _n + Version; }
                public int Twice(int x) { int L(int y) { return y * 9; } return L(x); }
            }
        }
        """;

    const string Foo = "N.C::Foo(System.Int32)";
    const string ToInt = "N.Ops::op_Implicit(N.Ops):System.Int32";
    const string FooBody = "public int Foo(int x) { return x + 1; }";
    /// The lambda that "a lambda added to a listed member" adds to Foo.
    const string FooLambda =
        "System.Int32 N.C/<>c__DisplayClass10_0::<Foo>b__0()";
    const string Say = "N.C::Say(System.Int32)";
    const string Two = "N.C::Two(System.Int32)";
    const string Lam = "N.C::Lam(System.Int32)";
    const string Iter = "N.C::Iter(System.Int32)";
    const string Loc = "N.C::Loc(System.Int32)";
    const string Def = "N.C::Def(System.Int32)";
    const string Over = "N.C::Over(System.Int32)";
    const string OverS = "N.C::OverS(System.Int32)";
    const string IterOver = "N.C::IterOver(System.Int32)";
    const string LocOver = "N.C::LocOver(System.Int32)";
    // Gen holds Cap's lambda, which captures only this, and Twice's local
    // function: both are methods of Gen itself, not of a generated type.
    const string Next = "N.Gen::Next(System.Int32)";
    const string Cap = "N.Gen::Cap(System.Int32)";
    const string Twice = "N.Gen::Twice(System.Int32)";
    const string Mark = "public int Mark;";
    const string Hello = "\"hello\"";
    const string Hullo = "\"hullo\"";
    const string PickBool = "N.C::Pick(System.Boolean)";
    const string PickInt = "N.C::Pick(System.Int32)";

    /// The strings of both overloads of Pick, as the base has them and with
    /// the two overloads' strings swapped.
    const string Picks = "\"up\" : \"down\"; }\n"
        + "        public string Pick(int c) { return c > 0 ? \"yes\" : \"no\"; }";
    const string PicksSwapped = "\"yes\" : \"no\"; }\n"
        + "        public string Pick(int c) { return c > 0 ? \"up\" : \"down\"; }";

    static readonly (string Name, string From, string To, string[] Members,
        string[] Allowed, StringChange[] Strings, string Fails)[] Cases =
    {
        ("identical builds pass", "", "", None, None, null, ""),
        ("a body inside the manifest passes", "x + 1", "x + 2",
            new[] { Foo }, None, null, ""),
        ("a body outside the manifest fails", "x + 1", "x + 2",
            None, None, null, "changed outside the manifest"),
        ("another overload fails", "{ return s.Length; }",
            "{ return s.Length + 1; }",
            new[] { Foo }, None, null, "changed outside the manifest"),
        ("a name without types covers every overload", "{ return s.Length; }",
            "{ return s.Length + 1; }",
            new[] { "N.C::Foo" }, None, null, ""),
        ("a lambda counts for its owner", "y + x", "y + x + 1",
            new[] { Lam }, None, null, ""),
        ("a lambda outside the manifest fails", "y + x", "y + x + 1",
            None, None, null, "changed outside the manifest"),
        ("MoveNext counts for its owner", "yield return x;", "yield return x + 1;",
            new[] { Iter }, None, null, ""),
        ("MoveNext outside the manifest fails", "yield return x;",
            "yield return x + 1;", None, None, null,
            "changed outside the manifest"),
        ("a new state machine field counts for its owner", "yield return x;",
            "int t = x; yield return t; yield return t;",
            new[] { Iter }, None, null, ""),
        ("a new state machine field outside the manifest fails", "yield return x;",
            "int t = x; yield return t; yield return t;",
            None, None, null, "field added"),
        ("a local function counts for its owner", "y + 1; } return Inner",
            "y + 2; } return Inner", new[] { Loc }, None, null, ""),
        ("a local function outside the manifest fails", "y + 1; } return Inner",
            "y + 2; } return Inner", None, None, null,
            "changed outside the manifest"),
        ("a new closure class counts for its owner", "return y + 1; }",
            "return y + x; }", new[] { Loc }, None, null, ""),
        ("a new closure class outside the manifest fails", "return y + 1; }",
            "return y + x; }", None, None, null, "type added"),
        // A lambda, local function or state machine is a stack frame of its
        // own, which the game's stack readers would count.
        ("a lambda added to a listed member fails", FooBody,
            "public int Foo(int x) { Func<int> g = () => x; return g() + 1; }",
            new[] { Foo }, None, null, "a lambda or local function was added"),
        ("a local function added to a listed member fails", FooBody,
            "public int Foo(int x) { int G() { return x; } return G() + 1; }",
            new[] { Foo }, None, null, "a lambda or local function was added"),
        ("a lambda without a capture added to a listed member fails", FooBody,
            "public int Foo(int x) { Func<int, int> g = y => y + 1; "
                + "return g(x); }",
            new[] { Foo }, None, null, "a lambda or local function was added"),
        ("a state machine added to a listed member fails", FooBody,
            "public int Foo(int x) { IEnumerable<int> L() { yield return 1; } "
                + "foreach (var i in L()) { x += i; } return x + 1; }",
            new[] { Foo }, None, null, "a state machine was added"),
        ("a lambda the manifest allows by name passes", FooBody,
            "public int Foo(int x) { Func<int> g = () => x; return g() + 1; }",
            new[] { Foo }, new[] { "method " + FooLambda }, null, ""),
        ("a second lambda in a listed member fails", "y => y + x; return f(1)",
            "y => y + x; Func<int> g = () => x; return f(g())",
            new[] { Lam }, None, null, "a lambda or local function was added"),
        ("a lambda of the listed overload passes", "y - x", "y - x - 1",
            new[] { Over }, None, null, ""),
        ("a lambda of another overload fails", "y - s.Length", "y - s.Length - 1",
            new[] { Over }, None, null, "changed outside the manifest"),
        ("a cached lambda of the listed overload passes", "y * 2", "y * 5",
            new[] { OverS }, None, null, ""),
        ("a cached lambda of another overload fails", "y * 3", "y * 4",
            new[] { OverS }, None, null, "changed outside the manifest"),
        ("MoveNext of the listed overload passes", "x * 2;", "x * 3;",
            new[] { IterOver }, None, null, ""),
        ("MoveNext of another overload fails", "yield return s.Length;",
            "yield return s.Length + 1;", new[] { IterOver }, None, null,
            "changed outside the manifest"),
        ("a local function of the listed overload passes", "y + 4", "y + 6",
            new[] { LocOver }, None, null, ""),
        ("a local function of another overload fails", "y + 5", "y + 7",
            new[] { LocOver }, None, null, "changed outside the manifest"),
        // Two conversion operators of a type differ only in what they return,
        // so each is a member of its own.
        ("a listed conversion operator passes", "return 41;", "return 43;",
            new[] { ToInt }, None, null, ""),
        ("a conversion operator to another type fails", "return 42;",
            "return 44;", new[] { ToInt }, None, null,
            "changed outside the manifest"),
        ("reordered fields of a struct fail", "public int A; public int B;",
            "public int B; public int A;", None, None, null, "field order changed"),
        ("reordered fields of a class pass", "public int X1; public int X2;",
            "public int X2; public int X1;", None, None, null, ""),
        ("an added field fails", "public int Field;",
            "public int Field; public int Extra;", None, None, null, "field added"),
        ("a listed field passes", "public int Field;",
            "public int Field; public int Extra;", None,
            new[] { "field N.C::Extra" }, null, ""),
        ("an added type fails", "public class C", "public class D { } public class C",
            None, None, null, "type added"),
        ("a listed type passes", "public class C",
            "public class D { } public class C", None,
            new[] { "type N.D" }, null, ""),
        ("an added property fails", "public int Field;",
            "public int Field; public int Other { get { return 1; } }",
            None, None, null, "property added"),
        ("an added event fails", "public int Field;",
            "public int Field; public event Action Gone;",
            None, None, null, "event added"),
        ("an added method fails", "public int Field;",
            "public int Field; public void New() { }",
            None, None, null, "method added"),
        ("a listed method passes", "public int Field;",
            "public int Field; public void New() { }", None,
            new[] { "method System.Void N.C::New()" }, null, ""),
        ("a renamed parameter fails", "Def(int x = 1) { return x; }",
            "Def(int y = 1) { return y; }", new[] { Def }, None, null,
            "parameter names"),
        ("a removed attribute fails", "[Obsolete(\"old\")] ", "", None, None, null,
            "method facts changed"),
        ("a changed attribute argument fails", "\"old\"", "\"new\"", None, None,
            null, "method facts changed"),
        ("a changed default value fails", "int x = 1", "int x = 2", new[] { Def },
            None, null, "method facts changed"),
        ("a changed constant fails", "Limit = 5", "Limit = 6", None, None, null,
            "field facts changed"),
        // A type written in source holds the lambdas and local functions of
        // its listed members, but its fields are still its own.
        ("a changed constant fails though its user and the type's lambda and "
            + "local function are listed", "Step = 5", "Step = 6",
            new[] { Next, Cap, Twice }, None, null, "field facts changed"),
        ("a changed constant fails though the lambda it is inlined in is "
            + "listed", "Version = 29", "Version = 30",
            new[] { Cap, Twice }, None, null, "field facts changed"),
        ("a removed field attribute fails though the type's lambda and local "
            + "function are listed", "[NonSerialized] " + Mark, Mark,
            new[] { Cap, Twice }, None, null, "field facts changed"),
        ("a field made readonly fails though the type's lambda and local "
            + "function are listed", Mark, "public readonly int Mark;",
            new[] { Cap, Twice }, None, null, "field facts changed"),
        ("a field made static fails though the type's lambda and local "
            + "function are listed", Mark, "public static int Mark;",
            new[] { Cap, Twice }, None, null, "field facts changed"),
        ("a changed field type fails though the type's lambda and local "
            + "function are listed", Mark, "public long Mark;",
            new[] { Cap, Twice }, None, null, "field facts changed"),
        ("changed accessibility fails", "public int Foo(int x)",
            "internal int Foo(int x)", new[] { Foo }, None, null,
            "method facts changed"),
        ("a changed string fails", Hello, Hullo, new[] { Say }, None, null,
            "string constants changed"),
        ("a string change with a recorded reason passes", Hello, Hullo,
            new[] { Say }, None, new[] { Recorded(Say, "hello", "hullo", "typo") },
            ""),
        ("a recorded change without a reason fails", Hello, Hullo, new[] { Say },
            None, new[] { Recorded(Say, "hello", "hullo", " ") },
            "string constants changed"),
        ("a recorded change of other strings fails", Hello, Hullo, new[] { Say },
            None, new[] { Recorded(Say, "hello", "other", "typo") },
            "string constants changed"),
        ("two recorded changes in one member pass", "\"first\" + x + \"second\"",
            "\"furst\" + x + \"secund\"", new[] { Two }, None,
            new[]
            {
                Recorded(Two, "first", "furst", "typo"),
                Recorded(Two, "second", "secund", "typo"),
            },
            ""),
        ("two recorded changes, one without a reason, fail",
            "\"first\" + x + \"second\"", "\"furst\" + x + \"secund\"",
            new[] { Two }, None,
            new[]
            {
                Recorded(Two, "first", "furst", "typo"),
                Recorded(Two, "second", "secund", ""),
            },
            "string constants changed"),
        // Each overload is a member of its own: the strings of the name as a
        // whole stay the same, but each overload's change.
        ("strings swapped between two listed overloads fail", Picks,
            PicksSwapped, new[] { PickBool, PickInt }, None, null,
            "string constants changed in " + PickBool),
        ("a record for one overload doesn't cover another", "\"up\"", "\"upp\"",
            new[] { PickBool, PickInt }, None,
            new[] { Recorded(PickInt, "up", "upp", "typo") },
            "string constants changed in " + PickBool),
        ("a record for the name covers each overload", "\"up\"", "\"upp\"",
            new[] { PickBool, PickInt }, None,
            new[] { Recorded("N.C::Pick", "up", "upp", "typo") }, ""),
    };

    /// What --list prints for a changed method: the manifest entry it was
    /// credited to.
    static readonly (string Name, string From, string To, string[] Members,
        string Credit)[] Credits =
    {
        ("a body is credited to its own entry", "x + 1", "x + 2",
            new[] { Foo }, "N.C::Foo(System.Int32)  for  " + Foo),
        ("a body is credited to a name for every overload",
            "{ return s.Length; }", "{ return s.Length + 1; }",
            new[] { "N.C::Foo" }, "  for  N.C::Foo (every overload)"),
        ("a lambda is credited to its owner", "y + x", "y + x + 1",
            new[] { Lam }, "<Lam>b__0(System.Int32)  for  " + Lam),
    };

    static string[] None => Array.Empty<string>();

    public static int Count => Cases.Length + Credits.Length;

    static StringChange Recorded(
        string member, string removed, string added, string reason)
    {
        return new StringChange
        {
            Member = member,
            Reason = reason,
            Removed = new List<string> { removed },
            Added = new List<string> { added },
        };
    }

    public static List<string> Failures()
    {
        var problems = new List<string>();
        foreach (var test in Cases)
        {
            var changed = Base;
            if (test.From.Length > 0)
            {
                changed = Base.Replace(test.From, test.To);
                if (changed == Base)
                {
                    problems.Add($"  {test.Name}: the planted text is not in the base");
                    continue;
                }
            }
            var a = SnippetCompiler.Emit(Base, out var errorsA);
            var b = SnippetCompiler.Emit(changed, out var errorsB);
            if (a == null || b == null)
            {
                problems.Add($"  {test.Name}: {errorsA}{errorsB}");
                continue;
            }
            var manifest = new Manifest
            {
                Members = test.Members.ToList(),
                Allowed = test.Allowed.ToList(),
            };
            if (test.Strings != null)
            {
                manifest.StringChanges = test.Strings.ToList();
            }
            var report = IlDiff.Compare(a, b, manifest);
            if (test.Fails.Length == 0 && !report.Passed)
            {
                problems.Add($"  {test.Name}: should pass, but: "
                    + string.Join("; ", report.Problems));
            }
            if (test.Fails.Length > 0
                && !report.Problems.Any(p => p.Contains(test.Fails)))
            {
                problems.Add($"  {test.Name}: should report '{test.Fails}', but: "
                    + string.Join("; ", report.Problems));
            }
        }
        foreach (var test in Credits)
        {
            var a = SnippetCompiler.Emit(Base, out var errorsA);
            var b = SnippetCompiler.Emit(
                Base.Replace(test.From, test.To), out var errorsB);
            if (a == null || b == null)
            {
                problems.Add($"  {test.Name}: {errorsA}{errorsB}");
                continue;
            }
            var manifest = new Manifest { Members = test.Members.ToList() };
            var listed = new StringWriter();
            IlDiff.Compare(a, b, manifest).PrintCredits(listed);
            if (!listed.ToString().Contains(test.Credit))
            {
                problems.Add($"  {test.Name}: should list '{test.Credit}', "
                    + "but listed: " + listed.ToString().Trim());
            }
        }
        return problems;
    }
}
