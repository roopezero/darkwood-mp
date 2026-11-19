using DarkwoodMP.Codemod.DiffTest;
using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Rules;

namespace DarkwoodMP.Codemod.SelfTest;

/// Proves the differential harness: the probe rule passes, and each wrong
/// rewrite of it is caught.
static class DiffHarnessCases
{
    const string Unity = """
        public static int? Run(UnityObject u)
        {
            return Log("u", u)?.Hit();
        }
        """;

    /// Case files that would test nothing. Reading each must stop.
    static readonly (string What, string Text)[] Unreadable =
    {
        ("an empty file", ""),
        ("a blank file", "\n  \n"),
        ("a mistyped header", "== name\npublic static int Run()\n{\n"
            + "    return 1;\n}\n"),
        ("a header with no method before the next",
            "=== a\n\n=== b\npublic static int Run()\n{\n"
            + "    return 1;\n}\n"),
        ("a header with no method at the end",
            "=== a\npublic static int Run()\n{\n    return 1;\n}\n=== b\n"),
    };

    /// Wrong rewrites that only some inputs show: (what, case, rewrite).
    static readonly (string What, string Method, string Wrong)[] Wrong =
    {
        ("an inverted double comparison", """
            public static int Run(double d)
            {
                return Log("d", d) > 0 ? Log("a", 1) : Log("b", 2);
            }
            """,
            "if (Log(\"d\", d) <= 0)\n{\nreturn Log(\"b\", 2);\n}\n"
                + "return Log(\"a\", 1);"),
        ("an inverted float comparison", """
            public static int Run(float f)
            {
                return Log("f", f) < 1 ? Log("a", 1) : Log("b", 2);
            }
            """,
            "if (Log(\"f\", f) >= 1)\n{\nreturn Log(\"b\", 2);\n}\n"
                + "return Log(\"a\", 1);"),
        // Both differ only at int.MaxValue.
        ("a value of the wrong type in an intermediate result", """
            public static long Run(bool c, int n)
            {
                return Log("c", c) ? Log("n", n) + 1 : 0L;
            }
            """,
            "if (Log(\"c\", c))\n{\nreturn Log(\"n\", n) + 1L;\n}\n"
                + "return 0L;"),
        ("a dropped checked context", """
            public static int Run(bool c, int n)
            {
                return checked(Log("c", c) ? Log("n", n) * 2 : 0);
            }
            """,
            "if (Log(\"c\", c))\n{\nreturn unchecked(Log(\"n\", n) * 2);\n}\n"
                + "return 0;"),
    };

    /// The parameter types the wire code is full of, which the harness must
    /// give inputs to: (what, case, right rewrite, rows).
    static readonly (string What, string Method, string Right, int Rows)[] Typed =
    {
        ("a byte and an enum", """
            public enum Kind { A, B }

            public static int Run(byte b, Kind k)
            {
                return Log("k", k) == Kind.A ? Log("b", b) : 0;
            }
            """,
            "if (Log(\"k\", k) == Kind.A)\n{\nreturn Log(\"b\", b);\n}\n"
                + "return 0;", 9),
        ("a short and an unsigned long", """
            public static long Run(short s, ulong u)
            {
                return Log("s", s) > 0 ? (long)Log("u", u) : s;
            }
            """,
            "if (Log(\"s\", s) > 0)\n{\nreturn (long)Log(\"u\", u);\n}\n"
                + "return s;", 15),
    };

    /// Keeps the operands and drops the store: "int dropped; if (c) ...".
    const string DropsStore = "int dropped;\nif (Log(\"c\", c))\n{\n"
        + "dropped = Log(\"a\", 1);\n}\nelse\n{\ndropped = Log(\"b\", 2);\n}";

    /// Effects that aren't a logged operand: (what, case, rewrite).
    static readonly (string What, string Method, string Wrong)[] Unlogged =
    {
        ("a dropped store to an argument's field", """
            public static int Run(bool c, UnityObject u)
            {
                if (u == null)
                {
                    return 0;
                }
                u.Count = Log("c", c) ? Log("a", 1) : Log("b", 2);
                return 0;
            }
            """, DropsStore),
        ("a dropped store to a static field", """
            static int _n;

            public static void Reset()
            {
                _n = 0;
            }

            public static int Run(bool c)
            {
                _n = Log("c", c) ? Log("a", 1) : Log("b", 2);
                return 0;
            }
            """, DropsStore),
        ("different contents of a returned array", """
            public static int[] Run(bool c)
            {
                return new[] { Log("c", c) ? Log("a", 1) : Log("b", 2) };
            }
            """, "int v;\nif (Log(\"c\", c))\n{\nv = Log(\"a\", 1);\n}\n"
                + "else\n{\nv = Log(\"b\", 2);\n}\nreturn new[] { v + 1 };"),
    };

