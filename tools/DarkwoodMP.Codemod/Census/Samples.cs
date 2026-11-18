using Microsoft.CodeAnalysis;

namespace DarkwoodMP.Codemod.Census;

/// Up to five example lines per census key, printed with --samples.
sealed class Samples
{
    const int PerKey = 5;

    readonly Dictionary<string, List<string>> _lines = new();

    public void Add(string key, SyntaxNode node)
    {
        if (!_lines.TryGetValue(key, out var list))
        {
            list = new List<string>();
            _lines[key] = list;
        }
        if (list.Count >= PerKey)
        {
            return;
        }
        var span = node.GetLocation().GetLineSpan();
        int line = span.StartLinePosition.Line;
        var text = node.SyntaxTree.GetText().Lines[line].ToString().Trim();
        list.Add($"{Path.GetFileName(span.Path)}:{line + 1}  {text}");
    }

    public void Print()
    {
        Console.WriteLine("\n--- samples");
        foreach (var (key, list) in _lines.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(key);
            foreach (var line in list)
            {
                var shown = line;
                if (shown.Length > 150)
                {
                    shown = shown.Substring(0, 150) + "...";
                }
                Console.WriteLine("    " + shown);
            }
        }
    }
}
