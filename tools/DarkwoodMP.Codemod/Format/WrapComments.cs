using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Format;

/// The width rules for comments: a long comment line is reflowed with the
/// rest of its paragraph, and a comment behind code goes on a line of its
/// own above it. Only plain prose is reflowed. Code, lists, tables, diagrams
/// and commented-out code inside a comment keep their lines, because their
/// line breaks carry meaning.
static class WrapComments
{
    static readonly Regex ListItem =
        new(@"^([-*+•]|\d+[.)]|[a-z][.)]|\(\d+\)|\([a-z]\))\s");
    static readonly Regex CodeStart =
        new(@"^(if|else|for|foreach|while|var|return|using|case|new|switch)"
            + @"\b.*[;{(]");
    public static readonly Regex OneLineTag =
        new(@"^<(summary|remarks|returns)>(.*\S.*)</\1>$");
    static readonly Regex Heading = new(@"^-{3,} \S.*-{3,}$");
    static readonly Regex TagsOnly = new(@"^(<[^<>]*>\s*)+$");
    static readonly Regex OpensTag = new(@"^<[A-Za-z]");
    public static readonly Regex Divider = new(@"^[-=*#~_+]{3,}$");
    const string BoxChars = "─│┌┐└┘├┤┬┴┼═║╔╗╚╝→←↑↓";

    /// A line that holds only a comment.
    public static LinePlan Plan(WrapLine line, SyntaxTrivia comment)
    {
        if (comment.IsKind(SyntaxKind.MultiLineCommentTrivia))
        {
            return line.Leave("a /* */ comment");
        }
        var prefix = PrefixOf(line.Text);
        var body = BodyOf(line.Text, prefix);
        if (body != null && Divider.IsMatch(body.Trim())
            && body.Trim().Distinct().Count() == 1)
        {
            return Shorten(line, prefix, body.Trim());
        }
        if (body != null && Heading.IsMatch(body.Trim()))
        {
            return ShortenHeading(line, body);
        }
        if (prefix.StartsWith("///", StringComparison.Ordinal) && body != null)
        {
            var tag = OneLineTag.Match(body.Trim());
            if (tag.Success)
            {
                return OpenTag(line, prefix, tag);
            }
        }
        var kind = KindOf(body);
        if (kind != null)
        {
            return line.Leave("a comment line that is " + kind);
        }
        var lines = Paragraph(line, prefix);
        var first = line.Source.Lines[lines[0]];
        var last = line.Source.Lines[lines[^1]];
        var span = TextSpan.FromBounds(first.Start, last.End);
        var old = line.Source.ToString(span);
        var reflowed = Reflow(line, prefix, lines);
        if (reflowed == old)
        {
            return line.Leave(
                "a comment word too long for the line (a URL or a path)");
        }
        var plan = line.Break("comment paragraph reflowed",
            new TextChange(span, reflowed));
        plan.Lines.Clear();
        plan.Lines.AddRange(lines);
        return plan;
    }

    /// A divider of one repeated character, cut to the limit. The comment
    /// word check counts any run of three or more of them as the same word
    /// (CommentWords.Of with dividers).
    static LinePlan Shorten(WrapLine line, string prefix, string divider)
    {
        int room = Wrap.Max - line.Indent - prefix.Length;
        if (room < 3)
        {
            return line.Leave("a divider with no room to shorten it");
        }
        var text = line.Pad + prefix + new string(divider[0], room);
        return line.Break(
            "divider shortened",
            new TextChange(line.Line.Span, text));
    }

    /// A heading such as "---- name ------": the dashes behind it are cut
    /// so the line fits, keeping three.
    static LinePlan ShortenHeading(WrapLine line, string body)
    {
        var trimmed = body.TrimEnd();
        int dashes = trimmed.Length - trimmed.TrimEnd(trimmed[^1]).Length;
        int over = line.Text.TrimEnd().Length - Wrap.Max;
        if (dashes < 3 || dashes - over < 3)
        {
            return line.Leave("a comment heading too long for the line");
        }
        var text = line.Text.TrimEnd();
        text = text.Substring(0, text.Length - over);
        return line.Break(
            "heading shortened", new TextChange(line.Line.Span, text));
    }

    /// A one-line <summary>text</summary> (or remarks, returns) that is too
    /// long: the tags go on lines of their own and the text is filled
    /// between them. The comment word check splits words at these tags
    /// (CommentWords.ForWrap), so both forms hold the same words.
    static LinePlan OpenTag(WrapLine line, string prefix, Match tag)
    {
        var name = tag.Groups[1].Value;
        var head = line.Pad + prefix;
        var lines = new List<string> { head + "<" + name + ">" };
        lines.AddRange(Fill(head, Words(tag.Groups[2].Value)));
        lines.Add(head + "</" + name + ">");
        return line.Break(
            "doc tag opened",
            new TextChange(line.Line.Span, string.Join("\n", lines)));
    }

