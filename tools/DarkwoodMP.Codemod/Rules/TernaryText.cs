using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// The shape of a ?: and of the ?: nested in its branches. A nested one is
/// part of the same rewrite: it becomes an else-if, or an if inside the
/// branch, and is never a site of its own.
static class TernaryChain
{
    public static ExpressionSyntax Strip(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax wrapped)
        {
            expression = wrapped.Expression;
        }
        return expression;
    }

    /// The node around the ?: once its parentheses are left out: its parent
    /// is the context the ?: is used in.
    public static ExpressionSyntax Outer(ExpressionSyntax site)
    {
        var node = site;
        while (node.Parent is ParenthesizedExpressionSyntax wrapped)
        {
            node = wrapped;
        }
        return node;
    }

    /// True for a ?: that is a branch of another, maybe in parentheses.
    public static bool IsBranch(ConditionalExpressionSyntax site)
    {
        var outer = Outer(site);
        return outer.Parent is ConditionalExpressionSyntax parent
            && (parent.WhenTrue == outer || parent.WhenFalse == outer);
    }

    /// The ?: and every ?: in its branches, outermost first.
    public static List<ConditionalExpressionSyntax> Of(
        ConditionalExpressionSyntax site)
    {
        var chain = new List<ConditionalExpressionSyntax>();
        Add(site, chain);
        return chain;
    }

    static void Add(
        ConditionalExpressionSyntax site,
        List<ConditionalExpressionSyntax> chain)
    {
        chain.Add(site);
        foreach (var branch in new[] { site.WhenTrue, site.WhenFalse })
        {
            if (Strip(branch) is ConditionalExpressionSyntax nested)
            {
                Add(nested, chain);
            }
        }
    }

    /// The branches that are values, not ?: themselves.
    public static List<ExpressionSyntax> Leaves(
        List<ConditionalExpressionSyntax> chain)
    {
        var leaves = new List<ExpressionSyntax>();
        foreach (var site in chain)
        {
            foreach (var branch in new[] { site.WhenTrue, site.WhenFalse })
            {
                if (Strip(branch) is not ConditionalExpressionSyntax)
                {
                    leaves.Add(branch);
                }
            }
        }
        return leaves;
    }

    /// The value the chain gives when every condition on the way along the
    /// else branches is false.
    public static ExpressionSyntax LastElse(ConditionalExpressionSyntax site)
    {
        var last = site.WhenFalse;
        while (Strip(last) is ConditionalExpressionSyntax next)
        {
            last = next.WhenFalse;
        }
        return last;
    }

    /// A literal: null, default, true, false, a number (with a minus or
    /// not), a string or a char. It has no effects, so it can be stored
    /// before the condition runs.
    public static bool IsLiteral(ExpressionSyntax expression)
    {
        var bare = Strip(expression);
        if (bare is PrefixUnaryExpressionSyntax sign
            && (sign.IsKind(SyntaxKind.UnaryMinusExpression)
                || sign.IsKind(SyntaxKind.UnaryPlusExpression)))
        {
            return Strip(sign.Operand)
                .IsKind(SyntaxKind.NumericLiteralExpression);
        }
        return bare is LiteralExpressionSyntax;
    }
}

/// The branches that must be cast to the natural type of the ?:, and that
/// type as written in the file. In "object o = c ? 1 : 2.0" both branches
/// become doubles before the ?: boxes one, so "o = 1" would box an int: it
/// becomes "o = (double)1" (codemod.md section 5.3, hazard 1).
sealed class Casts
{
    public static readonly Casts None =
        new Casts(new HashSet<SyntaxNode>(), null);

    public readonly HashSet<SyntaxNode> Leaves;
    public readonly string Type;

    public Casts(HashSet<SyntaxNode> leaves, string type)
    {
        Leaves = leaves;
        Type = type;
    }
}

/// A comment the rewrite took in from behind its statement, and where it
/// goes. One that starts with the text of a branch and a colon, such as
/// "// null: ..." behind "c ? a : null", explains that branch, so it goes
/// behind the statement that holds that value. Any other explains the whole
/// statement and goes on a line of its own above the new code: behind the
/// first if it would read as a note on the condition and the first branch,
/// and behind a bare declaration as a note on nothing. A member's comment
/// stays on its header's line.
sealed class TakenComment
{
    public string Text;

    /// The branch it explains, or null.
    public ExpressionSyntax Branch;

    /// The comment on a line of its own, or nothing when it goes behind a
    /// branch.
    public static string Above(TakenComment comment)
    {
        if (comment == null || comment.Branch != null)
        {
            return "";
        }
        return comment.Text + "\n";
    }

    /// The comment for a member's header line, or null.
    public static string OnHeader(TakenComment comment)
    {
        if (comment == null || comment.Branch != null)
        {
            return null;
        }
        return comment.Text;
    }
}

/// The statements a ?: chain becomes (codemod.md section 7.1). Text comes
/// from the EditText, so edits inside a branch are kept.
sealed class TernaryText
{
    readonly EditText _text;
    readonly Casts _casts;

    /// The comment that was behind the statement, when it goes behind one
    /// branch's statement, or null.
    readonly TakenComment _comment;

    /// What goes in front of a branch's statement, or null.
    readonly Func<ExpressionSyntax, string> _before;

    public TernaryText(
        EditText text, Casts casts, TakenComment comment = null)
    {
        _text = text;
        _casts = casts;
        _comment = comment;
    }

