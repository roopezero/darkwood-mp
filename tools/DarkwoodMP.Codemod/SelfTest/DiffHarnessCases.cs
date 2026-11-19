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
    };

    public const int Count = 9 + 5 + 2;

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
        foreach (var (what, method, wrong) in Wrong)
        {
            var diffCase = new DiffCase { Name = what, Method = method };
            ExpectCaught(problems, what, diffCase,
                new ProbeTextRule("probe-text", wrong));
        }
        foreach (var (what, text) in Unreadable)
        {
            ExpectUnreadable(problems, what, text);
        }
        return problems;
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
