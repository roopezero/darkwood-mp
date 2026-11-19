namespace DarkwoodMP.Codemod.DiffTest;

/// A small method written once in its original form: static Run(...) with
/// every operand going through Log("name", value).
sealed class DiffCase
{
    public string Name;
    public string Method;

    /// Reads a .cases file: "=== name" lines, each followed by one method.
    public static List<DiffCase> Read(string path)
    {
        var cases = new List<DiffCase>();
        DiffCase current = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                current = new DiffCase { Name = line.Substring(4).Trim() };
                cases.Add(current);
            }
            else if (current != null)
            {
                current.Method += line + "\n";
            }
        }
        return cases;
    }
}
