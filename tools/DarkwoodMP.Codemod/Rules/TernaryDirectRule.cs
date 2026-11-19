using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Stage 3 (guide section 4.3, codemod.md section 7.1). A ?: that is the
/// whole value of a declaration, an assignment, a return, an expression body,
/// a lambda or local function body, an if condition or a foreach source
/// becomes if/else. A ?: in one of its branches becomes an else-if, or an if
/// inside the branch. Nothing moves in front of its statement: a ?: inside an
/// argument, an operand or an interpolation hole is left for ternary-hoist.
sealed class TernaryDirectRule : Rule
{
    public override string Name => "ternary-direct";

    public override void Plan(RulePlan plan)
    {
        var edits = new List<PlannedEdit>();
        var sites = plan.Root.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .Where(site => !TernaryChain.IsBranch(site))
            .ToList();
        foreach (var site in sites)
        {
            new TernarySite(plan, site, edits).Decide();
        }
        NestedEdits.Propose(plan, edits);
    }
}

/// The decision for one ?: and the ?: nested in its branches.
sealed partial class TernarySite
{
    const string LeftForHoist =
        "not the whole value of its statement: left for ternary-hoist";

    const string InHole =
        "inside an interpolation hole: left for ternary-hoist, which moves "
            + "the hole into a local first";

    readonly RulePlan _plan;
    readonly SemanticModel _model;
    readonly ConditionalExpressionSyntax _site;
    readonly ExpressionSyntax _outer;
    readonly List<ConditionalExpressionSyntax> _chain;
    readonly ITypeSymbol _natural;
    readonly List<PlannedEdit> _edits;

    /// What a shape method planned, when it returned no refusal.
    PlannedEdit _edit;

    public TernarySite(
        RulePlan plan,
        ConditionalExpressionSyntax site,
        List<PlannedEdit> edits)
    {
        _plan = plan;
        _model = plan.Model;
        _site = site;
        _outer = TernaryChain.Outer(site);
        _chain = TernaryChain.Of(site);
        _natural = _model.GetTypeInfo(site).Type;
        _edits = edits;
    }

    public void Decide()
    {
        if (TernaryChain.Strip(_site.WhenTrue) is RefExpressionSyntax)
        {
            Queue("a ref ?:, which no if can stand for");
            return;
        }
        switch (_outer.Parent)
        {
            case EqualsValueClauseSyntax clause:
                Initializer(clause);
                return;
            case AssignmentExpressionSyntax assignment
                    when assignment.Right == _outer:
                Assignment(assignment);
                return;
            case ReturnStatementSyntax statement:
                Finish(Return(statement));
                return;
            case ArrowExpressionClauseSyntax arrow:
                Finish(Arrow(arrow));
                return;
            case LambdaExpressionSyntax lambda
                    when lambda.ExpressionBody == _outer:
                Finish(Lambda(_outer));
                return;
            case IfStatementSyntax statement when statement.Condition == _outer:
                if (statement.Parent is ElseClauseSyntax)
                {
                    Queue("an else-if condition: needs restructuring");
                    return;
                }
                Finish(If(statement));
                return;
            case CommonForEachStatementSyntax statement
                    when statement.Expression == _outer:
                Finish(ForEach(statement));
                return;
            case WhileStatementSyntax:
            case DoStatementSyntax:
                Queue("a loop condition: needs restructuring");
                return;
            case ForStatementSyntax:
                Queue("a for condition or iterator: needs restructuring");
                return;
            case SwitchExpressionArmSyntax arm when arm.Expression == _outer:
                Queue("a switch expression arm: needs a switch statement");
                return;
            case YieldStatementSyntax:
            case ThrowStatementSyntax:
            case SwitchStatementSyntax:
            case LockStatementSyntax:
            case UsingStatementSyntax:
                Queue("not a context of the pattern table: "
                    + _outer.Parent.Kind());
                return;
            default:
                Later(LeftForHoist);
                return;
        }
    }

