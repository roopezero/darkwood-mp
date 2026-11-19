namespace DarkwoodMP.Codemod.DiffTest;

/// A small method written once in its original form: static Run(...) with
/// every operand going through Log("name", value).
sealed class DiffCase
{
    public string Name;
    public string Method;

    /// Reads a .cases file: "=== name" lines, each followed by one method.
    /// Stops on a file that holds no case, on a line before the first header
    /// and on a header with no method, so that a mistyped file can't pass by
    /// testing nothing.
    public static List<DiffCase> Read(string path)
    {
        var cases = new List<DiffCase>();
        DiffCase current = null;
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                RequireMethod(current, path);
                current = new DiffCase { Name = line.Substring(4).Trim() };
                cases.Add(current);
            }
            else if (current != null)
            {
                current.Method += line + "\n";
            }
            else if (line.Trim().Length > 0)
            {
                throw new Stop($"{path}:{i + 1}: a line before the first "
                    + $"\"=== name\" header: {line.Trim()}");
            }
        }
        RequireMethod(current, path);
        if (cases.Count == 0)
        {
            throw new Stop($"{path} holds no case.");
        }
        return cases;
    }

    static void RequireMethod(DiffCase diffCase, string path)
    {
        if (diffCase != null && string.IsNullOrWhiteSpace(diffCase.Method))
        {
            throw new Stop(
                $"{path}: the case \"{diffCase.Name}\" has no method.");
        }
    }
}
