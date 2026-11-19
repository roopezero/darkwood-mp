using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Stage 6 (guide section 4.3, codemod.md section 7.3). A ?. chain whose
/// receivers are not Unity objects becomes ifs (AccessText):
///
/// - "x?.F(a);" becomes "if (x != null) { x.F(a); }", a declaration
///   "T n = null; if (x != null) { n = x.F; }", an assignment the same
///   with an else that stores null, a return "if (x != null) { return
///   x.F; } return null;", and an expression body gets a block first
///   (AccessForms);
/// - a comparison keeps its place as one of the fixed translations, such
///   as "x != null && x.F == v", when the compared value has no effects
///   and isn't null by surprise (AccessCompare);
/// - anywhere else the chain moves into a temporary of its natural type in
///   front of its statement, when the hazard rules of guide section 4.6
///   prove that nothing can notice (HoistPath, HoistHazards).
///
/// Every value a ?. tests is read once: a receiver that isn't a local, a
/// parameter or this goes into a temporary first, because Net/ runs on
/// several threads, and so does the value each inner ?. of a chain tests.
/// A delegate is called through its temporary: "handler(a)". A chain that
/// tests a Unity object is left whole for stage 7.
sealed class ConditionalAccessRule : Rule
{
    public override string Name => "conditional-access";

    public override void Plan(RulePlan plan)
    {
        new AccessPlanner(plan).Run();
    }
}

/// A value that moves into a temporary in front of its statement: a whole
/// chain, or the receiver of a comparison that stays where it is. Text is
/// made when the edit that replaces it is built.
sealed class AccessHoist
{
    /// The statement the temporary goes in front of, or the expression body
    /// that becomes a block with the temporary in front of its value.
    public SyntaxNode Place;
    public ExpressionSyntax Moved;
    public List<SyntaxNode> Sites;
    public string Text = "";
}

/// The plan of the conditional-access rule for one file. Sites come in
/// document order, so the sites of one statement come in the order they
/// run.
sealed partial class AccessPlanner
{
    const string InReceiver =
        "inside the receiver of another ?.: rewrite one level per run";

    const string HoldsInReceiver =
        "holds another ?. in its receiver: rewrite one level per run";

    const string InPart =
        "inside the part after another ?., which runs only when its "
            + "receiver isn't null: rewrite one level per run";

    const string HoldsInPart =
        "holds another ?. in the part after its own, which runs only "
            + "when the receiver isn't null: rewrite one level per run";

    const string Declares =
        "the ?. declares a variable, whose scope the rewrite would change";

    const string InHole =
        "inside an interpolation hole, where no statement can go";

    const string Awaits =
        "an await in the statement: a temporary could become a field of "
            + "the state machine";

    readonly RulePlan _plan;
    readonly SemanticModel _model;
    readonly AccessTypes _types;
    readonly HoistHazards _hazards;
    readonly List<PlannedEdit> _edits = new();
    readonly List<AccessHoist> _hoists = new();
    List<ConditionalAccessExpressionSyntax> _all;

    public AccessPlanner(RulePlan plan)
    {
        _plan = plan;
        _model = plan.Model;
        _types = new AccessTypes(plan.Model);
        _hazards = new HoistHazards(plan.Model, plan.Purity);
    }

    public void Run()
    {
        _all = _plan.Root.DescendantNodes()
            .OfType<ConditionalAccessExpressionSyntax>()
            .Where(node => OperatorCount.KindOf(node) != null)
            .ToList();
        foreach (var site in _all)
        {
            if (_plan.Receivers == ReceiverFilter.Unity)
            {
                _plan.Later(site, "--receivers unity: this rule rewrites "
                    + "no Unity object");
                continue;
            }
            Decide(new AccessChain(site));
        }
        AddHoistEdits();
        NestedEdits.Propose(_plan, _edits);
    }

    void Decide(AccessChain chain)
    {
        var refused = _types.Check(chain, out bool later);
        if (later)
        {
            _plan.Later(chain.Top, refused);
            return;
        }
        if (refused == null)
        {
            refused = Shape(chain);
        }
        if (refused != null)
        {
            Queue(chain, refused);
            return;
        }
        var outer = TernaryChain.Outer(chain.Top);
        if (Statement(chain, outer)
            || Compare(chain, outer)
            || Declaration(chain, outer)
            || Assignment(chain, outer)
            || Return(chain, outer)
            || Body(chain, outer))
        {
            return;
        }
        Hoist(chain);
    }

