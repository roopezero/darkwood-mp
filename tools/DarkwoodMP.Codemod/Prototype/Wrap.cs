using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Prototype;

/// Breaks lines over 80 columns by changing whitespace only (plus splitting plain constant strings,
/// which the compiler folds back). Each pass plans one break per long line; passes repeat to a fixed point.
static class Wrap
{
    const int Max = 80;
    const int Step = 4;

    public static readonly Dictionary<string, int> Stats = new();
    static void Count(string k) { Stats.TryGetValue(k, out var v); Stats[k] = v + 1; }

    public static string Run(string text, CSharpParseOptions opts)
    {
        for (int pass = 0; pass < 30; pass++)
        {
            var tree = CSharpSyntaxTree.ParseText(SourceText.From(text, System.Text.Encoding.UTF8), opts);
            var root = tree.GetRoot();
            var src = tree.GetText();
            var edits = new List<TextChange>();
            foreach (var line in src.Lines)
            {
                if (line.Span.Length > Max)
                {
                    edits.AddRange(Plan(root, src, line));
                }
            }
            if (edits.Count == 0)
            {
                break;
            }
            text = src.WithChanges(edits).ToString();
        }
        return text;
    }

    static IEnumerable<TextChange> Plan(SyntaxNode root, SourceText src, TextLine line)
    {
        var s = line.ToString();
        int indent = s.Length - s.TrimStart().Length;
        string pad = new string(' ', indent);
        var body = s.TrimStart();

        // 1. A line that is only a comment: wrap at the last space that fits.
        if (body.StartsWith("//"))
        {
            string prefix = body.StartsWith("///") ? "/// " : "// ";
            int at = s.LastIndexOf(' ', Max);
            int min = indent + prefix.Length;
            if (at > min)
            {
                Count("comment wrapped");
                return new[] { new TextChange(new TextSpan(line.Start + at, 1), "\n" + pad + prefix) };
            }
            
            return Array.Empty<TextChange>();
        }

        // 2. A trailing comment moves to its own line above the code.
        var trivia = root.FindTrivia(line.End - 1);
        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && trivia.Span.End == line.End)
        {
            var token = root.FindToken(trivia.SpanStart, findInsideTrivia: false);
            int codeEnd = trivia.SpanStart;
            while (codeEnd > line.Start && src[codeEnd - 1] == ' ')
            {
                codeEnd--;
            }
            Count("trailing comment moved above");
            return new[]
            {
                new TextChange(new TextSpan(line.Start, 0), pad + trivia.ToString() + "\n"),
                new TextChange(TextSpan.FromBounds(codeEnd, line.End), ""),
            };
        }

        var toks = root.DescendantTokens(line.Span)
            .Where(t => t.SpanStart >= line.Start && t.Span.End <= line.End)
            .Where(t => t.Parent?.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>() == null)
            .ToList();
        int Col(int pos) => pos - line.Start;
        int inner = indent + Step;
        string nl = "\n" + new string(' ', inner);

        // 3. A long call or signature: break after "(" and fill the arguments, one level in.
        foreach (var t in toks.Where(x => x.IsKind(SyntaxKind.OpenParenToken)))
        {
            SyntaxToken close;
            List<SyntaxNodeOrToken> items;
            switch (t.Parent)
            {
                case ArgumentListSyntax a: close = a.CloseParenToken; items = a.Arguments.GetWithSeparators().ToList(); break;
                case ParameterListSyntax p: close = p.CloseParenToken; items = p.Parameters.GetWithSeparators().ToList(); break;
                case AttributeArgumentListSyntax aa: close = aa.CloseParenToken; items = aa.Arguments.GetWithSeparators().ToList(); break;
                default: continue;
            }
            if (items.Count == 0 || Col(t.Span.End) >= Max || close.Span.End > line.End || t.Span.End == close.SpanStart)
            {
                continue;
            }
            if (Col(close.Span.End) <= Max && line.End - close.Span.End > 0 && Col(close.Span.End) <= Max && !RestOverflows(toks, close, line))
            {
                continue;
            }
            // Fill greedily: a comma that would push the next item past the limit becomes a line break.
            var edits = new List<TextChange> { new TextChange(TextSpan.FromBounds(t.Span.End, items[0].SpanStart), nl) };
            int col = inner;
            for (int i = 0; i < items.Count; i += 2)
            {
                int width = items[i].Span.Length;
                int comma = i + 1 < items.Count ? 1 : 0;
                if (i > 0 && col + 1 + width + comma > Max && col > inner)
                {
                    edits.Add(new TextChange(TextSpan.FromBounds(items[i - 1].Span.End, items[i].SpanStart), nl));
                    col = inner;
                }
                else if (i > 0)
                {
                    col += 1;
                }
                col += width + comma;
            }
            Count("broken after (");
            return edits;
        }

