using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Stage 5 (guide section 4.3, codemod.md section 7.2). A ?? chain or a ??=
/// whose null check is by reference becomes ifs (CoalesceText):
///
/// - "T x = a ?? b;" becomes "T x = a; if (x == null) { x = b; }", when T
///   is the type of the ?? itself, and "x = a ?? b;" for a local that
///   nothing can see in between "x = a; if (x == null) { x = b; }"
///   (CoalesceLocals);
/// - anywhere else, an assignment to a field, a return, an argument, an
///   operand or a hole, the ?? moves into a temporary of its natural type in
///   front of its statement, "T t = a; if (t == null) { t = b; } _f = t;",
///   when the hazard rules of guide section 4.6 prove that nothing can
///   notice (HoistPath, HoistHazards). The field is then written once, so no
///   other thread sees the value between;
/// - a Nullable first value is tested with HasValue and unwrapped with
///   Value, through a temporary unless it is a local or a parameter, and a
///   first value "r?.m" is opened into "if (r != null) { t = r.m; }";
/// - "x ??= v;" becomes "if (x == null) { x = v; }", and "(x ??= v).F()"
///   the same in front of its statement, then "x.F()", or with a temporary
///   for a field or property, which is read and written once as before.
///
/// The right side of ?? runs only when the left side is null, so it is
/// never moved in front of that test. A Unity object's null check is
/// stage 7's (CoalesceTypes), so those sites are left; a type with an == of
/// its own goes to the queue.
sealed class CoalesceRule : Rule
{
    public override string Name => "coalesce";

    public override void Plan(RulePlan plan)
    {
        new CoalescePlanner(plan).Run();
    }
}

/// A ?? chain or ??= that moves in front of its statement, and the name
/// that takes its place there.
sealed class CoalesceHoist
{
    public ExpressionSyntax Replaced;
    public ExpressionSyntax Moved;
    public List<SyntaxNode> Sites;
    public StatementSyntax Statement;
    public string Name;
    public Func<EditText, string> Make;
    public string Text = "";
}

/// The plan of the coalesce rule for one file. Sites come in document
/// order, so the sites of one statement come in the order they run.
sealed partial class CoalescePlanner
{
    const string Nested =
        "a ?? or ??= inside another one: the inner one would run only "
            + "sometimes after the rewrite";

    const string HoldsFirst =
        "holds another ?? or ??= in its first value, which runs every "
            + "time: rewrite one level per run";

    const string InFirst =
        "inside the first value of another ?? or ??=, which runs every "
            + "time: rewrite one level per run";

    const string Declares =
        "the ?? declares a variable, whose scope the rewrite would change";

    readonly RulePlan _plan;
    readonly SemanticModel _model;
    readonly CoalesceTypes _types;
    readonly HoistHazards _hazards;
    readonly List<PlannedEdit> _edits = new();
    readonly List<CoalesceHoist> _hoists = new();

    /// Checks says which null checks the planner rewrites: by reference
    /// for the coalesce rule, Unity's for unity-null (UnityNullRule).
    public CoalescePlanner(
        RulePlan plan, NullCheck checks = NullCheck.Reference)
    {
        _plan = plan;
        _model = plan.Model;
        _types = new CoalesceTypes(plan.Model, checks);
        _hazards = new HoistHazards(plan.Model, plan.Purity);
    }

    public void Run()
    {
        var sites = _plan.Root.DescendantNodes()
            .Where(IsSite)
            .Cast<ExpressionSyntax>()
            .ToList();
        var filtered = NullChecks.Filtered(_plan.Receivers, _types.Checks);
        foreach (var site in sites)
        {
            // Under unity-null a ?? and a ?. one inside the other are
            // queued before the planners run (UnityNesting).
            if (_plan.IsReported(site))
            {
                continue;
            }
            if (filtered != null)
            {
                _plan.Later(site, filtered);
                continue;
            }
            if (site is BinaryExpressionSyntax link)
            {
                if (!CoalesceChain.IsInnerLink(link))
                {
                    Decide(new CoalesceChain(link, _model), sites);
                }
                continue;
            }
            Decide((AssignmentExpressionSyntax)site, sites);
        }
        AddHoistEdits();
        NestedEdits.Propose(_plan, _edits);
    }

    static bool IsSite(SyntaxNode node)
    {
        return node.IsKind(SyntaxKind.CoalesceExpression)
            || node.IsKind(SyntaxKind.CoalesceAssignmentExpression);
    }

