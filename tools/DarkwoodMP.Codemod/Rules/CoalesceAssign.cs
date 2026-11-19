using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// The ??= sites of the coalesce rule, and the edits of every site.
sealed partial class CoalescePlanner
{
    /// "x ??= v;" becomes "if (x == null) { x = v; }": x is read once and
    /// written only when it was null, as before. Used as a value, it goes in
    /// front of its statement and x takes its place; a field or a property
    /// goes through a temporary, so it is read and written once.
    void Decide(AssignmentExpressionSyntax site, List<ExpressionSyntax> all)
    {
        var sites = new List<SyntaxNode> { site };
        var type = _types.TypeOf(site.Left);
        var refused = Shape(site, sites, all);
        if (refused == null)
        {
            refused = _types.CheckTarget(type, out bool later);
            if (later)
            {
                Later(sites, refused);
                return;
            }
        }
        if (refused == null)
        {
            refused = TargetRefusal(TernaryChain.Strip(site.Left));
        }
        if (refused != null)
        {
            Queue(sites, refused);
            return;
        }
        bool nullable = CoalesceTypes.IsNullable(type);
        var names = new CoalesceNames();
        if (site.Parent is ExpressionStatementSyntax statement)
        {
            Direct(
                sites, statement, text => text.Assign(site, names, nullable));
            return;
        }
        if (nullable)
        {
            Queue(sites, "a ??= of a Nullable used as a value, which is the "
                + "unwrapped value");
            return;
        }
        refused = WhyNotHoist(site, out var front);
        if (refused != null)
        {
            Queue(sites, refused);
            return;
        }
        var name = TernaryChain.Strip(site.Left).ToString();
        if (!IsLocal(site.Left))
        {
            names.Type = TypeSpelling.Spell(
                _model, type, front.SpanStart, out refused);
            if (names.Type == null)
            {
                Queue(sites, refused);
                return;
            }
            names.Name = Temps(site).Pick(
                new List<string> { TempNames.Named(site.Left, _model) },
                Related(site.Left, site.Left, null));
            name = names.Name;
        }
        int column = Column(front.SpanStart);
        _hoists.Add(new CoalesceHoist
        {
            Replaced = HoistPlanner.Replaced(site, null),
            Moved = site,
            Sites = sites,
            Statement = front,
            Name = name,
            Make = text => new CoalesceText(text, column)
                .Assign(site, names, false),
        });
    }

    /// The rewrite reads the target in its test and writes it in its store,
    /// so its parts run twice: they must be a name, or a member of this, of
    /// a type, or of a local or parameter.
    string TargetRefusal(ExpressionSyntax target)
    {
        if (target is MemberAccessExpressionSyntax member)
        {
            var receiver = TernaryChain.Strip(member.Expression);
            if (receiver is not ThisExpressionSyntax && !IsLocal(receiver)
                && _model.GetSymbolInfo(receiver).Symbol
                    is not INamespaceOrTypeSymbol)
            {
                return "the target's receiver would be read twice: "
                    + member.Expression;
            }
        }
        else if (target is not IdentifierNameSyntax)
        {
            return "a target whose parts would run twice: " + target;
        }
        switch (_model.GetSymbolInfo(target).Symbol)
        {
            case ILocalSymbol:
            case IParameterSymbol:
            case IFieldSymbol:
            case IEventSymbol:
                return null;
            case IPropertySymbol property when !property.IsIndexer:
                return null;
            default:
                return "a target the rule doesn't know: " + target;
        }
    }

    /// The statement becomes the new code, with the comment that was
    /// behind it on a line of its own above.
    void Direct(
        List<SyntaxNode> sites,
        StatementSyntax statement,
        Func<CoalesceText, string> make)
    {
        var comment = Behind(statement, out var span);
        var above = "";
        if (comment != null)
        {
            _plan.NoteComment(sites, "above: " + comment);
            above = comment + "\n";
        }
        int column = Column(statement.SpanStart);
        _edits.Add(new PlannedEdit
        {
            Span = span,
            Statement = statement,
            Sites = sites,
            Build = text => above + make(new CoalesceText(text, column)),
        });
    }

    /// One edit per statement with hoists in front of it, and one per hoist,
    /// whose text is the name that takes its place: NestedEdits puts the
    /// texts of the edits inside each one in place. The statement keeps the
    /// column it starts at, so the lines that continue it stay where they
    /// were.
    void AddHoistEdits()
    {
        foreach (var group in _hoists.GroupBy(h => h.Statement))
        {
            var statement = group.Key;
            var hoists = group.ToList();
            var indent = Indent(statement.SpanStart);
            _edits.Add(new PlannedEdit
            {
                Span = statement.Span,
                Statement = statement,
                Sites = new List<SyntaxNode>(),
                Build = text => string.Concat(hoists.Select(h => h.Text))
                    + indent + text.Of(statement.Span),
            });
        }
        foreach (var hoist in _hoists)
        {
            _edits.Add(new PlannedEdit
            {
                Span = hoist.Replaced.Span,
                Sites = hoist.Sites,
                Build = text =>
                {
                    hoist.Text = hoist.Make(text);
                    return hoist.Name;
                },
            });
        }
    }

    /// The comments behind the statement on its line, as written, or null;
    /// span is the statement with them.
    string Behind(StatementSyntax statement, out TextSpan span)
    {
        var last = statement.GetLastToken();
        int end = last.Span.End;
        foreach (var trivia in last.TrailingTrivia)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                break;
            }
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                end = trivia.Span.End;
            }
        }
        span = TextSpan.FromBounds(statement.SpanStart, end);
        if (end == last.Span.End)
        {
            return null;
        }
        var between = TextSpan.FromBounds(last.Span.End, end);
        return _plan.Tree.GetText().ToString(between).Trim();
    }

    /// How far into its line position is.
    int Column(int position)
    {
        var line = _plan.Tree.GetText().Lines.GetLineFromPosition(position);
        return position - line.Start;
    }

    /// The spaces in front of position on its line, or nothing when other
    /// text is there.
    string Indent(int position)
    {
        var text = _plan.Tree.GetText();
        var line = text.Lines.GetLineFromPosition(position);
        var before = text.ToString(TextSpan.FromBounds(line.Start, position));
        if (before.Trim().Length > 0)
        {
            return "";
        }
        return before;
    }
}
