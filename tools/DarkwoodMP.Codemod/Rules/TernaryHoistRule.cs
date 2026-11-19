using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Stage 4 (guide section 4.3, codemod.md section 7.1). A ?: in an argument,
/// an operand or an interpolation hole, in a statement, an if condition or a
/// foreach source, moves into a temporary of its natural type in front of
/// its statement: "F(x, c ? a : b);" becomes "T t; if (c) { t = a; } else
/// { t = b; } F(x, t);", and "{(c ? x : y):0.0}" becomes "{t:0.0}". A site
/// moves only when the hazard rules of guide section 4.6 prove that nothing
/// can notice (HoistPath, HoistHazards); every other goes to the queue with
/// the reason. Wire flags, "(a ? 1 : 0) | (b ? 2 : 0)", become "int flags =
/// 0; if (a) { flags |= 1; } if (b) { flags |= 2; }", inside the cast that
/// was around them. A ?: inside a branch or the first condition of one that
/// moves gets a temporary of its own, in front of the statement that holds
/// it after the rewrite, so a second run finds nothing more to do.
sealed class TernaryHoistRule : Rule
{
    public override string Name => "ternary-hoist";

    public override void Plan(RulePlan plan)
    {
        new HoistPlanner(plan).Run();
    }
}

/// One expression that moves into a temporary: a ?: and the ?: nested in
/// its branches, or a chain of wire flags.
sealed class Hoist
{
    /// What the temporary's name takes the place of.
    public ExpressionSyntax Replaced;

    /// What runs in front of the statement: the ?:, or the top |.
    public ExpressionSyntax Moved;

    public List<SyntaxNode> Sites;
    public string Name;
    public string Type;

    /// The statement it goes in front of, or null when it goes in front of
    /// Part, a branch or the condition of Host.
    public StatementSyntax Statement;
    public Hoist Host;
    public SyntaxNode Part;

    /// The hoists that go in front of each part of this one, in order.
    public readonly Dictionary<SyntaxNode, List<Hoist>> Inside = new();

    /// Makes the declaration and the statements that give the temporary its
    /// value. Run once the texts of the edits inside it are known.
    public Func<EditText, string> Make;

    /// What Make made.
    public string Text = "";

    /// The statement the outermost hoist around this one goes in front of.
    public StatementSyntax Root()
    {
        var hoist = this;
        while (hoist.Statement == null)
        {
            hoist = hoist.Host;
        }
        return hoist.Statement;
    }

    /// The texts of the hoists that go in front of this part.
    public string Before(SyntaxNode part)
    {
        if (!Inside.TryGetValue(part, out var inner))
        {
            return "";
        }
        return string.Concat(inner.Select(h => h.Text));
    }
}

/// The plan of ternary-hoist for one file. Sites come in document order, so
/// a ?: is decided before any ?: inside it, and the sites of one statement
/// in the order they run.
sealed partial class HoistPlanner
{
    const string WholeValue =
        "the whole value of its context: ternary-direct's, which rewrote or "
            + "queued it";

    readonly RulePlan _plan;
    readonly SemanticModel _model;
    readonly HoistHazards _hazards;
    readonly List<Hoist> _hoists = new();

    /// ?: that a chain of flags takes, so they aren't decided again.
    readonly HashSet<SyntaxNode> _taken = new();

    public HoistPlanner(RulePlan plan)
    {
        _plan = plan;
        _model = plan.Model;
        _hazards = new HoistHazards(plan.Model, plan.Purity);
    }

    public void Run()
    {
        var sites = _plan.Root.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .Where(site => !TernaryChain.IsBranch(site))
            .ToList();
        foreach (var site in sites)
        {
            if (!_taken.Contains(site))
            {
                Decide(site);
            }
        }
        NestedEdits.Propose(_plan, Edits());
    }

