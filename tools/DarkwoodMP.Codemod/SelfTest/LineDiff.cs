using System.Text;

namespace DarkwoodMP.Codemod.SelfTest;

/// A small line-by-line difference for failure messages.
static class LineDiff
{
    public static string Of(string expected, string actual)
    {
        var a = expected.Split('\n');
        var b = actual.Split('\n');
        var text = new StringBuilder();
        int limit = Math.Max(a.Length, b.Length);
        int shown = 0;
        for (int i = 0; i < limit && shown < 12; i++)
        {
            var left = i < a.Length ? a[i] : "<end>";
            var right = i < b.Length ? b[i] : "<end>";
            if (left == right)
            {
                continue;
            }
            shown++;
            text.Append($"    line {i + 1} expected: {left}\n");
            text.Append($"    line {i + 1}   actual: {right}\n");
        }
        return text.ToString().TrimEnd('\n');
    }
}