    void Decide(CoalesceChain chain, List<ExpressionSyntax> all)
    {
        // unity-null says nothing of a ?? that tests no Unity object, so
        // that its access planner can still take "x?.B ?? false" with x.
        if (!_types.Mine(chain))
        {
            return;
        }
        var links = chain.Links.Cast<SyntaxNode>().ToList();
        var refused = Shape(chain.Top, links, all);
        if (refused == null)
        {
            refused = _types.Check(chain, out bool later);
            if (later)
            {
                Later(links, refused);
                return;
            }
        }
        if (refused != null)
        {
            Queue(links, refused);
            return;
        }
        var outer = TernaryChain.Outer(chain.Top);
        if (Declaration(chain, outer) || IntoLocal(chain, outer))
        {
            return;
        }
        Hoist(chain);
    }

    /// What the text of any site must allow: no other site inside it or
    /// around it, no variable declared in it, no directive in it. A site
    /// in a right side runs only sometimes; one in a first value (or a
    /// ??= target) runs every time, and only the reason says which.
    string Shape(
        ExpressionSyntax site,
        List<SyntaxNode> own,
        List<ExpressionSyntax> all)
    {
        string nested = null;
        foreach (var other in all)
        {
            if (own.Contains(other))
            {
                continue;
            }
            if (other.Span.Contains(site.Span))
            {
                if (RightOf(other).Span.Contains(site.Span))
                {
                    return Nested;
                }
                nested = InFirst;
            }
            else if (site.Span.Contains(other.Span))
            {
                if (own.Any(link => RightOf(link).Span.Contains(other.Span)))
                {
                    return Nested;
                }
                nested = HoldsFirst;
            }
        }
        if (nested != null)
        {
            return nested;
        }
        if (HoistPlanner.Declares(site))
        {
            return Declares;
        }
        foreach (var trivia in _plan.Root.DescendantTrivia(site.Span))
        {
            if ((trivia.IsDirective
                    || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                && site.Span.Contains(trivia.Span))
            {
                return "a preprocessor directive inside the ??";
            }
        }
        return null;
    }

    /// "T x = a ?? b;" when T is the type of the ?? itself: x is the
    /// temporary. Any other declaration goes in front of its statement.
    bool Declaration(CoalesceChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Parent: LocalDeclarationStatementSyntax statement,
                    } declaration,
                } declarator,
            }
            || statement.Modifiers.Count > 0
            || statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            || declaration.Variables.Count > 1
            || !_types.Identity(
                _types.TypeOf(declaration.Type), chain.Natural))
        {
            return false;
        }
        var names = new CoalesceNames { Name = declarator.Identifier.Text };
        if (declaration.Type.IsVar)
        {
            names.Type = Spell(chain, chain.Natural, statement);
            if (names.Type == null)
            {
                return true;
            }
        }
        else
        {
            names.Type = declaration.Type.ToString();
        }
        if (!Temporaries(chain, names, statement, new List<string>()))
        {
            return true;
        }
        Direct(chain.Sites(), statement, text => text.Chain(chain, names));
        return true;
    }

    /// The right side of a ?? link or of a ??=, which runs only sometimes.
    static ExpressionSyntax RightOf(SyntaxNode site)
    {
        if (site is AssignmentExpressionSyntax assignment)
        {
            return assignment.Right;
        }
        return ((BinaryExpressionSyntax)site).Right;
    }

    /// The chain moves into a temporary in front of its statement. The
    /// temporaries it needs are named in the order they are declared.
    void Hoist(CoalesceChain chain)
    {
        var site = chain.Top;
        var links = chain.Links.Cast<SyntaxNode>().ToList();
        var refused = WhyNotHoist(site, out var statement);
        if (refused != null)
        {
            Queue(links, refused);
            return;
        }
        var replaced = HoistPlanner.Replaced(site, null);
        if (HoistIntoLocal(chain, replaced, statement))
        {
            return;
        }
        var names = new CoalesceNames();
        names.Type = Spell(chain, chain.Natural, statement);
        if (names.Type == null)
        {
            return;
        }
        var hints = Hints(replaced, chain.First);
        if (!Temporaries(chain, names, statement, hints))
        {
            return;
        }
        names.Name = Temps(site).Pick(
            hints, Related(replaced, chain.First, null));
        int column = Column(statement.SpanStart);
        _hoists.Add(new CoalesceHoist
        {
            Replaced = replaced,
            Moved = site,
            Sites = chain.Sites(),
            Statement = statement,
            Name = names.Name,
            Make = text => new CoalesceText(text, column).Chain(chain, names),
        });
    }

    /// The temporaries for the receiver of an opened ?. and for a first
    /// value that is unwrapped, named after what they hold, and a first
    /// value then after the variable the chain fills (names.Name) or the
    /// hints of the chain's own temporary. False when one of their types
    /// can't be written, after the chain went to the queue.
    bool Temporaries(
        CoalesceChain chain,
        CoalesceNames names,
        StatementSyntax statement,
        List<string> later)
    {
        ExpressionSyntax held = null;
        if (chain.Access != null)
        {
            if (_plan.Purity.NeedsTemporary(chain.Access.Expression))
            {
                held = chain.Access.Expression;
            }
        }
        else if (chain.Form == CoalesceForm.Unwrap && !IsLocal(chain.First))
        {
            held = chain.First;
        }
        if (held == null)
        {
            return true;
        }
        var type = Spell(chain, _types.TypeOf(held), statement);
        if (type == null)
        {
            return false;
        }
        // An unwrapped first value is the chain's value before it is
        // unwrapped, so it may take the name of what the chain fills; a
        // receiver is something else.
        var hints = new List<string> { TempNames.Named(held, _model) };
        string filled = null;
        if (chain.Access == null)
        {
            filled = names.Name;
            if (filled != null)
            {
                hints.Add(filled);
            }
            hints.AddRange(later);
        }
        var name = Temps(held).Pick(hints, Related(held, held, filled));
        if (chain.Access != null)
        {
            names.ReceiverType = type;
            names.Receiver = name;
        }
        else
        {
            names.HeldType = type;
            names.Held = name;
        }
        return true;
    }

    /// The type as written in front of the statement, or null after the
    /// chain went to the queue.
    string Spell(
        CoalesceChain chain, ITypeSymbol type, StatementSyntax statement)
    {
        var text = TypeSpelling.Spell(
            _model, type, statement.SpanStart, out var refused);
        if (text == null)
        {
            Queue(chain.Links.Cast<SyntaxNode>().ToList(), refused);
        }
        return text;
    }

    /// Why the site can't move in front of its statement, or null, after
    /// which statement is where it goes.
    string WhyNotHoist(ExpressionSyntax site, out StatementSyntax statement)
    {
        var path = HoistPath.Walk(site, null, _model);
        statement = path.Statement;
        if (path.Refusal != null)
        {
            return path.Refusal;
        }
        if (statement.Ancestors().Any(node => node is InterpolationSyntax))
        {
            return "the statement is inside an interpolation hole, where no "
                + "statement can go";
        }
        if (HoistPlanner.AwaitsIn(statement))
        {
            return "an await in the statement: the temporary could become a "
                + "field of the state machine";
        }
        return _hazards.WhyNotBefore(site, path, Moved(statement));
    }

    /// What the hoists already planned in front of the statement move:
    /// they run there in the same order as before.
    HashSet<SyntaxNode> Moved(StatementSyntax statement)
    {
        var moved = new HashSet<SyntaxNode>();
        foreach (var hoist in _hoists)
        {
            if (hoist.Statement == statement)
            {
                moved.Add(hoist.Moved);
            }
        }
        return moved;
    }

    /// The names for a temporary of the chain's value, best first: the
    /// parameter it is passed to, when that is a word, the target an
    /// assignment stores it to,
    /// the name of its first value, and "text" in an interpolation hole.
    List<string> Hints(ExpressionSyntax replaced, ExpressionSyntax first)
    {
        var hints = new List<string>();
        if (replaced.Parent is ArgumentSyntax)
        {
            var parameter = TempNames.Hint(replaced, _model);
            if (TempNames.IsWord(parameter))
            {
                hints.Add(parameter);
            }
        }
        if (replaced.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == replaced)
        {
            hints.Add(TempNames.Hint(assignment.Left, _model));
        }
        hints.Add(TempNames.Named(first, _model));
        if (replaced.Parent is InterpolationSyntax)
        {
            hints.Add("text");
        }
        return hints;
    }

    /// The names a temporary may take with a number, as a second version
    /// of what they name: the target an assignment stores the value to,
    /// the variable the chain fills, and the first value when it is a
    /// name or a member.
    List<string> Related(
        ExpressionSyntax replaced, ExpressionSyntax first, string filled)
    {
        var related = new List<string>();
        if (replaced.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == replaced)
        {
            related.Add(TempNames.Hint(assignment.Left, _model));
        }
        if (filled != null)
        {
            related.Add(filled);
        }
        var bare = TernaryChain.Strip(first);
        if (bare is IdentifierNameSyntax
            || bare is MemberAccessExpressionSyntax)
        {
            related.Add(TempNames.Named(bare, _model));
        }
        return related;
    }

    /// A local or a parameter, which nothing but the code in front of it
    /// can change between two reads.
    bool IsLocal(ExpressionSyntax expression)
    {
        var bare = TernaryChain.Strip(expression);
        if (bare is not IdentifierNameSyntax)
        {
            return false;
        }
        var symbol = _model.GetSymbolInfo(bare).Symbol;
        return symbol is ILocalSymbol || symbol is IParameterSymbol;
    }

    TempNames Temps(SyntaxNode site)
    {
        return _plan.Temps(site);
    }

    void Queue(List<SyntaxNode> sites, string reason)
    {
        foreach (var site in sites)
        {
            _plan.Queue(site, reason);
        }
    }

    void Later(List<SyntaxNode> sites, string reason)
    {
        foreach (var site in sites)
        {
            _plan.Later(site, reason);
        }
    }
}
