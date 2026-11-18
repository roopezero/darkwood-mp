using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Stages;

/// Says where two trees stop being the same code, for failure messages.
static class TokenDiff
{
    /// The first token that differs, or a note that only the structure does.
    public static string First(SyntaxNode a, SyntaxNode b)
    {
        var left = a.DescendantTokens().ToList();
        var right = b.DescendantTokens().ToList();
        int limit = Math.Min(left.Count, right.Count);
        for (int i = 0; i < limit; i++)
        {
            if (left[i].RawKind != right[i].RawKind
                || left[i].Text != right[i].Text)
            {
                return $"token '{Text(left[i])}' at line {Line(left[i])} "
                    + $"became '{Text(right[i])}' at line {Line(right[i])}";
            }
        }
        if (left.Count != right.Count)
        {
            return $"{left.Count} tokens became {right.Count}";
        }
        return "the tokens are the same but the tree differs: "
            + FirstNode(a, b);
    }

    /// Walks both trees side by side to the first node of a different kind.
    static string FirstNode(SyntaxNode a, SyntaxNode b)
    {
        var left = a.ChildNodes().ToList();
        var right = b.ChildNodes().ToList();
        for (int i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            if (left[i].RawKind != right[i].RawKind)
            {
                return $"line {Line(left[i].GetFirstToken())}: "
                    + $"{left[i].Kind()} became {right[i].Kind()} "
                    + $"at line {Line(right[i].GetFirstToken())}";
            }
            if (!SameCode.Of(left[i], right[i]))
            {
                return FirstNode(left[i], right[i]);
            }
        }
        return $"line {Line(a.GetFirstToken())}: {a.Kind()} has "
            + $"{left.Count} children, then {right.Count}";
    }

    static string Text(SyntaxToken token)
    {
        var text = token.Text;
        if (text.Length > 60)
        {
            text = text.Substring(0, 60) + "...";
        }
        return text.Replace("\n", "\\n");
    }

    static int Line(SyntaxToken token)
    {
        return token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
