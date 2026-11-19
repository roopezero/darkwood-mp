namespace DarkwoodMP.Codemod.Rules;

/// The rules "codemod rule" and "codemod difftest" can run by name. The real
/// rules of stages 3 to 7 are added here as they are written.
static class RuleRegistry
{
    public static Rule Find(string name)
    {
        if (name == "probe-return")
        {
            return new ProbeReturnRule(ProbeForm.Correct);
        }
        return null;
    }

    public static string Names => "probe-return (the tool's own test rule)";
}
