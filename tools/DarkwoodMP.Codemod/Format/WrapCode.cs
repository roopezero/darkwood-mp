using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Format;

/// The width rules for a line of code (codemod.md section 7.6), tried in
/// the order the README's "The wrap stage" lists them until one finds a
/// break: an initializer that crosses the limit is opened; a ||, && or ??
/// outside any bracket breaks; a chain of calls breaks before a dot; an
/// argument or parameter list breaks after its "(" and is filled; a list
/// over several lines breaks after a comma; any other binary operator, "="
/// or "=>", or "." of a call breaks; a plain string constant is split (see
/// WrapStrings); and last, a break past the limit so the next pass has a
/// shorter line. No break is made inside an interpolated string or inside
/// an initializer that starts on the line, because the Formatter lays those
/// out its own way.
static class WrapCode
{
    static readonly SyntaxKind[][] Loosest =
    {
        new[] { SyntaxKind.BarBarToken },
        new[] { SyntaxKind.AmpersandAmpersandToken },
        new[] { SyntaxKind.QuestionQuestionToken },
        new[] { SyntaxKind.BarToken },
        new[] { SyntaxKind.CaretToken },
        new[] { SyntaxKind.AmpersandToken },
        new[]
        {
            SyntaxKind.EqualsEqualsToken,
            SyntaxKind.ExclamationEqualsToken,
        },
        new[]
        {
            SyntaxKind.LessThanToken,
            SyntaxKind.GreaterThanToken,
            SyntaxKind.LessThanEqualsToken,
            SyntaxKind.GreaterThanEqualsToken,
        },
        new[] { SyntaxKind.PlusToken, SyntaxKind.MinusToken },
    };

    /// The operators the first pass breaks at, before any argument list:
    /// the ones that join whole conditions or fall back to a default.
    static readonly SyntaxKind[] Joining =
    {
        SyntaxKind.BarBarToken,
        SyntaxKind.AmpersandAmpersandToken,
        SyntaxKind.QuestionQuestionToken,
    };

    public static LinePlan Plan(WrapLine line)
    {
        var tokens = line.Tokens();
        var open = OpenInitializer(line);
        if (open != null)
        {
            return open;
        }
        var free = tokens.Where(t => !InInitializer(line, t)).ToList();
        var plan = BreakBeforeOperator(line, free, outside: true);
        if (plan != null)
        {
            return plan;
        }
        if (Chained(line, free).Count >= 2)
        {
            plan = BreakChain(line, free);
            if (plan != null)
            {
                return plan;
            }
        }
        plan = BreakArguments(line, free);
        if (plan != null)
        {
            return plan;
        }
        plan = BreakOpenList(line, free);
        if (plan != null)
        {
            return plan;
        }
        plan = BreakBeforeOperator(line, free, outside: false);
        if (plan != null)
        {
            return plan;
        }
        plan = BreakAfterEquals(line, free);
        if (plan != null)
        {
            return plan;
        }
        plan = BreakChain(line, free);
        if (plan != null)
        {
            return plan;
        }
        var split = WrapStrings.Plan(line);
        if (split.Edits.Count > 0)
        {
            return split;
        }
        plan = BreakPastLimit(line, free);
        if (plan != null)
        {
            return plan;
        }
        plan = BreakAfterLongHead(line, free);
        if (plan != null)
        {
            return plan;
        }
        return split;
    }

