namespace DarkwoodMP.Codemod.SelfTest;

/// One file in SelfTest/Golden: a piece of source and what the two stages must
/// make of it. Sections start with a line "=== input", "=== braces" or
/// "=== layout". A case with no braces section expects braces to change
/// nothing. Lines before the first section are the case's description.
sealed class GoldenCase
{
    public readonly string Name;
    public readonly string Input;

    /// What the braces stage makes of Input.
    public readonly string Braces;

    /// What the layout stage makes of the braces stage's output.
    public readonly string Layout;

    GoldenCase(string name, string input, string braces, string layout)
    {
        Name = name;
        Input = input;
        Braces = braces;
        Layout = layout;
    }

    public static GoldenCase Read(string file)
    {
        var sections = new Dictionary<string, List<string>>();
        List<string> current = null;
        foreach (var line in File.ReadAllLines(file))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                current = new List<string>();
                sections[line.Substring(4).Trim()] = current;
            }
            else if (current != null)
            {
                current.Add(line);
            }
        }
        var name = Path.GetFileNameWithoutExtension(file);
        if (!sections.ContainsKey("input"))
        {
            throw new Stop("Golden case " + name + " has no input section.");
        }
        var input = Text(sections["input"]);
        var braces = input;
        if (sections.ContainsKey("braces"))
        {
            braces = Text(sections["braces"]);
        }
        string layout = null;
        if (sections.ContainsKey("layout"))
        {
            layout = Text(sections["layout"]);
        }
        return new GoldenCase(name, input, braces, layout);
    }

    static string Text(List<string> lines)
    {
        return string.Join("\n", lines) + "\n";
    }
}
