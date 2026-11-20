namespace DarkwoodMP.Codemod.Rules;

/// The rules "codemod rule" and "codemod difftest" can run by name: the real
/// rules of stages 3 to 7 and the tool's own test rule.
static class RuleRegistry
{
    public static Rule Find(string name)
    {
        switch (name)
        {
            case "probe-return":
                return new ProbeReturnRule(ProbeForm.Correct);
            case "ternary-direct":
                return new TernaryDirectRule();
            case "ternary-hoist":
                return new TernaryHoistRule();
            case "coalesce":
                return new CoalesceRule();
            case "conditional-access":
                return new ConditionalAccessRule();
            case "unity-null":
                return new UnityNullRule();
            default:
                return null;
        }
    }

    public static string Names =>
        "ternary-direct, ternary-hoist, coalesce, conditional-access, "
            + "unity-null, probe-return (the tool's own test rule)";
}
