using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Stages;

/// Where the wrap stage may put a comment. The word check can't see a comment
/// that moved past code, or two paragraphs that became one, so the stage
/// proves both here. Every comment word must stay between the same two tokens
/// of code, and on a line of its own if it was. The one move allowed is a
/// comment behind code going to a line of its own above that code: above its
/// own line, or above the first line of a run of lines that all end in a
/// comment (a sentence that ran on from one trailing comment to the next).
/// The empty comment lines that part paragraphs must stay where they were,
/// counted by the comment words in front of them.
static class WrapPlaces
{
    /// The check for one file: split strings are joined back on both sides
    /// first when the stage split any.
    public static string Of(SyntaxNode before, SyntaxNode after, bool split)
    {
        if (split)
        {
            return Difference(
                JoinedStrings.Joined(before), JoinedStrings.Joined(after));
        }
        return Difference(before, after);
    }

    /// Null when every comment is where the stage may put it; otherwise the
    /// first one that isn't. Both trees must have their split strings joined
    /// back when the stage split any (JoinedStrings.Joined), so the tokens of
    /// code match one for one.
    public static string Difference(SyntaxNode before, SyntaxNode after)
    {
        var was = Of(before, out var wasBreaks);
        var now = Of(after, out var nowBreaks);
        if (was.Count != now.Count)
        {
            return $"{was.Count} comment words became {now.Count}";
        }
        for (int i = 0; i < was.Count; i++)
        {
            if (was[i].Word != now[i].Word)
            {
                return $"comment word {i} \"{was[i].Word}\" became "
                    + $"\"{now[i].Word}\"";
            }
            if (!Allowed(was[i], now[i]))
            {
                return $"the comment word \"{was[i].Word}\" (word {i}) "
                    + $"moved: it was after {was[i].Gap} tokens of code"
                    + Where(was[i]) + $", and is after {now[i].Gap}"
                    + Where(now[i]);
            }
        }
        var paragraphs = FirstDifference(wasBreaks, nowBreaks);
        if (paragraphs != null)
        {
            return paragraphs;
        }
        return null;
    }

    static string Where(Spot word)
    {
        if (word.Behind)
        {
            return " behind code";
        }
        return " on a line of its own";
    }

    static bool Allowed(Spot was, Spot now)
    {
        if (now.Gap == was.Gap && now.Behind == was.Behind)
        {
            return true;
        }
        return was.Behind && !now.Behind && was.Above.Contains(now.Gap);
    }

    static string FirstDifference(List<int> was, List<int> now)
    {
        if (was.Count != now.Count)
        {
            return $"{was.Count} empty comment lines became {now.Count}: "
                + "two paragraphs were joined or one was split";
        }
        for (int i = 0; i < was.Count; i++)
        {
            if (was[i] != now[i])
            {
                return $"an empty comment line moved: it was after "
                    + $"{was[i]} comment words, and is after {now[i]}";
            }
        }
        return null;
    }

    /// Every comment word as CommentWords.ForWrap compares it, with the
    /// number of code tokens in front of it, whether code is in front of it
    /// on its line, and where it may go if so. Breaks: for each empty
    /// comment line, the number of comment words in front of it.
    static List<Spot> Of(SyntaxNode root, out List<int> breaks)
    {
        var text = SourceText.From(root.ToFullString());
        var words = new List<Spot>();
        breaks = new List<int>();
        var run = new List<int>();
        int lastRunLine = -2;
        int gap = 0;
        int position = 0;
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                Collect(trivia, position, gap, text, words, breaks, null);
                position += trivia.FullSpan.Length;
            }
            // The first token of a line: the place above that line.
            var line = text.Lines.GetLineFromPosition(position).LineNumber;
            if (FirstOnLine(text, position))
            {
                if (line != lastRunLine + 1)
                {
                    run.Clear();
                }
                run.Add(gap);
            }
            position += token.Span.Length;
            gap++;
            foreach (var trivia in token.TrailingTrivia)
            {
                bool behind = Format.Lines.IsComment(trivia);
                Collect(trivia, position, gap, text, words, breaks, run);
                if (behind && BehindCode(text, position))
                {
                    lastRunLine =
                        text.Lines.GetLineFromPosition(position).LineNumber;
                }
                position += trivia.FullSpan.Length;
            }
        }
        return words;
    }

    static void Collect(
        SyntaxTrivia trivia, int position, int gap, SourceText text,
        List<Spot> words, List<int> breaks, List<int> run)
    {
        if (!Format.Lines.IsComment(trivia))
        {
            return;
        }
        bool behind = BehindCode(text, position);
        var above = new HashSet<int>();
        if (behind && run != null)
        {
            above.UnionWith(run);
        }
        var full = trivia.ToFullString();
        foreach (var piece in full.Split('\n'))
        {
            var body = piece.Trim();
            if (body == "//" || body == "///")
            {
                breaks.Add(words.Count);
                continue;
            }
            var raw = body.Replace("///", " ").Replace("//", " ")
                .Replace("/*", " ").Replace("*/", " ")
                .Split(Spaces, StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            foreach (var word in CommentWords.ForWrap(raw))
            {
                words.Add(new Spot(word, gap, behind, above));
            }
        }
    }

    static readonly char[] Spaces = { ' ', '\t', '\r' };

    /// True when code stands in front of position on its line.
    static bool BehindCode(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);
        for (int i = line.Start; i < position; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return true;
            }
        }
        return false;
    }

    static bool FirstOnLine(SourceText text, int position)
    {
        return !BehindCode(text, position);
    }

    readonly record struct Spot(
        string Word, int Gap, bool Behind, HashSet<int> Above);
}
