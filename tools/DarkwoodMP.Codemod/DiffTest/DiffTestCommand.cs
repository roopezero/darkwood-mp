using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Rules;

namespace DarkwoodMP.Codemod.DiffTest;

/// "codemod difftest RULE": runs the cases in DiffTest/Cases/RULE.cases
/// through the harness. Exit code 1 when any case fails.
static class DiffTestCommand
{
    const string CaseFolder = "tools/DarkwoodMP.Codemod/DiffTest/Cases";

    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            throw new Stop("codemod difftest <rule>");
        }
        if (args[0] == "interpolated")
        {
            return RunCases(args[0], InterpolatedCases.Run);
        }
        var rule = RuleRegistry.Find(args[0]);
        if (rule == null)
        {
            throw new Stop($"No rule named {args[0]}. Rules: {RuleRegistry.Names}");
        }
        return RunCases(rule.Name, c => DiffHarness.Run(rule, c));
    }

    static int RunCases(string name, Func<DiffCase, DiffOutcome> run)
    {
        var file = Path.Combine(
            RepoPaths.Find().Root, CaseFolder, name + ".cases");
        if (!File.Exists(file))
        {
            throw new Stop("No differential cases for this rule: " + file);
        }
        int failed = 0;
        var cases = DiffCase.Read(file);
        foreach (var diffCase in cases)
        {
            var outcome = run(diffCase);
            if (outcome.Passed)
            {
                Console.WriteLine($"ok    {diffCase.Name} ({outcome.Rows} rows)");
                continue;
            }
            failed++;
            Console.WriteLine("FAIL  " + diffCase.Name);
            foreach (var mismatch in outcome.Mismatches.Take(10))
            {
                Console.WriteLine("  " + mismatch);
            }
        }
        Console.WriteLine($"{cases.Count - failed} of {cases.Count} cases passed.");
        if (failed > 0)
        {
            return 1;
        }
        return 0;
    }
}