    void Initializer(EqualsValueClauseSyntax clause)
    {
        switch (clause.Parent)
        {
            case VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax
                {
                    Parent: LocalDeclarationStatementSyntax statement,
                } declaration,
            } declarator:
                Finish(Declaration(statement, declaration, declarator));
                return;
            case VariableDeclaratorSyntax
            {
                Parent.Parent: BaseFieldDeclarationSyntax,
            }:
                Queue("a field initializer: needs a helper method, never a "
                    + "static constructor");
                return;
            case PropertyDeclarationSyntax:
                Queue("a property initializer: needs a helper method");
                return;
            case VariableDeclaratorSyntax { Parent.Parent: ForStatementSyntax }:
                Queue("a for initializer: needs restructuring");
                return;
            default:
                Queue("not a context of the pattern table: "
                    + clause.Parent.Parent.Kind());
                return;
        }
    }

    void Assignment(AssignmentExpressionSyntax assignment)
    {
        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            Later("the right side of ??=, which runs only when the target "
                + "is null");
            return;
        }
        if (assignment.Parent is ExpressionStatementSyntax statement)
        {
            Finish(AssignStatement(assignment, statement));
            return;
        }
        if (assignment.Parent is ArrowExpressionClauseSyntax
            || assignment.Parent is LambdaExpressionSyntax)
        {
            Finish(AssignBody(assignment));
            return;
        }
        Later(LeftForHoist);
    }

    /// A rewrite would write statements inside an interpolation hole, as
    /// the block of a lambda there, so a site in a hole waits until
    /// ternary-hoist has moved the hole into a local of its own.
    void Finish(string refusal)
    {
        if (_outer.Ancestors().Any(node => node is InterpolationSyntax))
        {
            Later(InHole);
            return;
        }
        if (refusal != null)
        {
            Queue(refusal);
            return;
        }
        _edits.Add(_edit);
    }

    void Queue(string reason)
    {
        foreach (var site in _chain)
        {
            _plan.Queue(site, reason);
        }
    }

    void Later(string reason)
    {
        foreach (var site in _chain)
        {
            _plan.Later(site, reason);
        }
    }

    /// "T x = c ? a : b;" becomes "T x; if (c) { x = a; } else { x = b; }",
    /// or "T x = k; if (c) { x = a; }" when the last value is a literal and
    /// T is the type of the ?: itself.
    string Declaration(
        LocalDeclarationStatementSyntax statement,
        VariableDeclarationSyntax declaration,
        VariableDeclaratorSyntax declarator)
    {
        if (statement.Modifiers.Count > 0)
        {
            return "a const or scoped declaration";
        }
        if (statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
        {
            return "a using declaration";
        }
        if (declaration.Variables.Count > 1)
        {
            return "several declarators in one statement";
        }
        if (declaration.Type is RefTypeSyntax)
        {
            return "a ref local";
        }
        var refused = Common(statement.Span);
        if (refused != null)
        {
            return refused;
        }
        var local = (ILocalSymbol)_model.GetDeclaredSymbol(declarator);
        string written = null;
        if (declaration.Type.IsVar)
        {
            written = Spell(local.Type, statement.SpanStart, out refused);
            if (written == null)
            {
                return refused;
            }
        }
        refused = CastsFor(local.Type, statement.SpanStart, out var casts);
        if (refused != null)
        {
            return refused;
        }
        var declared = new Declared
        {
            Name = declarator.Identifier.Text,
            KeepsLast = Same(local.Type, _natural)
                && TernaryChain.IsLiteral(TernaryChain.LastElse(_site)),
            Casts = casts,
            Comment = Taken(Behind(statement, out var span), "above"),
        };
        _edit = new PlannedEdit
        {
            Span = span,
            Sites = Sites(),
            Statement = statement,
            Build = text =>
            {
                var type = written;
                if (type == null)
                {
                    type = text.Of(declaration.Type);
                }
                return Declare(text, declared, type);
            },
        };
        return null;
    }

    /// How a declaration is written: its name, whether it starts with the
    /// last value, the casts of its values and the comment for its line.
    sealed class Declared
    {
        public string Name;
        public bool KeepsLast;
        public Casts Casts;
        public TakenComment Comment;
    }

    /// The declaration and the if/else that give it its value.
    string Declare(EditText text, Declared declared, string type)
    {
        var comment = declared.Comment;
        var chain = new TernaryText(text, declared.Casts, comment);
        var name = declared.Name;
        string Store(string value)
        {
            return name + " = " + value + ";\n";
        }
        var line = type + " " + name;
        string rest;
        if (declared.KeepsLast)
        {
            var last = TernaryChain.LastElse(_site);
            line += " = " + text.Of(TernaryChain.Strip(last));
            rest = chain.StoresBeforeLast(_site, Store);
            if (comment != null && comment.Branch == last)
            {
                line += "; " + comment.Text;
                return line + "\n" + rest;
            }
        }
        else
        {
            rest = chain.Stores(_site, Store);
        }
        return TakenComment.Above(comment) + line + ";\n" + rest;
    }

    /// The comments behind the statement on its line, as written, or null.
    /// The rewrite places them (see TakenComment), so span is the statement
    /// with them.
    string Behind(StatementSyntax statement, out TextSpan span)
    {
        return Behind(statement.GetLastToken(), statement.SpanStart, out span);
    }

    string Behind(SyntaxToken last, int start, out TextSpan span)
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
        span = TextSpan.FromBounds(start, end);
        if (end == last.Span.End)
        {
            return null;
        }
        var between = TextSpan.FromBounds(last.Span.End, end);
        return _plan.Tree.GetText().ToString(between).Trim();
    }

    /// Where a comment taken in goes (see TakenComment): behind the one
    /// branch whose text it starts with, followed by a colon, or else where
    /// says. sites.jsonl notes each, so a person can read them again.
    TakenComment Taken(string text, string elsewhere)
    {
        if (text == null)
        {
            return null;
        }
        var words = text.TrimStart('/', '*').TrimStart();
        var taken = new TakenComment { Text = text };
        int matches = 0;
        foreach (var leaf in TernaryChain.Leaves(_chain))
        {
            var written = TernaryChain.Strip(leaf).ToString();
            if (words.StartsWith(written + ":", StringComparison.Ordinal))
            {
                taken.Branch = leaf;
                matches++;
            }
        }
        var place = "behind its branch";
        if (matches != 1)
        {
            taken.Branch = null;
            place = elsewhere;
        }
        _plan.NoteComment(_chain, place + ": " + text);
        return taken;
    }

    /// "x = c ? a : b;" becomes "if (c) { x = a; } else { x = b; }". The
    /// condition now runs before the target's parts, so they must not be able
    /// to notice (hazard 2). A compound assignment reads its target before
    /// the condition, so nothing in it may be a call (hazard 3).
    string AssignStatement(
        AssignmentExpressionSyntax assignment,
        ExpressionStatementSyntax statement)
    {
        var refused = Common(statement.Span);
        if (refused == null)
        {
            refused = Target(assignment.Left);
        }
        if (refused == null)
        {
            refused = _plan.Purity.WhyNotHoist(_site.Condition, statement);
        }
        if (refused != null)
        {
            return refused;
        }
        Casts casts;
        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            var sink = _model.GetTypeInfo(assignment.Left).Type;
            refused = CastsFor(sink, statement.SpanStart, out casts);
        }
        else
        {
            if (_plan.Purity.HasEffects(assignment.Left)
                || _plan.Purity.HasEffects(_site))
            {
                return "a compound assignment with a call in it";
            }
            if (_natural == null)
            {
                return "a compound assignment of a ?: with no type of its own";
            }
            refused = CastsFor(null, statement.SpanStart, out casts);
        }
        if (refused != null)
        {
            return refused;
        }
        var op = assignment.OperatorToken.Text;
        var flips = Flips(assignment, casts);
        var comment = Taken(Behind(statement, out var span), "above");
        _edit = new PlannedEdit
        {
            Span = span,
            Sites = Sites(),
            Statement = statement,
            AddedStrings = Copies(assignment.Left),
            Build = text =>
            {
                var target = text.Of(assignment.Left);
                string Store(ExpressionSyntax branch, string value)
                {
                    if (flips.TryGetValue(branch, out var number))
                    {
                        return target + " " + Opposite(op) + " "
                            + text.Of(number) + ";\n";
                    }
                    return target + " " + op + " " + value + ";\n";
                }
                return TakenComment.Above(comment)
                    + new TernaryText(text, casts, comment).Stores(
                        _site, Store);
            },
        };
        return null;
    }

    /// The branches of "x += c ? 1 : -1" that are a minus and a number, with
    /// the number, so that they become "x -= 1" rather than "x += -1". Only
    /// for the operators of the built-in number types, where x + (-k) and
    /// x - k are the same value and overflow alike (IEEE 754 defines x - y
    /// as x + (-y)), and only when the number has the type of its negation:
    /// 2147483648 is a uint, -2147483648 an int.
    Dictionary<ExpressionSyntax, ExpressionSyntax> Flips(
        AssignmentExpressionSyntax assignment, Casts casts)
    {
        var flips = new Dictionary<ExpressionSyntax, ExpressionSyntax>();
        if (!assignment.IsKind(SyntaxKind.AddAssignmentExpression)
            && !assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
        {
            return flips;
        }
        if (_model.GetOperation(assignment) is not
            ICompoundAssignmentOperation { OperatorMethod: null } compound
            || !IsNumber(compound.Target.Type))
        {
            return flips;
        }
        foreach (var leaf in TernaryChain.Leaves(_chain))
        {
            if (!casts.Leaves.Contains(leaf)
                && TernaryChain.Strip(leaf) is PrefixUnaryExpressionSyntax sign
                && sign.IsKind(SyntaxKind.UnaryMinusExpression)
                && sign.Operand.IsKind(SyntaxKind.NumericLiteralExpression)
                && Same(
                    _model.GetTypeInfo(sign.Operand).Type,
                    _model.GetTypeInfo(sign).Type))
            {
                flips[leaf] = sign.Operand;
            }
        }
        return flips;
    }

    static string Opposite(string op)
    {
        if (op == "+=")
        {
            return "-=";
        }
        return "+=";
    }

    static bool IsNumber(ITypeSymbol type)
    {
        if (type == null)
        {
            return false;
        }
        switch (type.SpecialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
            default:
                return false;
        }
    }

    /// "return c ? a : b;" becomes "if (c) { return a; } return b;".
    string Return(ReturnStatementSyntax statement)
    {
        var refused = Common(statement.Span);
        if (refused == null)
        {
            refused = ReturnCasts(statement.SpanStart, out var casts);
            if (refused == null)
            {
                var comment = Taken(Behind(statement, out var span), "above");
                _edit = new PlannedEdit
                {
                    Span = span,
                    Sites = Sites(),
                    Statement = statement,
                    Build = text => TakenComment.Above(comment)
                        + new TernaryText(text, casts, comment).Returns(_site),
                };
            }
        }
        return refused;
    }

    /// The type the returned value is converted to: the member's return
    /// type, or for an async one what its task holds.
    string ReturnCasts(int position, out Casts casts)
    {
        var sink = _model.GetTypeInfo(_outer).ConvertedType;
        return CastsFor(sink, position, out casts);
    }

    /// "if (c ? a : b) ..." becomes "T t; if (c) { t = a; } else { t = b; }
    /// if (t) ...". The condition is the first thing the if runs, so the
    /// temporary changes no order.
    string If(IfStatementSyntax statement)
    {
        return WithTemporary(statement, "condition");
    }

    /// "foreach (var x in c ? a : b)" gets a temporary the same way. A
    /// variable declared in the source would move from the foreach's scope
    /// to the block's.
    string ForEach(CommonForEachStatementSyntax statement)
    {
        if (Declares(_site))
        {
            return "the source declares a variable, whose scope would change";
        }
        return WithTemporary(statement, "items");
    }

    string WithTemporary(StatementSyntax statement, string fallback)
    {
        var refused = Common(_outer.Span);
        if (refused != null)
        {
            return refused;
        }
        var type = Spell(_natural, statement.SpanStart, out refused);
        if (type == null)
        {
            return refused;
        }
        // The temporary has the natural type, so no value needs a cast. A
        // comment behind the statement follows its last brace, as before.
        var declared = new Declared
        {
            Name = _plan.Temps(_site).Unique(Hint(fallback)),
            KeepsLast = TernaryChain.IsLiteral(TernaryChain.LastElse(_site)),
            Casts = Casts.None,
        };
        _edit = new PlannedEdit
        {
            Span = statement.Span,
            Sites = Sites(),
            Statement = statement,
            Build = text => Declare(text, declared, type)
                + text.Of(statement.SpanStart, _outer.SpanStart)
                + declared.Name
                + text.Of(_outer.Span.End, statement.Span.End),
        };
        return null;
    }

    /// A name from what the branches hold, such as "ready" for
    /// "c ? a.Ready : b.Ready", and "runs" for "c ? StoryStepRuns() : Runs()":
    /// the value is either one, so it is named after what both have in
    /// common, or after the context when they have nothing. A branch that is
    /// a plain name gives none: its name is taken, and "a2" says nothing.
    string Hint(string fallback)
    {
        var hints = new List<string>();
        foreach (var branch in new[] { _site.WhenTrue, _site.WhenFalse })
        {
            if (TernaryChain.Strip(branch) is IdentifierNameSyntax)
            {
                continue;
            }
            var hint = TempNames.Hint(branch, _model);
            if (hint != "value")
            {
                hints.Add(hint);
            }
        }
        if (hints.Count == 0)
        {
            return fallback;
        }
        if (hints.Count == 1)
        {
            return hints[0];
        }
        var common = TempNames.CommonEnd(hints[0], hints[1]);
        if (common == null)
        {
            return fallback;
        }
        return common;
    }
}