    /// The outermost initializer, anonymous object or array on this line that
    /// crosses the limit, opened: its braces on lines of their own and one
    /// member per line, the form the Formatter keeps.
    static LinePlan OpenInitializer(WrapLine line)
    {
        var limit = line.Start + Wrap.Max;
        foreach (var node in line.Root.DescendantNodes(line.Line.Span))
        {
            if (!TryBraces(node, out var open, out var close, out var items))
            {
                continue;
            }
            if (open.SpanStart < line.Start || close.Span.End > line.End
                || line.GluedEnd(close) <= limit || items.Count == 0
                || WrapLine.InInterpolation(open))
            {
                continue;
            }
            var before = open.GetPreviousToken();
            var edits = new List<TextChange>();
            var pad = "\n" + line.Pad;
            if (before.Span.End >= line.Start + line.Indent
                && line.OnlySpaceBetween(before, open))
            {
                edits.Add(new TextChange(
                    TextSpan.FromBounds(before.Span.End, open.SpanStart), pad));
            }
            var tokens = new List<SyntaxToken> { open };
            foreach (var item in items)
            {
                if (item.IsToken)
                {
                    tokens.Add(item.AsToken());
                }
            }
            foreach (var token in tokens)
            {
                var next = token.GetNextToken();
                if (next == close)
                {
                    break;
                }
                if (!line.OnlySpaceBetween(token, next))
                {
                    return null;
                }
                edits.Add(line.BreakBetween(token, next));
            }
            var last = close.GetPreviousToken();
            if (!line.OnlySpaceBetween(last, close))
            {
                return null;
            }
            edits.Add(new TextChange(
                TextSpan.FromBounds(last.Span.End, close.SpanStart), pad));
            return line.Break("initializer opened", edits);
        }
        return null;
    }

    static bool TryBraces(
        SyntaxNode node,
        out SyntaxToken open,
        out SyntaxToken close,
        out List<SyntaxNodeOrToken> items)
    {
        switch (node)
        {
            case InitializerExpressionSyntax init:
                open = init.OpenBraceToken;
                close = init.CloseBraceToken;
                items = init.Expressions.GetWithSeparators().ToList();
                return true;
            case AnonymousObjectCreationExpressionSyntax anon:
                open = anon.OpenBraceToken;
                close = anon.CloseBraceToken;
                items = anon.Initializers.GetWithSeparators().ToList();
                return true;
            default:
                open = default;
                close = default;
                items = null;
                return false;
        }
    }

