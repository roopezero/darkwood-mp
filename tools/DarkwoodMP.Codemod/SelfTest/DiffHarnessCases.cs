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

    public const int Count = 9;

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
        return problems;
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