    void Decide(ConditionalExpressionSyntax site)
    {
        var chain = TernaryChain.Of(site);
        var outer = TernaryChain.Outer(site);
        if (TernaryChain.Strip(site.WhenTrue) is RefExpressionSyntax)
        {
            Queue(chain, "a ref ?:, which no if can stand for");
            return;
        }
        if (IsWholeValue(outer) && !InHole(outer))
        {
            Later(chain, WholeValue);
            return;
        }
        if (TryFlags(site))
        {
            return;
        }
        var host = HostOf(site);
        SyntaxNode part = null;
        if (host != null)
        {
            part = PartOf(host, site, out var refused);
            if (part == null)
            {
                Queue(chain, refused);
                return;
            }
        }
        var path = HoistPath.Walk(site, part, _model);
        if (path.Refusal != null)
        {
            Queue(chain, path.Refusal);
            return;
        }
        if (path.Statement != null)
        {
            // A statement inside a lambda in the part: the site's own.
            host = null;
            part = null;
        }
        var hoist = new Hoist
        {
            Replaced = Replaced(site, part),
            Moved = site,
            Sites = chain.Cast<SyntaxNode>().ToList(),
            Statement = path.Statement,
            Host = host,
            Part = part,
        };
        var refusal = Check(site, hoist, path);
        if (refusal != null)
        {
            Queue(chain, refusal);
            return;
        }
        Accept(hoist);
    }

    /// Why the chain can't move, or null, after which hoist has its name,
    /// type and text.
    string Check(ConditionalExpressionSyntax site, Hoist hoist, HoistPath path)
    {
        var ternary = new TernarySite(_plan, site);
        var root = hoist.Root();
        var span = hoist.Replaced.Span;
        if (hoist.Statement != null)
        {
            span = hoist.Statement.Span;
        }
        var refused = ternary.HoistRefusal(span);
        if (refused == null)
        {
            refused = StatementRefusal(hoist, site);
        }
        if (refused == null && TernaryChain.Leaves(TernaryChain.Of(site))
            .All(l => TernaryChain.Strip(l) is ThrowExpressionSyntax))
        {
            refused = "every branch throws: the statement after it would "
                + "never run";
        }
        if (refused == null)
        {
            refused = _hazards.WhyNotBefore(site, path, Moved(hoist));
        }
        if (refused != null)
        {
            return refused;
        }
        hoist.Type = ternary.SpellType(
            ternary.Natural, root.SpanStart, out refused);
        if (hoist.Type == null)
        {
            return refused;
        }
        hoist.Name = _plan.Temps(site).Unique(Hint(ternary, hoist.Replaced));
        hoist.Make = text => Chain(text, hoist, site);
        return null;
    }

    /// What the statement in front of which the temporary goes must allow.
    string StatementRefusal(Hoist hoist, ExpressionSyntax moved)
    {
        var statement = hoist.Statement;
        if (statement == null)
        {
            return null;
        }
        if (statement.Ancestors().Any(node => node is InterpolationSyntax))
        {
            return "the statement is inside an interpolation hole, where no "
                + "statement can go";
        }
        if (Declares(moved) && !KeepsScope(statement))
        {
            return "the ?: declares a variable, whose scope would change";
        }
        if (AwaitsIn(statement))
        {
            return "an await in the statement: the temporary could become a "
                + "field of the state machine";
        }
        return null;
    }

    /// The hoists that move in front of the same statement or part before
    /// this one: what they move runs there in the same order as before.
    HashSet<SyntaxNode> Moved(Hoist hoist)
    {
        var moved = new HashSet<SyntaxNode>();
        foreach (var other in _hoists)
        {
            if (other.Statement == hoist.Statement && other.Host == hoist.Host
                && other.Part == hoist.Part)
            {
                moved.Add(other.Moved);
            }
        }
        return moved;
    }

    void Accept(Hoist hoist)
    {
        _hoists.Add(hoist);
        if (hoist.Host != null)
        {
            if (!hoist.Host.Inside.TryGetValue(hoist.Part, out var inner))
            {
                inner = new List<Hoist>();
                hoist.Host.Inside[hoist.Part] = inner;
            }
            inner.Add(hoist);
        }
    }

