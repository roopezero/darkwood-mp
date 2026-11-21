using DarkwoodMP.Codemod.Format;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Stages;

/// Every line still over the limit after the wrap stage, with the reason no
/// rule shortened it: for a person to fix by hand, or for stage 11.
static class WidthReport
{
    public sealed record Left(string File, int Line, int Width, string Reason);

    public static List<Left> Of(
        string file, string text, CSharpParseOptions parse)
    {
        var tree = CSharpSyntaxTree.ParseText(text, parse);
        var lines = tree.GetText().Lines;
        var left = new List<Left>();
        foreach (var plan in Wrap.Plan(tree))
        {
            var reason = plan.Reason;
            if (plan.Edits.Count > 0)
            {
                reason = "the Formatter undoes the break (" + plan.Rule + ")";
            }
            foreach (var number in plan.Lines)
            {
                int width = lines[number].Span.Length;
                if (width > Wrap.Max)
                {
                    left.Add(new Left(file, number + 1, width, reason));
                }
            }
        }
        return left;
    }

    /// One line per long line, then the count of each reason.
    public static void Print(List<Left> left, TextWriter output)
    {
        foreach (var line in left)
        {
            output.WriteLine(
                $"{line.File}:{line.Line}: {line.Width} columns: "
                    + line.Reason);
        }
        var reasons = left
            .GroupBy(l => l.Reason)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        output.WriteLine(
            $"{left.Count} lines are still over {Wrap.Max} columns:");
        foreach (var reason in reasons)
        {
            output.WriteLine($"  {reason.Count(),5}  {reason.Key}");
        }
    }
}