    /// A line of code with a // comment at its end: the comment goes on a
    /// line of its own above the code. Null when there is no such comment.
    public static LinePlan MoveTrailing(WrapLine line)
    {
        var trivia = line.Root.FindTrivia(line.End - 1);
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.Span.End != line.End
            || trivia.SpanStart <= line.Start + line.Indent)
        {
            return null;
        }
        int code = trivia.SpanStart;
        while (code > line.Start && line.Source[code - 1] == ' ')
        {
            code--;
        }
        var above = new TextChange(
            new TextSpan(line.Start, 0),
            line.Pad + trivia.ToString() + "\n");
        var behind = new TextChange(
            TextSpan.FromBounds(code, line.End), "");
        return line.Break("trailing comment moved above", above, behind);
    }

    /// "///" or "//", and the space after it when the line has one.
    static string PrefixOf(string text)
    {
        var body = text.TrimStart();
        var slashes = "//";
        if (body.StartsWith("///", StringComparison.Ordinal))
        {
            slashes = "///";
        }
        if (body.Length > slashes.Length && body[slashes.Length] == ' ')
        {
            return slashes + " ";
        }
        return slashes;
    }

    static string BodyOf(string text, string prefix)
    {
        var body = text.TrimStart();
        if (!body.StartsWith(prefix.TrimEnd(), StringComparison.Ordinal))
        {
            return null;
        }
        return body.Substring(Math.Min(prefix.Length, body.Length));
    }

    /// What a comment line is when it isn't prose, or null for prose.
    public static string KindOf(string body)
    {
        if (body == null)
        {
            return "not a // line";
        }
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            return "empty";
        }
        if (body.StartsWith(' ') || body.StartsWith('\t'))
        {
            return "indented (code, a list's next line or a diagram)";
        }
        if (body.StartsWith('/'))
        {
            return "commented-out code or a divider";
        }
        if (ListItem.IsMatch(body))
        {
            return "a list item";
        }
        if (trimmed.StartsWith('|') || trimmed.EndsWith('|'))
        {
            return "a table row";
        }
        if (Divider.IsMatch(trimmed))
        {
            return "a divider";
        }
        if (trimmed.IndexOfAny(BoxChars.ToCharArray()) >= 0
            || trimmed.Contains("   "))
        {
            return "a diagram";
        }
        if (TagsOnly.IsMatch(trimmed))
        {
            return "an XML doc tag line";
        }
        if (LooksLikeCode(trimmed))
        {
            return "code";
        }
        return null;
    }

    /// Commented-out code: a line that ends like a statement or a block and
    /// starts like one, or has hardly any plain words. Prose here often ends
    /// in a semicolon, so the ending alone isn't enough.
    static bool LooksLikeCode(string trimmed)
    {
        bool ends = trimmed.EndsWith(';') || trimmed.EndsWith('{')
            || trimmed.EndsWith('}') || trimmed.StartsWith('{')
            || trimmed.StartsWith('}');
        if (!ends)
        {
            return false;
        }
        if (CodeStart.IsMatch(trimmed))
        {
            return true;
        }
        int words = trimmed.Split(' ')
            .Count(w => w.Length > 0 && w.All(char.IsLetter)
                && char.IsLower(w[0]));
        return words < 3;
    }

    /// The numbers of the prose lines around this one, in the same comment:
    /// the same indentation and prefix, up to an empty or non-prose line.
    static List<int> Paragraph(WrapLine line, string prefix)
    {
        var lines = new List<int> { line.Number };
        for (int n = line.Number - 1; n >= 0; n--)
        {
            if (OpensParagraph(line, n + 1) || !Joins(line, n, prefix))
            {
                break;
            }
            lines.Insert(0, n);
        }
        var count = line.Source.Lines.Count;
        for (int n = line.Number + 1; n < count; n++)
        {
            if (OpensParagraph(line, n) || !Joins(line, n, prefix))
            {
                break;
            }
            lines.Add(n);
        }
        return lines;
    }

    /// A doc comment line that opens a tag (<param ...>, <summary>) starts a
    /// paragraph of its own: the tag's text never joins the one before.
    static bool OpensParagraph(WrapLine line, int number)
    {
        var text = line.Source.Lines[number].ToString();
        var body = BodyOf(text, PrefixOf(text));
        return body != null && OpensTag.IsMatch(body);
    }

    static bool Joins(WrapLine line, int number, string prefix)
    {
        var other = new WrapLine(
            line.Root, line.Source, line.Source.Lines[number]);
        if (other.Indent != line.Indent || !other.IsCommentOnly(out var c))
        {
            return false;
        }
        if (c.IsKind(SyntaxKind.MultiLineCommentTrivia))
        {
            return false;
        }
        if (PrefixOf(other.Text).TrimEnd() != prefix.TrimEnd())
        {
            return false;
        }
        var body = BodyOf(other.Text, PrefixOf(other.Text));
        return KindOf(body) == null;
    }

    /// The paragraph's words filled greedily into lines of at most Max
    /// columns. A word never breaks, and a run between < and > counts as one
    /// word, so an XML tag stays on one line.
    static string Reflow(WrapLine line, string prefix, List<int> numbers)
    {
        var words = new List<string>();
        foreach (var number in numbers)
        {
            var text = line.Source.Lines[number].ToString();
            words.AddRange(Words(BodyOf(text, PrefixOf(text))));
        }
        return string.Join("\n", Fill(line.Pad + prefix, words));
    }

    /// Words filled greedily behind head into lines of at most Max columns.
    static List<string> Fill(string head, List<string> words)
    {
        var lines = new List<string>();
        var current = new StringBuilder(head);
        bool empty = true;
        foreach (var word in words)
        {
            if (!empty && current.Length + 1 + word.Length > Wrap.Max)
            {
                lines.Add(current.ToString());
                current.Clear().Append(head);
                empty = true;
            }
            if (!empty)
            {
                current.Append(' ');
            }
            current.Append(word);
            empty = false;
        }
        lines.Add(current.ToString());
        return lines;
    }

    static List<string> Words(string body)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        int depth = 0;
        for (int i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '<' && i + 1 < body.Length
                && (char.IsLetter(body[i + 1]) || body[i + 1] == '/'))
            {
                depth++;
            }
            if (c == '>' && depth > 0)
            {
                depth--;
            }
            if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (word.Length > 0)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
                continue;
            }
            word.Append(c);
        }
        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }
        return words;
    }
}
