using DarkwoodMP.Codemod.Il;

namespace DarkwoodMP.Codemod.Rules;

/// What a rule did to the sources, and the numbers that must add up.
sealed class RuleResult
{
    public readonly string RuleName;

    /// New text per rewritten file, by path.
    public readonly Dictionary<string, string> Rewritten = new();

    public readonly List<SiteEntry> Sites = new();
    public readonly List<QueueEntry> Queue = new();
    public readonly Manifest Manifest = new Manifest();

    public readonly int[] Before = new int[OperatorCount.All.Length];
    public readonly int[] Removed = new int[OperatorCount.All.Length];
    public readonly int[] Queued = new int[OperatorCount.All.Length];
    public readonly int[] After = new int[OperatorCount.All.Length];

    public RuleResult(string ruleName)
    {
        RuleName = ruleName;
        Manifest.Rule = ruleName;
    }

    /// Sites of an operator the rule neither rewrote nor queued. They are
    /// still in the code, waiting for a later rule.
    public int LeftFor(Operator op)
    {
        int i = (int)op;
        return Before[i] - Removed[i] - Queued[i];
    }

    /// Stops unless census before = rewritten + queued + left for later rules,
    /// and the census after the rewrite shows exactly the sites not rewritten.
    public void CheckCensus()
    {
        foreach (var op in OperatorCount.All)
        {
            int i = (int)op;
            var name = OperatorCount.Label(op);
            if (LeftFor(op) < 0)
            {
                throw new Stop(
                    $"Census of {name}: {Before[i]} before, but {Removed[i]} "
                        + $"rewritten and {Queued[i]} queued. A site counted "
                        + "twice is a bug in the rule.");
            }
            if (After[i] != Before[i] - Removed[i])
            {
                throw new Stop(
                    $"Census of {name}: {Before[i]} before and {Removed[i]} "
                        + $"rewritten, so {Before[i] - Removed[i]} should "
                        + $"remain, but the rewritten code has {After[i]}. "
                        + "Nothing was written.");
            }
        }
    }

    public void PrintCensus(TextWriter output)
    {
        output.WriteLine("operator  before  rewritten  queued  later  after");
        foreach (var op in OperatorCount.All)
        {
            int i = (int)op;
            output.WriteLine(
                $"{OperatorCount.Label(op),-8}  {Before[i],6}  {Removed[i],9}  "
                    + $"{Queued[i],6}  {LeftFor(op),5}  {After[i],5}");
        }
    }
}