    /// "T t; if (c) { t = a; } else { t = b; }", or "T t = k; if (c) { t =
    /// a; }" when the last value is a literal. A hoist inside a branch goes
    /// in front of the store of that branch, one inside the condition in
    /// front of it all.
    string Chain(EditText text, Hoist hoist, ConditionalExpressionSyntax site)
    {
        var chain = new TernaryText(
            text, Casts.None, branch => hoist.Before(PartKey(branch)));
        var name = hoist.Name;
        string Store(ExpressionSyntax branch, string value)
        {
            var store = name + " = ";
            if (value.Contains('\n'))
            {
                // The store starts where its statement did, so the lines
                // that continue the value, such as a lambda's block, move
                // as far as the store does.
                store = new string(' ', Column(hoist.Root().SpanStart))
                    + store;
            }
            return store + value + ";\n";
        }
        var line = hoist.Type + " " + name;
        var last = TernaryChain.LastElse(site);
        string rest;
        if (TernaryChain.IsLiteral(last))
        {
            line += " = " + text.Of(TernaryChain.Strip(last));
            rest = chain.StoresBeforeLast(site, Store);
        }
        else
        {
            rest = chain.Stores(site, Store);
        }
        return hoist.Before(TernaryChain.Strip(site.Condition)) + line + ";\n"
            + rest;
    }