    /// Bodies that run only when the harness enumerates or waits for what
    /// Run returns: (what, case, rewrite).
    static readonly (string What, string Method, string Wrong)[] Deferred =
    {
        ("a changed iterator", """
            public static IEnumerable<int> Run(bool c)
            {
                yield return Log("c", c) ? Log("a", 1) : Log("b", 2);
            }
            """, "yield return 7;"),
        ("a changed coroutine", """
            public static System.Collections.IEnumerator Run(bool c)
            {
                yield return Log("c", c) ? Log("a", 1) : Log("b", 2);
            }
            """, "yield return 7;"),
        ("a changed coroutine that never ends", """
            public static System.Collections.IEnumerator Run(bool c)
            {
                while (true)
                {
                    yield return Log("c", c) ? Log("a", 1) : Log("b", 2);
                }
            }
            """, "yield return 7;"),
        ("a changed async method", """
            public static async System.Threading.Tasks.Task<int> Run(bool c)
            {
                await System.Threading.Tasks.Task.Delay(100);
                return Log("c", c) ? Log("a", 1) : Log("b", 2);
            }
            """, "return 7;"),
    };

    /// An async void Run returns at its first await, and the rest of its body
    /// runs on the thread pool during later rows. Whether a wrong rewrite
    /// after the await shows would depend on timing, so the harness must stop.
    const string AsyncVoid = """
        public static async void Run(bool c)
        {
            await System.Threading.Tasks.Task.Yield();
            Log("x", Log("c", c) ? Log("a", 1) : Log("b", 2));
        }
        """;

    const string AsyncVoidSwapped = "int t;\nif (Log(\"c\", c))\n{\n"
        + "t = Log(\"b\", 2);\n}\nelse\n{\nt = Log(\"a\", 1);\n}\nLog(\"x\", t);";

    /// The right rewrite of the endless coroutine: both runs stop at the same
    /// step and must compare equal.
    const string EndlessRight = "if (Log(\"c\", c))\n{\n"
        + "yield return Log(\"a\", 1);\n}\nelse\n{\n"
        + "yield return Log(\"b\", 2);\n}";

    /// "u ?? null" with the reference check kept: it returns the destroyed
    /// object where the original returned null for a null u.
    const string Coalesce = """
        public static UnityObject Run(UnityObject u)
        {
            return Log("u", u) ?? null;
        }
        """;

    const string KeepsReferenceCheck = "var t = Log(\"u\", u);\n"
        + "if ((object)t != null)\n{\nreturn t;\n}\nreturn null;";

    /// "a ?? b" passes b on unchecked, so a destroyed b must come back.
    const string Fallback = """
        public static UnityObject Run(
            UnityObject a, [PassesThrough] UnityObject b)
        {
            return Log("a", a) ?? Log("b", b);
        }
        """;

    const string FallbackRight = "var t = Log(\"a\", a);\n"
        + "if (t != null)\n{\nreturn t;\n}\nreturn Log(\"b\", b);";

    /// Checks b as well, so a destroyed b becomes null.
    const string FallbackChecked = "var t = Log(\"a\", a);\n"
        + "if (t != null)\n{\nreturn t;\n}\nvar s = Log(\"b\", b);\n"
        + "if (s != null)\n{\nreturn s;\n}\nreturn null;";

    public const int Count = 9 + 5 + 4 + 3 + 4 + 1 + 1 + 2 + 1 + 2;

