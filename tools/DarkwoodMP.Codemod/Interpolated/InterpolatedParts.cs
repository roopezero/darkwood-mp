using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Interpolated;

/// The text of an interpolated string cut in two at a space of its literal
/// text, never inside a hole. Each hole is copied as it is written, with its
/// alignment and format, and the parts keep the order of the original. A
/// part with no hole becomes a plain string: "$" goes and "{{" and "}}" are
/// written "{" and "}".
sealed class InterpolatedParts
{
    public readonly string Head;
    public readonly string Tail;

    InterpolatedParts(string head, string tail)
    {
        Head = head;
        Tail = tail;
    }

    /// The cut that leaves the longest head within room columns, closing
    /// quote included, with at least one hole-free character in front of
    /// the space and something behind it; safe says whether a cut may have
    /// that many holes in front of it. Null when no cut fits.
    public static InterpolatedParts Cut(
        InterpolatedStringExpressionSyntax text,
        int room,
        Func<int, bool> safe)
    {
        var start = text.StringStartToken.Text;
        var end = text.StringEndToken.Text;
        bool verbatim = start.Contains('@');
        var contents = text.Contents;
        int column = start.Length;
        int holes = 0;
        int bestContent = -1;
        int bestAt = -1;
        for (int i = 0; i < contents.Count; i++)
        {
            if (contents[i] is not InterpolatedStringTextSyntax piece)
            {
                column += contents[i].ToString().Length;
                holes++;
                continue;
            }
            var chars = piece.TextToken.Text;
            for (int j = 0; j < chars.Length; j++)
            {
                if (!verbatim && chars[j] == '\\')
                {
                    j++;
                    continue;
                }
                if (chars[j] != ' ')
                {
                    continue;
                }
                int at = j + 1;
                bool fits = column + at + end.Length <= room;
                bool before = i > 0 || j > 0;
                bool behind = at < chars.Length || i < contents.Count - 1;
                if (fits && before && behind && safe(holes))
                {
                    bestContent = i;
                    bestAt = at;
                }
            }
            column += chars.Length;
        }
        if (bestContent < 0)
        {
            return null;
        }
        var cutText = ((InterpolatedStringTextSyntax)contents[bestContent])
            .TextToken.Text;
        var head = new List<string>();
        var tail = new List<string>();
        for (int i = 0; i < contents.Count; i++)
        {
            if (i < bestContent)
            {
                head.Add(contents[i].ToString());
            }
            else if (i > bestContent)
            {
                tail.Add(contents[i].ToString());
            }
        }
        head.Add(cutText.Substring(0, bestAt));
        tail.Insert(0, cutText.Substring(bestAt));
        bool headHoles = contents.Take(bestContent)
            .Any(c => c is InterpolationSyntax);
        bool tailHoles = contents.Skip(bestContent + 1)
            .Any(c => c is InterpolationSyntax);
        return new InterpolatedParts(
            Part(start, head, end, headHoles),
            Part(start, tail, end, tailHoles));
    }

    static string Part(
        string start, List<string> pieces, string end, bool holes)
    {
        var body = string.Concat(pieces);
        if (holes)
        {
            return start + body + end;
        }
        var plain = start.Replace("$", "");
        return plain + body.Replace("{{", "{").Replace("}}", "}") + end;
    }
}
