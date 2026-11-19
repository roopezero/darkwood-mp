namespace DarkwoodMP.Codemod.Il;

/// What the comparison of two DLLs found.
sealed class IlDiffReport
{
    public int Same;
    public int Added;
    public int Removed;

    /// Methods whose body changed, inside the manifest.
    public readonly List<string> Changed = new();

    /// Each method of Changed with the manifest entries it counts for.
    public readonly List<(string Method, string Entries)> Credits = new();

    /// Every reason the diff fails. Empty means the gate passed.
    public readonly List<string> Problems = new();

    public bool Passed => Problems.Count == 0;

    /// One line per changed method, with what it was credited to, in the
    /// order of the methods' names.
    public void PrintCredits(TextWriter output)
    {
        var sorted = Credits.OrderBy(
            credit => credit.Method, StringComparer.Ordinal);
        foreach (var (method, entries) in sorted)
        {
            output.WriteLine("changed  " + method + "  for  " + entries);
        }
    }

    public void Print(TextWriter output)
    {
        output.WriteLine(
            $"ildiff: {Same} methods identical, {Changed.Count} changed "
                + $"inside the manifest, {Added} added and {Removed} removed "
                + "with their owners.");
        foreach (var problem in Problems)
        {
            output.WriteLine("FAIL  " + problem);
        }
        if (Passed)
        {
            output.WriteLine("ildiff: passed.");
        }
        else
        {
            output.WriteLine($"ildiff: {Problems.Count} problems.");
        }
    }
}
