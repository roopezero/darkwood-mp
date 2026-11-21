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

    /// The words as the wrap stage compares them: every divider (a word of
    /// one character repeated three or more times, such as "-----") made the
    /// same, because the stage shortens dividers to fit the line, and the
    /// tags it moves onto lines of their own split off (see SplitAtTags).
    public static List<string> ForWrap(List<string> words)
    {
        var same = new List<string>();
        foreach (var word in SplitAtTags(words))
        {
            if (IsDivider(word))
            {
                same.Add("<divider " + word[0] + ">");
            }
            else
            {
                same.Add(word);
            }
        }
        return same;
    }

    static readonly string[] Tags =
    {
        "<summary>", "</summary>", "<remarks>", "</remarks>",
        "<returns>", "</returns>",
    };

    /// The words with <summary>, </summary> and the like as words of their
    /// own, wherever they were glued to a word.
    static List<string> SplitAtTags(List<string> words)
    {
        var split = new List<string>();
        foreach (var word in words)
        {
            var text = word;
            foreach (var tag in Tags)
            {
                text = text.Replace(tag, " " + tag + " ");
            }
            split.AddRange(
                text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        return split;
    }

    static bool IsDivider(string word)
    {
        return Format.WrapComments.Divider.IsMatch(word)
            && word.Distinct().Count() == 1;
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
