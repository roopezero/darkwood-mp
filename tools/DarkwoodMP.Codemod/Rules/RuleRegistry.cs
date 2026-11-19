namespace DarkwoodMP.Codemod.Rules;

/// The rules "codemod rule" and "codemod difftest" can run by name. The real
/// rules of stages 3 to 7 are added here as they are written.
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
            default:
                return null;
        }
    }

    public static string Names =>
        "ternary-direct, ternary-hoist, probe-return (the tool's own test "
            + "rule)";
}
