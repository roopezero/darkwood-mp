using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Stages;

/// The words of all comments in a file, in order. A whitespace stage must not
/// change them.
static class CommentWords
{
    static readonly char[] Spaces = { ' ', '\t', '\r', '\n' };

    public static List<string> Of(SyntaxNode root)
    {
        var words = new List<string>();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (!Format.Lines.IsComment(trivia))
            {
                continue;
            }
            var text = trivia.ToFullString()
                .Replace("///", " ")
                .Replace("//", " ")
                .Replace("/*", " ")
                .Replace("*/", " ");
            words.AddRange(
                text.Split(Spaces, StringSplitOptions.RemoveEmptyEntries));
        }
        return words;
    }

    public static List<string> Of(string source, CSharpParseOptions parse)
    {
        return Of(CSharpSyntaxTree.ParseText(source, parse).GetRoot());
    }

    /// Where two word lists first differ, or null when they are the same.
    public static string FirstDifference(List<string> a, List<string> b)
    {
        if (a.SequenceEqual(b))
        {
            return null;
        }
        int i = 0;
        while (i < a.Count && i < b.Count && a[i] == b[i])
        {
            i++;
        }
        var left = "<end>";
        if (i < a.Count)
        {
            left = a[i];
        }
        var right = "<end>";
        if (i < b.Count)
        {
            right = b[i];
        }
        return $"word {i}: '{left}' becomes '{right}'";
    }
}