    /// What the text of any site must allow: no other site inside it or
    /// around it, no variable declared in it, no directive in it.
    string Shape(AccessChain chain)
    {
        var top = chain.Top;
        foreach (var other in _all)
        {
            if (other == top)
            {
                continue;
            }
            if (other.Span.Contains(top.Span))
            {
                if (other.Expression.Span.Contains(top.Span))
                {
                    return InReceiver;
                }
                return InPart;
            }
            if (top.Span.Contains(other.Span))
            {
                if (top.Expression.Span.Contains(other.Span))
                {
                    return HoldsInReceiver;
                }
                return HoldsInPart;
            }
        }
        if (HoistPlanner.Declares(top))
        {
            return Declares;
        }
        foreach (var trivia in _plan.Root.DescendantTrivia(top.Span))
        {
            if ((trivia.IsDirective
                    || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                && top.Span.Contains(trivia.Span))
            {
                return "a preprocessor directive inside the ?.";
            }
        }
        return null;
    }

    /// Why a rewrite that adds statements in front of or in place of this
    /// statement can't be made, or null.
    string WhyNotStatements(AccessChain chain, StatementSyntax statement)
    {
        if (chain.Top.Ancestors().Any(node => node is InterpolationSyntax))
        {
            return InHole;
        }
        if (HoistPlanner.AwaitsIn(statement))
        {
            return Awaits;
        }
        return null;
    }

    /// The receiver's temporary, when it needs one: the first the chain
    /// declares. Null after the chain went to the queue, when its type
    /// can't be written at position.
    AccessNames ReceiverNames(AccessChain chain, int position)
    {
        var names = new AccessNames();
        if (!NeedsTemporary(chain.Receiver))
        {
            return names;
        }
        var type = Spell(chain, _types.TypeOf(chain.Receiver), position);
        if (type == null)
        {
            return null;
        }
        names.ReceiverType = type;
        var hints = ReceiverHints(chain);
        names.Receiver = Temps(chain).Pick(hints, hints);
        return names;
    }

    /// The temporary of the value each inner ?. tests, named after its
    /// step, after the temporaries declared in front of them. False after
    /// the chain went to the queue.
    bool StepNames(AccessChain chain, AccessNames names, int position)
    {
        for (int i = 0; i < chain.Steps.Count - 1; i++)
        {
            var step = chain.Steps[i];
            var type = Spell(chain, _types.TypeOf(step), position);
            if (type == null)
            {
                return false;
            }
            var hints = new List<string> { TempNames.Named(step, _model) };
            names.StepTypes.Add(type);
            names.Steps.Add(Temps(chain).Pick(hints, hints));
        }
        return true;
    }

    /// Both, for a rewrite that declares no temporary of its own.
    AccessNames Names(AccessChain chain, int position)
    {
        var names = ReceiverNames(chain, position);
        if (names == null || !StepNames(chain, names, position))
        {
            return null;
        }
        return names;
    }

    /// "handler" for a delegate, as in Microsoft's thread-safe example;
    /// otherwise the receiver's last identifier ("X.Instance" gives
    /// "instance").
    List<string> ReceiverHints(AccessChain chain)
    {
        var hints = new List<string>();
        var type = _types.TypeOf(chain.Receiver);
        if (type?.TypeKind == TypeKind.Delegate)
        {
            hints.Add("handler");
        }
        var bare = TernaryChain.Strip(chain.Receiver);
        if (bare is BinaryExpressionSyntax cast
            && cast.IsKind(SyntaxKind.AsExpression))
        {
            hints.Add(TypeHint(cast.Right));
        }
        hints.Add(TempNames.Named(bare, _model));
        return hints;
    }

    /// "x as IGuiSection" gives "guiSection".
    static string TypeHint(ExpressionSyntax type)
    {
        var name = type.ToString();
        int dot = name.LastIndexOf('.');
        name = name.Substring(dot + 1);
        int angle = name.IndexOf('<');
        if (angle >= 0)
        {
            name = name.Substring(0, angle);
        }
        if (name.Length > 2 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name.Substring(1);
        }
        if (name.Length == 0)
        {
            return "value";
        }
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// A receiver read twice must be a local or a parameter that isn't a
    /// ref, or this: anything else can change between the reads.
    bool NeedsTemporary(ExpressionSyntax receiver)
    {
        var bare = TernaryChain.Strip(receiver);
        if (bare is ThisExpressionSyntax)
        {
            return false;
        }
        if (bare is not IdentifierNameSyntax)
        {
            return true;
        }
        switch (_model.GetSymbolInfo(bare).Symbol)
        {
            case ILocalSymbol local:
                return local.IsRef;
            case IParameterSymbol parameter:
                return parameter.RefKind != RefKind.None;
            default:
                return true;
        }
    }

    /// The type as written at position, or null after the chain went to
    /// the queue.
    string Spell(AccessChain chain, ITypeSymbol type, int position)
    {
        var text = TypeSpelling.Spell(_model, type, position, out var refused);
        if (text == null)
        {
            Queue(chain, refused);
        }
        return text;
    }

    /// The statement becomes the new code, with the comment that was
    /// behind it on a line of its own above, at the statement's own
    /// indentation. The first line of the new code starts there too: when it
    /// doesn't, the formatter lines the comment up under a comment behind
    /// the line above, as if it went on with that one.
    void Direct(
        AccessChain chain,
        StatementSyntax statement,
        AccessNames names,
        Func<AccessText, string> make)
    {
        var comment = Behind(statement.GetLastToken(), out var span);
        var above = "";
        if (comment != null)
        {
            _plan.NoteComment(chain.Sites(), "above: " + comment);
            above = comment + "\n" + Indent(statement.SpanStart);
        }
        int column = Column(statement.SpanStart);
        _edits.Add(new PlannedEdit
        {
            Span = TextSpan.FromBounds(statement.SpanStart, span.End),
            Statement = statement,
            Sites = chain.Sites(),
            Build = text => above
                + make(new AccessText(text, chain, names, _types, column)),
        });
    }

    /// One edit per statement with hoists in front of it, which puts their
    /// texts there: NestedEdits makes the texts of the edits inside it
    /// first. The statement keeps the column it starts at, so the lines
    /// that continue it stay where they were.
    void AddHoistEdits()
    {
        foreach (var group in _hoists.GroupBy(h => h.Place))
        {
            var hoists = group.ToList();
            if (group.Key is not StatementSyntax statement)
            {
                BodyEdit((ExpressionSyntax)group.Key, hoists);
                continue;
            }
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
    }

    /// The comments behind the token on its line, as written, or null;
    /// span runs from the token to the end of them.
    string Behind(SyntaxToken last, out TextSpan span)
    {
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
        span = TextSpan.FromBounds(last.SpanStart, end);
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

    TempNames Temps(AccessChain chain)
    {
        return _plan.Temps(chain.Top);
    }

    void Queue(AccessChain chain, string reason)
    {
        _plan.Queue(chain.Top, reason);
    }
}
