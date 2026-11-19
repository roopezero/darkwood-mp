using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Census;

/// Lines over 80 columns, by what is on them.
sealed class LineCensus
{
    const int Limit = 80;

    public int Lines;

    public readonly Tally OverLimit = new Tally("lines over 80 columns");

    public void Count(SyntaxTree tree, SyntaxNode root, string file)
    {
        var source = tree.GetText();
        foreach (var line in source.Lines)
        {
            // What follows a file's last line break isn't a line.
            if (line.Start == source.Length)
            {
                continue;
            }
            Lines++;
            var text = line.ToString();
            if (text.Length > Limit)
            {
                OverLimit.Add(Kind(text, root, line), file);
            }
        }
    }

    static string Kind(
        string text,
        SyntaxNode root,
        Microsoft.CodeAnalysis.Text.TextLine line)
    {
        if (text.TrimStart().StartsWith("//", StringComparison.Ordinal))
        {
            return "comment only";
        }
        int last = Math.Max(line.End - 1, 0);
        if (root.FindTrivia(last).IsKind(SyntaxKind.SingleLineCommentTrivia))
        {
            return "code + trailing comment";
        }
        var token = root.FindToken(line.Start + Limit);
        if (token.IsKind(SyntaxKind.StringLiteralToken)
            || token.IsKind(SyntaxKind.InterpolatedStringTextToken))
        {
            return "code, a string crosses column 80";
        }
        return "code";
    }
}