    /// One edit per statement with hoists in front of it, and one per hoist,
    /// whose text is the temporary's name: NestedEdits puts the texts of
    /// the edits inside each one in place. The statement keeps the column
    /// it starts at, because the layout pass moves the lines that continue
    /// it as far as it moves its first line.
    List<PlannedEdit> Edits()
    {
        var edits = new List<PlannedEdit>();
        var statements = _hoists
            .Where(h => h.Statement != null)
            .GroupBy(h => h.Statement);
        foreach (var group in statements)
        {
            var statement = group.Key;
            var hoists = group.ToList();
            var indent = Indent(statement.SpanStart);
            edits.Add(new PlannedEdit
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
            edits.Add(new PlannedEdit
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
        return edits;
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

    /// The innermost hoist that holds the site, or null.
    Hoist HostOf(SyntaxNode site)
    {
        for (int i = _hoists.Count - 1; i >= 0; i--)
        {
            if (_hoists[i].Moved.Span.Contains(site.Span))
            {
                return _hoists[i];
            }
        }
        return null;
    }

    /// The part of the host that holds the site and becomes a statement of
    /// its own: a branch, or the first condition. Null, with the reason,
    /// for any other.
    static SyntaxNode PartOf(Hoist host, SyntaxNode site, out string refused)
    {
        refused = null;
        if (host.Moved is not ConditionalExpressionSyntax top)
        {
            refused = "inside wire flags that move";
            return null;
        }
        foreach (var leaf in TernaryChain.Leaves(TernaryChain.Of(top)))
        {
            var part = PartKey(leaf);
            if (part.Span.Contains(site.Span))
            {
                return part;
            }
        }
        var condition = TernaryChain.Strip(top.Condition);
        if (condition.Span.Contains(site.Span))
        {
            return condition;
        }
        refused = Purity.MayNotRun + ": an else-if condition of a ?: that "
            + "moves";
        return null;
    }

    /// What a branch's statement holds: the value, or what a throw throws.
    static ExpressionSyntax PartKey(ExpressionSyntax branch)
    {
        var bare = TernaryChain.Strip(branch);
        if (bare is ThrowExpressionSyntax thrown)
        {
            return TernaryChain.Strip(thrown.Expression);
        }
        return bare;
    }

    /// The ?: with the parentheses around it, as far as they are inside the
    /// part it is in: "{(c ? x : y):0.0}" becomes "{t:0.0}".
    static ExpressionSyntax Replaced(ExpressionSyntax site, SyntaxNode part)
    {
        var node = site;
        while (node != part
            && node.Parent is ParenthesizedExpressionSyntax wrapped)
        {
            node = wrapped;
        }
        return node;
    }

    /// The parameter an argument is passed to, or what the branches have in
    /// common, or "text" in an interpolation hole.
    string Hint(TernarySite ternary, ExpressionSyntax replaced)
    {
        if (replaced.Parent is ArgumentSyntax)
        {
            var hint = TempNames.Hint(replaced, _model);
            if (hint != "value")
            {
                return hint;
            }
        }
        if (replaced.Parent is InterpolationSyntax)
        {
            return ternary.NameHint("text");
        }
        return ternary.NameHint("value");
    }

    /// The contexts ternary-direct rewrote a ?: in, as the whole value. What
    /// it left there is in its queue already.
    static bool IsWholeValue(ExpressionSyntax outer)
    {
        switch (outer.Parent)
        {
            case EqualsValueClauseSyntax:
            case ReturnStatementSyntax:
            case ArrowExpressionClauseSyntax:
            case WhileStatementSyntax:
            case DoStatementSyntax:
            case ForStatementSyntax:
            case YieldStatementSyntax:
            case ThrowStatementSyntax:
            case SwitchStatementSyntax:
            case LockStatementSyntax:
            case UsingStatementSyntax:
                return true;
            case AssignmentExpressionSyntax assignment
                    when assignment.Right == outer:
                return !assignment.IsKind(
                        SyntaxKind.CoalesceAssignmentExpression)
                    && (assignment.Parent is ExpressionStatementSyntax
                        || assignment.Parent is ArrowExpressionClauseSyntax
                        || assignment.Parent is LambdaExpressionSyntax);
            case LambdaExpressionSyntax lambda:
                return lambda.ExpressionBody == outer;
            case IfStatementSyntax test:
                return test.Condition == outer;
            case CommonForEachStatementSyntax loop:
                return loop.Expression == outer;
            case SwitchExpressionArmSyntax arm:
                return arm.Expression == outer;
            default:
                return false;
        }
    }

    /// ternary-direct left the sites in a hole to this rule.
    static bool InHole(SyntaxNode node)
    {
        return node.Ancestors().Any(n => n is InterpolationSyntax);
    }

    /// Statements whose expression variables are in scope in the whole
    /// block around them, as those of an if condition in front of them are.
    static bool KeepsScope(StatementSyntax statement)
    {
        return statement is ExpressionStatementSyntax
            || statement is LocalDeclarationStatementSyntax
            || statement is ReturnStatementSyntax
            || statement is ThrowStatementSyntax
            || statement is IfStatementSyntax;
    }

    /// The expression declares a variable (out var, a pattern), apart from
    /// one in a lambda or local function in it.
    static bool Declares(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf(node =>
                node is not AnonymousFunctionExpressionSyntax
                && node is not LocalFunctionStatementSyntax)
            .OfType<SingleVariableDesignationSyntax>()
            .Any();
    }

    /// An await in the statement, outside a lambda, in an async member.
    static bool AwaitsIn(StatementSyntax statement)
    {
        return statement
            .DescendantNodes(node =>
                node is not AnonymousFunctionExpressionSyntax
                && node is not LocalFunctionStatementSyntax)
            .OfType<AwaitExpressionSyntax>()
            .Any();
    }

    void Queue(List<ConditionalExpressionSyntax> chain, string reason)
    {
        foreach (var site in chain)
        {
            _plan.Queue(site, reason);
        }
    }

    void Later(List<ConditionalExpressionSyntax> chain, string reason)
    {
        foreach (var site in chain)
        {
            _plan.Later(site, reason);
        }
    }
}

/// What ternary-hoist asks of a ?: chain: the checks every rewrite of it
/// makes, and the spelling and name of its temporary.
sealed partial class TernarySite
{
    public TernarySite(RulePlan plan, ConditionalExpressionSyntax site)
        : this(plan, site, new List<PlannedEdit>())
    {
    }

    public ITypeSymbol Natural => _natural;

    /// Why the chain can't become an if/else in front of a statement, or
    /// null: a ref ?:, a constant condition, a nested ?: of another type, a
    /// directive in span, or a variable a branch declares named elsewhere.
    public string HoistRefusal(TextSpan span)
    {
        return Common(span);
    }

    /// The type as written at position, or null and why it can't be.
    public string SpellType(ITypeSymbol type, int position, out string refused)
    {
        return Spell(type, position, out refused);
    }

    /// A name from what the branches hold, or the fallback.
    public string NameHint(string fallback)
    {
        return Hint(fallback);
    }
}