    /// Statements that go in front of each branch's statement, such as the
    /// temporaries of a ?: inside the branch: before gets the branch as
    /// written.
    public TernaryText(
        EditText text, Casts casts, Func<ExpressionSyntax, string> before)
        : this(text, casts)
    {
        _before = before;
    }

    /// "if (c) { x = a; } else if (d) { x = e; } else { x = f; }", where
    /// store gives the statement that keeps one value.
    public string Stores(
        ConditionalExpressionSyntax site, Func<string, string> store)
    {
        return Stores(site, (branch, value) => store(value));
    }

    /// The same, with a store that also sees the branch the value is from.
    public string Stores(
        ConditionalExpressionSyntax site,
        Func<ExpressionSyntax, string, string> store)
    {
        var output = new StringBuilder();
        Store(site, store, true, output);
        return output.ToString();
    }

    /// The same without the last else, for a declaration that already holds
    /// the last value: "T x = k; if (c) { x = a; }".
    public string StoresBeforeLast(
        ConditionalExpressionSyntax site, Func<string, string> store)
    {
        return StoresBeforeLast(site, (branch, value) => store(value));
    }

    /// The same, with a store that also sees the branch the value is from.
    public string StoresBeforeLast(
        ConditionalExpressionSyntax site,
        Func<ExpressionSyntax, string, string> store)
    {
        var output = new StringBuilder();
        Store(site, store, false, output);
        return output.ToString();
    }

    void Store(
        ConditionalExpressionSyntax site,
        Func<ExpressionSyntax, string, string> store,
        bool lastElse,
        StringBuilder output)
    {
        Header(site, output);
        var whenTrue = TernaryChain.Strip(site.WhenTrue);
        if (whenTrue is ConditionalExpressionSyntax yes)
        {
            Store(yes, store, true, output);
        }
        else
        {
            output.Append(Leaf(site.WhenTrue, store));
        }
        output.Append("}\n");
        var whenFalse = TernaryChain.Strip(site.WhenFalse);
        if (whenFalse is ConditionalExpressionSyntax no)
        {
            output.Append("else ");
            Store(no, store, lastElse, output);
            return;
        }
        if (lastElse)
        {
            output.Append("else\n{\n")
                .Append(Leaf(site.WhenFalse, store))
                .Append("}\n");
        }
    }

    /// "if (c) { return a; } return b;": after a return no else is needed.
    public string Returns(ConditionalExpressionSyntax site)
    {
        var output = new StringBuilder();
        Return(site, output);
        return output.ToString();
    }

    void Return(ConditionalExpressionSyntax site, StringBuilder output)
    {
        Header(site, output);
        ReturnBranch(site.WhenTrue, output);
        output.Append("}\n");
        ReturnBranch(site.WhenFalse, output);
    }

    void ReturnBranch(ExpressionSyntax branch, StringBuilder output)
    {
        if (TernaryChain.Strip(branch) is ConditionalExpressionSyntax nested)
        {
            Return(nested, output);
            return;
        }
        output.Append(
            Leaf(branch, (leaf, value) => "return " + value + ";\n"));
    }

    /// "if (c)" and the opening brace.
    void Header(ConditionalExpressionSyntax site, StringBuilder output)
    {
        output.Append("if (").Append(Condition(site)).Append(")\n{\n");
    }

    string Condition(ConditionalExpressionSyntax site)
    {
        return _text.Of(TernaryChain.Strip(site.Condition));
    }

    /// One value as a statement, with the comment that explains it. A throw
    /// expression becomes a throw statement, because "x = throw e;" isn't C#.
    string Leaf(
        ExpressionSyntax branch, Func<ExpressionSyntax, string, string> store)
    {
        var statement = Value(branch, store);
        if (_comment != null && _comment.Branch == branch)
        {
            statement = statement.TrimEnd('\n') + " " + _comment.Text + "\n";
        }
        if (_before != null)
        {
            statement = _before(branch) + statement;
        }
        return statement;
    }

    string Value(
        ExpressionSyntax branch, Func<ExpressionSyntax, string, string> store)
    {
        var bare = TernaryChain.Strip(branch);
        if (bare is ThrowExpressionSyntax thrown)
        {
            return "throw " + _text.Of(thrown.Expression) + ";\n";
        }
        if (!_casts.Leaves.Contains(branch))
        {
            return store(branch, _text.Of(bare));
        }
        var value = _text.Of(bare);
        if (!IsPrimary(bare))
        {
            value = "(" + value + ")";
        }
        return store(branch, "(" + _casts.Type + ")" + value);
    }

    /// An expression a cast can stand in front of without parentheses. A
    /// minus sign is not among them: "(Kind)-1" reads as a subtraction.
    static bool IsPrimary(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case IdentifierNameSyntax:
            case GenericNameSyntax:
            case MemberAccessExpressionSyntax:
            case InvocationExpressionSyntax:
            case ElementAccessExpressionSyntax:
            case ThisExpressionSyntax:
            case DefaultExpressionSyntax:
            case TypeOfExpressionSyntax:
            case ParenthesizedExpressionSyntax:
            case TupleExpressionSyntax:
            case ObjectCreationExpressionSyntax:
            case InterpolatedStringExpressionSyntax:
                return true;
            case LiteralExpressionSyntax literal:
                return !literal.IsKind(SyntaxKind.DefaultLiteralExpression);
            default:
                return false;
        }
    }
}