    /// True when the token is inside braces of an initializer that opens on
    /// this line.
    static bool InInitializer(WrapLine line, SyntaxToken token)
    {
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            if (TryBraces(node, out var open, out _, out _)
                && open.SpanStart >= line.Start && token != open)
            {
                return true;
            }
            if (node is StatementSyntax || node is MemberDeclarationSyntax)
            {
                return false;
            }
        }
        return false;
    }

    /// Breaks before the loosest binary operator that fits. With outside, only
    /// ||, && and ?? that are in no argument list opened on this line.
    static LinePlan BreakBeforeOperator(
        WrapLine line, List<SyntaxToken> tokens, bool outside)
    {
        var inner = line.Indent + Wrap.Step;
        var candidates = tokens
            .Where(t => t.Parent is BinaryExpressionSyntax)
            .Where(t => line.Col(t.SpanStart) > inner)
            .Where(t => line.Col(t.SpanStart) < Wrap.Max)
            .Where(t => line.OnlySpaceBetween(t.GetPreviousToken(), t))
            .ToList();
        if (outside)
        {
            candidates = candidates
                .Where(t => Joining.Any(k => t.IsKind(k)))
                .Where(t => ListDepth(line, t) == 0)
                .ToList();
        }
        if (candidates.Count == 0)
        {
            return null;
        }
        int depth = candidates.Min(t => ListDepth(line, t));
        candidates = candidates
            .Where(t => ListDepth(line, t) == depth)
            .ToList();
        foreach (var kinds in Loosest)
        {
            var op = candidates.LastOrDefault(t => kinds.Any(k => t.IsKind(k)));
            if (op != default)
            {
                return line.Break(
                    "broken before a binary operator",
                    line.BreakBefore(op));
            }
        }
        return null;
    }

    /// How many brackets opened on this line the token sits in: argument
    /// lists, parameter lists and parentheses.
    static int ListDepth(WrapLine line, SyntaxToken token)
    {
        int depth = 0;
        for (var node = token.Parent; node != null; node = node.Parent)
        {
            if (node is StatementSyntax || node is MemberDeclarationSyntax)
            {
                break;
            }
            if (node.SpanStart < line.Start)
            {
                break;
            }
            if (node is ArgumentListSyntax
                || node is BracketedArgumentListSyntax
                || node is ParenthesizedExpressionSyntax)
            {
                depth++;
            }
        }
        return depth;
    }

    /// The first argument or parameter list on the line that crosses the
    /// limit: a break after "(", then the items filled greedily one level
    /// in, with ")" behind the last one.
    static LinePlan BreakArguments(WrapLine line, List<SyntaxToken> tokens)
    {
        foreach (var open in tokens)
        {
            if (!ListOf(open, out var close, out var items))
            {
                continue;
            }
            if (items.Count > 0 && close.SpanStart > line.End
                && line.Col(open.Span.End) < Wrap.Max)
            {
                var opened = OpenList(line, open, items);
                if (opened != null)
                {
                    return opened;
                }
                continue;
            }
            if (items.Count == 0 || close.Span.End > line.End
                || line.Col(open.Span.End) >= Wrap.Max
                || line.Col(line.GluedEnd(close)) <= Wrap.Max)
            {
                continue;
            }
            var first = items[0].AsNode().GetFirstToken();
            if (!line.OnlySpaceBetween(open, first))
            {
                continue;
            }
            var edits = new List<TextChange>
            {
                line.BreakBetween(open, first),
            };
            int inner = line.Indent + Wrap.Step;
            int col = inner;
            bool fits = true;
            for (int i = 0; i < items.Count; i += 2)
            {
                int width = items[i].Span.Length;
                int comma = 0;
                if (i + 1 < items.Count)
                {
                    comma = 1;
                }
                else
                {
                    comma = line.GluedEnd(close) - items[i].Span.End;
                }
                if (i > 0 && col + 1 + width + comma > Wrap.Max)
                {
                    var separator = items[i - 1].AsToken();
                    var next = items[i].AsNode().GetFirstToken();
                    if (!line.OnlySpaceBetween(separator, next))
                    {
                        fits = false;
                        break;
                    }
                    edits.Add(line.BreakBetween(separator, next));
                    col = inner;
                }
                else if (i > 0)
                {
                    col += 1;
                }
                col += width + comma;
            }
            if (fits)
            {
                return line.Break("broken after (", edits);
            }
        }
        return null;
    }

    /// A list that opens on this line and closes on a later one: a break
    /// after "(", and every later item that starts a line moved to the same
    /// column, one level in from this line. Lists holding a block (a lambda
    /// with braces, an initializer over several lines) are left alone, since
    /// the Formatter lines those up with their own first line.
    static LinePlan OpenList(
        WrapLine line, SyntaxToken open, List<SyntaxNodeOrToken> items)
    {
        var first = items[0].AsNode().GetFirstToken();
        if (first.SpanStart > line.End || !line.OnlySpaceBetween(open, first))
        {
            return null;
        }
        var list = open.Parent;
        if (list.DescendantNodes().Any(n => n is BlockSyntax
            || n is InitializerExpressionSyntax
            || n is AnonymousObjectCreationExpressionSyntax
            || n is SwitchExpressionSyntax))
        {
            return null;
        }
        var edits = new List<TextChange> { line.BreakBetween(open, first) };
        int column = line.Indent + Wrap.Step;
        for (int i = 2; i < items.Count; i += 2)
        {
            if (!Shift(line, items[i].AsNode(), column, edits))
            {
                return null;
            }
        }
        return line.Break("a list over several lines broken after (", edits);
    }

    /// Moves an item that starts a line to column, and every other line it
    /// covers by the same amount, so its own continuation lines keep their
    /// place under it. False when a line of it starts inside a string.
    static bool Shift(
        WrapLine line, SyntaxNode item, int column, List<TextChange> edits)
    {
        var lines = line.Source.Lines;
        var start = item.GetFirstToken().SpanStart;
        var head = lines.GetLineFromPosition(start);
        int lead = start - head.Start;
        if (head.Start <= line.Start
            || line.Source.ToString(TextSpan.FromBounds(head.Start, start))
                .Trim().Length > 0)
        {
            return true;
        }
        int delta = column - lead;
        if (delta == 0)
        {
            return true;
        }
        int last = lines.GetLineFromPosition(item.Span.End).LineNumber;
        for (int n = head.LineNumber; n <= last; n++)
        {
            var text = lines[n];
            var token = line.Root.FindToken(text.Start);
            if (token.SpanStart < text.Start && token.Span.End > text.Start)
            {
                return false;
            }
            var content = text.ToString();
            int spaces = content.Length - content.TrimStart(' ').Length;
            if (content.Trim().Length == 0)
            {
                continue;
            }
            int moved = Math.Max(0, spaces + delta);
            edits.Add(new TextChange(
                new TextSpan(text.Start, spaces), new string(' ', moved)));
        }
        return true;
    }

    static bool ListOf(
        SyntaxToken open,
        out SyntaxToken close,
        out List<SyntaxNodeOrToken> items)
    {
        close = default;
        items = null;
        if (!open.IsKind(SyntaxKind.OpenParenToken)
            && !open.IsKind(SyntaxKind.OpenBracketToken))
        {
            return false;
        }
        switch (open.Parent)
        {
            case ArgumentListSyntax a:
                close = a.CloseParenToken;
                items = a.Arguments.GetWithSeparators().ToList();
                return true;
            case BracketedArgumentListSyntax b:
                close = b.CloseBracketToken;
                items = b.Arguments.GetWithSeparators().ToList();
                return true;
            case ParameterListSyntax p:
                close = p.CloseParenToken;
                items = p.Parameters.GetWithSeparators().ToList();
                return true;
            case AttributeArgumentListSyntax aa:
                close = aa.CloseParenToken;
                items = aa.Arguments.GetWithSeparators().ToList();
                return true;
            default:
                return false;
        }
    }

    /// Breaks after the last "=" or "=>" that fits, with code after it on
    /// the line.
    static LinePlan BreakAfterEquals(WrapLine line, List<SyntaxToken> tokens)
    {
        var inner = line.Indent + Wrap.Step;
        var equals = tokens.LastOrDefault(t => IsEquals(t)
            && line.Col(t.Span.End) < Wrap.Max
            && line.Col(t.SpanStart) > inner
            && t.GetNextToken().Span.End <= line.End
            && line.OnlySpaceBetween(t, t.GetNextToken()));
        if (equals == default)
        {
            return null;
        }
        return line.Break(
            "broken after = or =>",
            line.BreakBetween(equals, equals.GetNextToken()));
    }

    /// When no break fits, the first ||, && or ?? at the outermost level
    /// past the limit, so the next pass can work on a shorter line.
    static LinePlan BreakPastLimit(WrapLine line, List<SyntaxToken> tokens)
    {
        var inner = line.Indent + Wrap.Step;
        var candidates = tokens
            .Where(t => t.Parent is BinaryExpressionSyntax)
            .Where(t => Joining.Any(k => t.IsKind(k)))
            .Where(t => line.Col(t.SpanStart) > inner)
            .Where(t => line.OnlySpaceBetween(t.GetPreviousToken(), t))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        int depth = candidates.Min(t => ListDepth(line, t));
        candidates = candidates
            .Where(t => ListDepth(line, t) == depth)
            .ToList();
        foreach (var kinds in Loosest)
        {
            var op = candidates
                .FirstOrDefault(t => kinds.Any(k => t.IsKind(k)));
            if (op != default)
            {
                return line.Break(
                    "broken before a binary operator past the limit",
                    line.BreakBefore(op));
            }
        }
        return null;
    }

    /// The last resort for a declaration whose head alone is too long: a
    /// break after its "=" or "=>" past the limit, so at least the value
    /// gets a line of its own. The head stays long, for a person.
    static LinePlan BreakAfterLongHead(WrapLine line, List<SyntaxToken> tokens)
    {
        var inner = line.Indent + Wrap.Step;
        var equals = tokens.FirstOrDefault(t => IsEquals(t)
            && line.Col(t.SpanStart) > inner
            && t.GetNextToken().Span.End <= line.End
            && line.OnlySpaceBetween(t, t.GetNextToken()));
        if (equals == default)
        {
            return null;
        }
        return line.Break(
            "broken after = or => past the limit",
            line.BreakBetween(equals, equals.GetNextToken()));
    }

    /// A list that started on an earlier line or goes on past this one: a
    /// break after the last of its commas on the line that fits, taking the
    /// outermost such list.
    static LinePlan BreakOpenList(WrapLine line, List<SyntaxToken> tokens)
    {
        var commas = tokens
            .Where(t => t.IsKind(SyntaxKind.CommaToken))
            .Where(t => IsList(t.Parent))
            .Where(t => t.Parent.SpanStart < line.Start
                || t.Parent.Span.End > line.End)
            .Where(t => line.Col(t.Span.End) <= Wrap.Max)
            .Where(t => t.GetNextToken().Span.End <= line.End)
            .Where(t => line.OnlySpaceBetween(t, t.GetNextToken()))
            .ToList();
        if (commas.Count == 0)
        {
            return null;
        }
        int depth = commas.Min(t => ListDepth(line, t));
        var comma = commas.Last(t => ListDepth(line, t) == depth);
        var next = comma.GetNextToken();
        var pad = line.Inner;
        var column = ItemColumn(line, comma.Parent, comma);
        if (column >= 0 && column < line.Col(next.SpanStart))
        {
            pad = "\n" + new string(' ', column);
        }
        return line.Break(
            "a list broken after a comma",
            new TextChange(
                TextSpan.FromBounds(comma.Span.End, next.SpanStart), pad));
    }

    /// The column of the last item before the comma that starts a line of
    /// its own, or -1 when none does.
    static int ItemColumn(WrapLine line, SyntaxNode list, SyntaxToken comma)
    {
        int column = -1;
        foreach (var child in list.ChildNodes())
        {
            if (child.SpanStart >= comma.SpanStart)
            {
                break;
            }
            var first = child.GetFirstToken();
            var before = first.GetPreviousToken();
            var lines = line.Source.Lines;
            var at = lines.GetLineFromPosition(first.SpanStart);
            if (lines.GetLineFromPosition(before.Span.End).LineNumber
                != at.LineNumber)
            {
                column = first.SpanStart - at.Start;
            }
        }
        return column;
    }

    static bool IsList(SyntaxNode node)
    {
        return node is ArgumentListSyntax
            || node is ParameterListSyntax
            || node is BracketedArgumentListSyntax
            || node is AttributeArgumentListSyntax
            || node is InitializerExpressionSyntax
            || node is VariableDeclarationSyntax;
    }

    static bool IsEquals(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.EqualsGreaterThanToken))
        {
            return true;
        }
        if (token.Parent is EqualsValueClauseSyntax clause)
        {
            return token == clause.EqualsToken;
        }
        if (token.Parent is AssignmentExpressionSyntax assignment)
        {
            return token == assignment.OperatorToken;
        }
        return false;
    }

    /// The dots on the line that call a method on what a call or an index
    /// returned: the links of a chain such as a.Where(...).Select(...).
    static List<SyntaxToken> Chained(WrapLine line, List<SyntaxToken> tokens)
    {
        return tokens
            .Where(t => t.IsKind(SyntaxKind.DotToken))
            .Where(t => t.Parent is MemberAccessExpressionSyntax m
                && m.Parent is InvocationExpressionSyntax
                && m.Expression is InvocationExpressionSyntax
                    or ElementAccessExpressionSyntax)
            .ToList();
    }

    /// Breaks before the last "." of a call chain that fits: first a dot
    /// behind a call or an index (a real chain), then any dot of a call.
    static LinePlan BreakChain(WrapLine line, List<SyntaxToken> tokens)
    {
        var inner = line.Indent + Wrap.Step;
        var dots = tokens
            .Where(t => t.IsKind(SyntaxKind.DotToken))
            .Where(t => t.Parent is MemberAccessExpressionSyntax m
                && m.Parent is InvocationExpressionSyntax)
            .Where(t => line.Col(t.SpanStart) > inner)
            .Where(t => line.Col(t.SpanStart) < Wrap.Max)
            .Where(t => line.OnlySpaceBetween(t.GetPreviousToken(), t))
            .ToList();
        var chained = dots.LastOrDefault(t =>
            ((MemberAccessExpressionSyntax)t.Parent).Expression
                is InvocationExpressionSyntax or ElementAccessExpressionSyntax);
        var dot = chained;
        if (dot == default)
        {
            dot = dots.LastOrDefault();
        }
        if (dot == default)
        {
            return null;
        }
        return line.Break(
            "broken before a member call",
            line.BreakBetween(dot.GetPreviousToken(), dot));
    }
}