        // 4. Binary operators, the loosest one first: break before the operator.
        foreach (var kinds in new[] { new[] { SyntaxKind.BarBarToken }, new[] { SyntaxKind.AmpersandAmpersandToken }, new[] { SyntaxKind.QuestionQuestionToken }, new[] { SyntaxKind.PlusToken, SyntaxKind.MinusToken } })
        {
            var ops = toks.Where(x => kinds.Any(k => x.IsKind(k)) && x.Parent is BinaryExpressionSyntax
                && Col(x.SpanStart) > inner && Col(x.SpanStart) < Max).ToList();
            if (ops.Count > 0)
            {
                var op = ops.Last();
                Count("broken before a binary operator");
                return new[] { new TextChange(TextSpan.FromBounds(op.GetPreviousToken().Span.End, op.SpanStart), nl) };
            }
        }

        // 5. After "=" or "=>": what follows goes one level in.
        foreach (var kind in new[] { SyntaxKind.EqualsToken, SyntaxKind.EqualsGreaterThanToken })
        {
            var eq = toks.LastOrDefault(x => x.IsKind(kind) && Col(x.Span.End) < Max && Col(x.SpanStart) > inner);
            if (eq != default)
            {
                var next = eq.GetNextToken();
                if (next.SpanStart <= line.End)
                {
                    Count("broken after = or =>");
                    return new[] { new TextChange(TextSpan.FromBounds(eq.Span.End, next.SpanStart), nl) };
                }
            }
        }

        // 6. A chain of member calls: break before the last dot that fits.
        var dot = toks.LastOrDefault(x => x.IsKind(SyntaxKind.DotToken) && x.Parent is MemberAccessExpressionSyntax m
            && m.Parent is InvocationExpressionSyntax && Col(x.SpanStart) > inner && Col(x.SpanStart) < Max);
        if (dot != default)
        {
            Count("broken before a member call");
            return new[] { new TextChange(TextSpan.FromBounds(dot.GetPreviousToken().Span.End, dot.SpanStart), nl) };
        }

        // 7. A plain string constant that crosses the limit: split it at a space (the compiler folds "a" + "b").
        var str = root.FindToken(line.Start + Max);
        if (Environment.GetEnvironmentVariable("WRAP_NOSPLIT") == "1") { return Array.Empty<TextChange>(); }
        bool receiver = str.Parent?.Parent is MemberAccessExpressionSyntax;
        if (str.IsKind(SyntaxKind.StringLiteralToken) && !receiver && !str.Text.StartsWith("@") && !str.Text.StartsWith("\"\"\""))
        {
            string raw = str.Text;
            int limit = Max - Col(str.SpanStart) - 1;
            int cut = limit > 1 && limit < raw.Length ? raw.LastIndexOf(' ', limit) : -1;
            while (cut > 0 && raw[cut - 1] == '\\')
            {
                cut = raw.LastIndexOf(' ', cut - 1);
            }
            if (cut > 1)
            {
                Count("string constant split");
                return new[] { new TextChange(new TextSpan(str.SpanStart + cut + 1, 0), "\"" + nl + "+ \"") };
            }
        }
        var over = root.FindToken(line.Start + Max);
        if (over.IsKind(SyntaxKind.InterpolatedStringTextToken) || over.Parent is InterpolationSyntax || over.Parent?.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>() != null)
        {
            
        }
        else if (over.IsKind(SyntaxKind.StringLiteralToken))
        {
            
        }
        else
        {
            
        }
        return Array.Empty<TextChange>();
    }

    public static void Finish(string text, CSharpParseOptions opts)
    {
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(text, System.Text.Encoding.UTF8), opts);
        var root = tree.GetRoot();
        foreach (var line in tree.GetText().Lines)
        {
            if (line.Span.Length <= Max) { continue; }
            var s = line.ToString().TrimStart();
            var over = root.FindToken(line.Start + Max);
            string why;
            if (s.StartsWith("//")) { why = "comment with no space to break at"; }
            else if (over.Parent?.FirstAncestorOrSelf<InterpolatedStringExpressionSyntax>() != null) { why = "interpolated string"; }
            else if (over.IsKind(SyntaxKind.StringLiteralToken)) { why = "plain string (receiver, verbatim, or no space)"; }
            else { why = "other code"; }
            Left.TryGetValue(why, out var v); Left[why] = v + 1;
            if (why == "other code" && Samples.Count < 25) { Samples.Add(line.ToString().Trim()); }
        }
    }
    public static readonly Dictionary<string, int> Left = new();
    public static readonly List<string> Samples = new();

    static bool RestOverflows(List<SyntaxToken> toks, SyntaxToken close, TextLine line) =>
        // The list ends before the limit, so some later part of the line is the one over: not this list.
        false;
}