    public static List<string> Failures()
    {
        var problems = new List<string>();
        var folder = Path.Combine(
            RepoPaths.Find().Root, "tools/DarkwoodMP.Codemod/DiffTest/Cases");
        var cases = DiffCase.Read(Path.Combine(folder, "probe-return.cases"));
        foreach (var diffCase in cases)
        {
            var outcome = DiffHarness.Run(
                new ProbeReturnRule(ProbeForm.Correct), diffCase);
            if (!outcome.Passed)
            {
                problems.Add($"  correct rule, {diffCase.Name}: "
                    + string.Join("; ", outcome.Mismatches.Take(3)));
            }
        }
        var literal = cases[0];
        ExpectCaught(problems, "evaluates an operand twice", literal,
            new ProbeReturnRule(ProbeForm.EvaluatesTwice));
        ExpectCaught(problems, "changes the order", literal,
            new ProbeReturnRule(ProbeForm.ChangesOrder));
        ExpectCaught(problems, "drops a side effect", literal,
            new ProbeReturnRule(ProbeForm.DropsEffect));

        var unity = new DiffCase { Name = "unity", Method = Unity };
        var allowed = DiffHarness.Run(new ProbeUnityRule("unity-null"), unity);
        if (!allowed.Passed || allowed.DestroyedRows != 1)
        {
            problems.Add("  unity-null: the destroyed row should be asserted "
                + "and pass: " + string.Join("; ", allowed.Mismatches));
        }
        ExpectCaught(problems, "a destroyed object under any other rule", unity,
            new ProbeUnityRule("probe-unity"));
        ExpectCaught(problems, "a reference check under unity-null", unity,
            new ProbeUnityRule("unity-null", checksReference: true));
        foreach (var (what, method, wrong) in Wrong.Concat(Unlogged).Concat(Deferred))
        {
            var diffCase = new DiffCase { Name = what, Method = method };
            ExpectCaught(problems, what, diffCase,
                new ProbeTextRule("probe-text", wrong));
        }
        var endless = new DiffCase
        {
            Name = "endless coroutine",
            Method = Deferred[2].Method,
        };
        var right = DiffHarness.Run(
            new ProbeTextRule("probe-text", EndlessRight), endless);
        if (!right.Passed)
        {
            problems.Add("  the right rewrite of an endless coroutine: "
                + string.Join("; ", right.Mismatches.Take(3)));
        }
        ExpectCaught(problems, "a destroyed object returned under unity-null",
            new DiffCase { Name = "coalesce", Method = Coalesce },
            new ProbeTextRule("unity-null", KeepsReferenceCheck));
        var fallback = new DiffCase { Name = "fallback", Method = Fallback };
        var kept = DiffHarness.Run(
            new ProbeTextRule("unity-null", FallbackRight), fallback);
        if (!kept.Passed)
        {
            problems.Add("  a destroyed object passed through under unity-null: "
                + string.Join("; ", kept.Mismatches.Take(3)));
        }
        ExpectCaught(problems, "a passed-through object checked under unity-null",
            fallback, new ProbeTextRule("unity-null", FallbackChecked));
        ExpectStop(problems, "an async void Run",
            new DiffCase { Name = "async void", Method = AsyncVoid },
            new ProbeTextRule("probe-text", AsyncVoidSwapped));
        foreach (var (what, text) in Unreadable)
        {
            ExpectUnreadable(problems, what, text);
        }
        foreach (var (what, method, rewrite, rows) in Typed)
        {
            var diffCase = new DiffCase { Name = what, Method = method };
            ExpectPasses(problems, what, diffCase,
                new ProbeTextRule("probe-text", rewrite), rows);
        }
        return problems;
    }

    static void ExpectPasses(
        List<string> problems, string what, DiffCase diffCase, Rule rule,
        int rows)
    {
        try
        {
            var outcome = DiffHarness.Run(rule, diffCase);
            if (!outcome.Passed || outcome.Rows != rows)
            {
                problems.Add($"  {what}: {outcome.Rows} rows, not {rows}: "
                    + string.Join("; ", outcome.Mismatches.Take(3)));
            }
        }
        catch (Stop stop)
        {
            problems.Add($"  {what}: stopped: {stop.Message}");
        }
    }

    static void ExpectStop(
        List<string> problems, string what, DiffCase diffCase, Rule rule)
    {
        try
        {
            var outcome = DiffHarness.Run(rule, diffCase);
            problems.Add($"  {what}: compared, not refused, with "
                + $"{outcome.Mismatches.Count} mismatches");
        }
        catch (Stop)
        {
            // The case was refused, as it should be.
        }
    }

    static void ExpectUnreadable(
        List<string> problems, string what, string text)
    {
        var folder = Directory.CreateTempSubdirectory("dwmp-codemod-").FullName;
        try
        {
            var file = Path.Combine(folder, "rule.cases");
            File.WriteAllText(file, text);
            var cases = DiffCase.Read(file);
            problems.Add($"  {what}: read as {cases.Count} cases, not refused");
        }
        catch (Stop)
        {
            // The file was refused, as it should be.
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    static void ExpectCaught(
        List<string> problems, string what, DiffCase diffCase, Rule rule)
    {
        var outcome = DiffHarness.Run(rule, diffCase);
        if (!outcome.Rewritten)
        {
            problems.Add($"  {what}: the rule did not rewrite the case");
        }
        else if (outcome.Mismatches.Count == 0)
        {
            problems.Add($"  {what}: the harness did not catch it");
        }
    }
}
