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
    static readonly Regex OpensTag =
        new(@"^<(?!(c|see|paramref|typeparamref|b|i|em)\b)[A-Za-z]");

    /// The old fill width of the plugin's comments. A line that ended a
    /// sentence while the next word would still have fitted under it was
    /// broken on purpose.
    const int OldWidth = 120;

    /// A line that ends a sentence, a clause or a list's lead-in.
    static readonly Regex EndsSentence = new(@"[.:;!?][)""']?$");

    /// A line that starts an item of a list written one item a line: a
    /// quoted option or step ("--x N": or "step":), a direction label
    /// (host -> client,), a bare number, a "Label:" head, or a wire format
    /// ("door: u8 flags", "Burning: ObjRef, ...").
    static readonly Regex StartsItem = new(
        @"^(""|\w+ -> \w+,|\d+ [a-z]|[A-Z][\w']*( [\w']+){0,2}: "
            + @"|[\w.]+( \([^)]*\))?: (" + Types + @")\b)");

    /// The type words of the wire formats written in comments.
    const string Types =
        "u8|u16|u32|u64|i8|i16|i32|i64|f32|f64|str|bigstr|blob|big|vec3"
            + "|quat|ObjRef|bool|nothing";

    static readonly Regex TypeWord = new("^(" + Types + ")$");
    static readonly Regex Number = new(@"^[\d.]+$");
    static readonly Regex Unit = new(@"^(s|ms|KiB|MiB|KB|MB|%|px|fps)[.,;:)]*$");
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
    /// line of its own above the code, filled at the code's indentation.
    /// Null when there is no such comment. Trailing comments lined up in one
    /// column on the lines around it move with it, each above its own line,
    /// so the column isn't left half moved. A comment that runs on into the
    /// next line's (it was cut where the old width ran out, and the next one
    /// starts in lower case) moves with it as one comment above the first.
    public static LinePlan MoveTrailing(WrapLine line)
    {
        var mine = TrailingOf(line);
        if (mine == null)
        {
            return null;
        }
        int column = line.Col(mine.Value.SpanStart);
        var group = new List<(WrapLine Line, SyntaxTrivia Comment)>
        {
            (line, mine.Value),
        };
        for (int n = line.Number - 1; n >= 0; n--)
        {
            var other = Aligned(line, n, column);
            if (other == null)
            {
                break;
            }
            group.Insert(0, other.Value);
        }
        for (int n = line.Number + 1; n < line.Source.Lines.Count; n++)
        {
            var other = Aligned(line, n, column);
            if (other == null)
            {
                break;
            }
            group.Add(other.Value);
        }
        var edits = new List<TextChange>();
        int i = 0;
        while (i < group.Count)
        {
            int end = i;
            while (end + 1 < group.Count && RunsOn(group[end], group[end + 1]))
            {
                end++;
            }
            var words = new List<string>();
            for (int k = i; k <= end; k++)
            {
                var text = group[k].Comment.ToString();
                words.AddRange(Words(BodyOf(text, PrefixOf(text))));
            }
            var head = group[i].Line;
            var prefix = PrefixOf(group[i].Comment.ToString());
            var filled = Fill(head.Pad + prefix, words);
            edits.Add(new TextChange(
                new TextSpan(head.Start, 0),
                string.Join("\n", filled) + "\n"));
            for (int k = i; k <= end; k++)
            {
                edits.Add(Behind(group[k].Line, group[k].Comment));
            }
            i = end + 1;
        }
        edits.Sort((x, y) => x.Span.Start.CompareTo(y.Span.Start));
        var plan = line.Break("trailing comment moved above", edits);
        plan.Lines.Clear();
        plan.Lines.AddRange(group.Select(g => g.Line.Number));
        return plan;
    }

    static SyntaxTrivia? TrailingOf(WrapLine line)
    {
        if (line.End <= line.Start)
        {
            return null;
        }
        var trivia = line.Root.FindTrivia(line.End - 1);
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.Span.End != line.End
            || trivia.SpanStart <= line.Start + line.Indent)
        {
            return null;
        }
        return trivia;
    }

    /// The line's trailing comment when it starts at column and the line has
    /// the same indentation, or null.
    static (WrapLine, SyntaxTrivia)? Aligned(WrapLine line, int number,
        int column)
    {
        var other = new WrapLine(
            line.Root, line.Source, line.Source.Lines[number]);
        if (other.Indent != line.Indent || other.InsideToken() != null)
        {
            return null;
        }
        var comment = TrailingOf(other);
        if (comment == null || other.Col(comment.Value.SpanStart) != column)
        {
            return null;
        }
        return (other, comment.Value);
    }

    /// True when the second comment goes on with the first one's sentence:
    /// the first doesn't end a sentence, its next word wouldn't have fitted
    /// on its line at the old width, and the second starts in lower case.
    static bool RunsOn(
        (WrapLine Line, SyntaxTrivia Comment) first,
        (WrapLine Line, SyntaxTrivia Comment) second)
    {
        var upper = first.Line.Text.TrimEnd();
        var text = second.Comment.ToString();
        var body = BodyOf(text, PrefixOf(text)).Trim();
        if (body.Length == 0 || !char.IsLower(body[0])
            || EndsSentence.IsMatch(upper))
        {
            return false;
        }
        var word = body.Split(' ')[0];
        return upper.Length + 1 + word.Length > OldWidth;
    }

    /// The comment and the spaces before it, cut from the end of its line.
    static TextChange Behind(WrapLine line, SyntaxTrivia comment)
    {
        int code = comment.SpanStart;
        while (code > line.Start && line.Source[code - 1] == ' ')
        {
            code--;
        }
        return new TextChange(TextSpan.FromBounds(code, line.End), "");
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
        if (trimmed.StartsWith("----", StringComparison.Ordinal)
            || Regex.IsMatch(trimmed, "-{3,}$"))
        {
            return "a heading";
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
        int width = BlockWidth(line, prefix);
        for (int n = line.Number - 1; n >= 0; n--)
        {
            if (OpensParagraph(line, n + 1) || !Joins(line, n, prefix)
                || BrokenOnPurpose(line, n, width))
            {
                break;
            }
            lines.Insert(0, n);
        }
        var count = line.Source.Lines.Count;
        for (int n = line.Number + 1; n < count; n++)
        {
            if (OpensParagraph(line, n) || !Joins(line, n, prefix)
                || BrokenOnPurpose(line, n - 1, width))
            {
                break;
            }
            lines.Add(n);
        }
        return lines;
    }

    /// The width the comment around this line was filled to: its longest
    /// line, counting every comment line of the same indent and prefix next
    /// to it, empty ones too.
    static int BlockWidth(WrapLine line, string prefix)
    {
        int width = 0;
        var count = line.Source.Lines.Count;
        for (int n = line.Number; n >= 0 && SameBlock(line, n, prefix); n--)
        {
            width = Math.Max(width, line.Source.Lines[n].Span.Length);
        }
        for (int n = line.Number + 1; n < count && SameBlock(line, n, prefix);
            n++)
        {
            width = Math.Max(width, line.Source.Lines[n].Span.Length);
        }
        return Math.Min(width, OldWidth);
    }

    static bool SameBlock(WrapLine line, int number, string prefix)
    {
        var other = new WrapLine(
            line.Root, line.Source, line.Source.Lines[number]);
        return other.Indent == line.Indent && other.IsCommentOnly(out _)
            && PrefixOf(other.Text).TrimEnd() == prefix.TrimEnd();
    }

    /// True when the break after line number was made on purpose: the line
    /// ends a sentence, and the next line starts a list item or its first
    /// word would have fitted on this line at the width the comment was
    /// filled to. Each part is then refilled by itself.
    static bool BrokenOnPurpose(WrapLine line, int number, int width)
    {
        if (number + 1 >= line.Source.Lines.Count)
        {
            return false;
        }
        var upper = line.Source.Lines[number].ToString().TrimEnd();
        var lowerText = line.Source.Lines[number + 1].ToString();
        var lower = BodyOf(lowerText, PrefixOf(lowerText));
        if (lower == null || lower.Trim().Length == 0)
        {
            return false;
        }
        if (!EndsSentence.IsMatch(upper))
        {
            return false;
        }
        if (StartsItem.IsMatch(lower))
        {
            return true;
        }
        var first = lower.Trim().Split(' ')[0];
        return upper.Length + 1 + first.Length <= width;
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

    /// Words filled greedily behind head into lines of at most Max columns,
    /// with the words that belong together kept on one line (see Glue).
    static List<string> Fill(string head, List<string> words)
    {
        return FillUnits(head, Glue(words, Wrap.Max - head.Length));
    }

    /// The words joined into the units a line never breaks inside, as long
    /// as a unit fits in room: a quoted span ("target == x"), a bracketed
    /// option ([--at EPOCH]), a type word and its field (u8 flags), "n x"
    /// and the word after it, a number and its unit (0.3 s), and the two
    /// sides of a lone "=" (b = its).
    static List<string> Glue(List<string> words, int room)
    {
        var units = new List<string>(words);
        for (int i = 0; i < units.Count; i++)
        {
            if (OpensSpan(units[i], '"', '"'))
            {
                GlueUntil(units, i, room, w => w.Contains('"'));
            }
            else if (OpensSpan(units[i], '[', ']'))
            {
                GlueUntil(units, i, room, w => w.Contains(']'));
            }
        }
        for (int i = 0; i + 1 < units.Count; i++)
        {
            var word = units[i];
            if (TypeWord.IsMatch(word) || word == "x"
                || (word.EndsWith(" x") && word.Length <= 3)
                || (Number.IsMatch(word) && Unit.IsMatch(units[i + 1]))
                || word == "=" || word.EndsWith(" ="))
            {
                if (Join(units, i, room))
                {
                    i--;
                }
            }
            else if (units[i + 1] == "x" && word.Length == 1)
            {
                if (Join(units, i, room))
                {
                    i--;
                }
            }
            else if (units[i + 1] == "=")
            {
                if (Join(units, i, room))
                {
                    i--;
                }
            }
        }
        return units;
    }

    static bool OpensSpan(string word, char open, char close)
    {
        if (!word.StartsWith(open) || word.Length == 0)
        {
            return false;
        }
        if (open == close)
        {
            return word.Count(c => c == open) % 2 == 1;
        }
        return !word.Contains(close);
    }

    /// Joins the units behind i onto it up to the first one that closes the
    /// span, if the whole span fits in room and closes within six words.
    static void GlueUntil(
        List<string> units, int i, int room, Func<string, bool> closes)
    {
        int end = -1;
        for (int j = i + 1; j < units.Count && j <= i + 6; j++)
        {
            if (closes(units[j]))
            {
                end = j;
                break;
            }
        }
        if (end < 0)
        {
            return;
        }
        var span = string.Join(" ", units.Skip(i).Take(end - i + 1));
        if (span.Length > room)
        {
            return;
        }
        units[i] = span;
        units.RemoveRange(i + 1, end - i);
    }

    static bool Join(List<string> units, int i, int room)
    {
        var joined = units[i] + " " + units[i + 1];
        if (joined.Length > room)
        {
            return false;
        }
        units[i] = joined;
        units.RemoveAt(i + 1);
        return true;
    }

    static List<string> FillUnits(string head, List<string> words)
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
        bool code = false;
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
            if (string.CompareOrdinal(body, i, "<c>", 0, 3) == 0)
            {
                code = true;
            }
            if (string.CompareOrdinal(body, i, "</c>", 0, 4) == 0)
            {
                code = false;
            }
            if (char.IsWhiteSpace(c) && depth == 0 && !code)
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
