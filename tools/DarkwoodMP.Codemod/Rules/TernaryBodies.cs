using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// The rewrites of a ?: that is a whole expression body: of a method, a
/// property, an accessor, a local function or a lambda. Each gets a block
/// body.
sealed partial class TernarySite
{
    /// "int X => c ? a : b;" gets a block body, then the return form. A
    /// property or an indexer gets a get accessor with the block.
    string Arrow(ArrowExpressionClauseSyntax arrow)
    {
        if (arrow.Parent is not BaseMethodDeclarationSyntax
            && arrow.Parent is not AccessorDeclarationSyntax
            && arrow.Parent is not LocalFunctionStatementSyntax
            && arrow.Parent is not BasePropertyDeclarationSyntax)
        {
            return "not a context of the pattern table: an expression body "
                + "of a " + arrow.Parent.Kind();
        }
        var refused = Common(BodySpan(_outer));
        if (refused == null)
        {
            refused = ReturnCasts(arrow.SpanStart, out var casts);
            if (refused == null)
            {
                _edit = ArrowEdit(arrow, casts);
            }
        }
        return refused;
    }

    PlannedEdit ArrowEdit(ArrowExpressionClauseSyntax arrow, Casts casts)
    {
        string comment;
        TextSpan span;
        if (arrow.Parent is BasePropertyDeclarationSyntax property)
        {
            comment = Behind(
                property.GetLastToken(), property.SpanStart, out span);
            return new PlannedEdit
            {
                Span = span,
                Sites = Sites(),
                Member = property,
                Build = text => Header(text, property, arrow, comment)
                    + "{\nget\n{\n"
                    + new TernaryText(text, casts).Returns(_site)
                    + "}\n}",
            };
        }
        comment = BodyComment(_outer, out span);
        return new PlannedEdit
        {
            Span = span,
            Sites = Sites(),
            Build = text => Block(
                comment, new TernaryText(text, casts).Returns(_site)),
        };
    }

    /// A property's attributes, modifiers, type and name as written, with
    /// the comment that was behind the property at the end of their line.
    static string Header(
        EditText text,
        BasePropertyDeclarationSyntax property,
        ArrowExpressionClauseSyntax arrow,
        string comment)
    {
        var header = text.Of(property.SpanStart, arrow.SpanStart);
        if (comment == null)
        {
            return header;
        }
        return header.TrimEnd() + " " + comment + "\n";
    }

    /// "x => c ? a : b" becomes "x => { if (c) { return a; } return b; }". If
    /// a generic method's types can't be inferred from the block, it doesn't
    /// compile, and the site goes to the queue.
    string Lambda(ExpressionSyntax body)
    {
        if (IsTree(body.Parent))
        {
            return "a lambda that becomes an expression tree, which can't "
                + "hold a block";
        }
        var refused = Common(body.Span);
        if (refused == null)
        {
            refused = ReturnCasts(body.SpanStart, out var casts);
            if (refused == null)
            {
                _edit = new PlannedEdit
                {
                    Span = body.Span,
                    Sites = Sites(),
                    Build = text => "{\n"
                        + new TernaryText(text, casts).Returns(_site) + "}",
                };
            }
        }
        return refused;
    }

    /// "void M() => x = c ? a : b;" gets a block that assigns x on every
    /// path, as a patch's "Prefix(out T __state) => __state = ...;" does.
    /// The target's parts are checked as in a statement (hazard 2).
    string AssignBody(AssignmentExpressionSyntax assignment)
    {
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            return "a compound assignment in an expression body";
        }
        if (!DiscardsValue(assignment.Parent))
        {
            return "the assignment is the value of its expression body";
        }
        var span = BodySpan(assignment);
        var refused = Common(span);
        if (refused == null)
        {
            refused = Target(assignment.Left);
        }
        if (refused == null)
        {
            refused = _plan.Purity.WhyNotBefore(
                _site.Condition, Purity.TargetParts(assignment));
        }
        if (refused != null)
        {
            return refused;
        }
        var sink = _model.GetTypeInfo(assignment.Left).Type;
        refused = CastsFor(sink, assignment.SpanStart, out var casts);
        if (refused != null)
        {
            return refused;
        }
        var comment = BodyComment(assignment, out span);
        _edit = new PlannedEdit
        {
            Span = span,
            Sites = Sites(),
            AddedStrings = Copies(assignment.Left),
            Build = text =>
            {
                var target = text.Of(assignment.Left);
                return Block(comment, new TernaryText(text, casts).Stores(
                    _site, value => target + " = " + value + ";\n"));
            },
        };
        return null;
    }

    /// The comment behind a member's expression body, for the line of its
    /// header, and the body's span with it. A lambda's has none of its own.
    string BodyComment(ExpressionSyntax body, out TextSpan span)
    {
        span = BodySpan(body);
        if (body.Parent is ArrowExpressionClauseSyntax arrow)
        {
            return Behind(Semicolon(arrow), arrow.SpanStart, out span);
        }
        return null;
    }

    /// A block body; the comment that was behind the expression body goes
    /// in front of it, on the header's line.
    static string Block(string comment, string statements)
    {
        var block = "{\n" + statements + "}";
        if (comment == null)
        {
            return block;
        }
        return comment + "\n" + block;
    }

    /// True for a lambda converted to an Expression<T>. Its compile error
    /// would be reported on the lambda, outside the rewritten body, and send
    /// every rewrite of the member to the queue.
    bool IsTree(SyntaxNode lambda)
    {
        var type = _model.GetTypeInfo(lambda).ConvertedType;
        return type is INamedTypeSymbol { Name: "Expression", Arity: 1 } named
            && named.ContainingNamespace.ToDisplayString()
                == "System.Linq.Expressions";
    }

    /// True when the member or lambda returns nothing, so the value of its
    /// expression body is thrown away.
    bool DiscardsValue(SyntaxNode body)
    {
        ISymbol symbol;
        if (body is LambdaExpressionSyntax lambda)
        {
            symbol = _model.GetSymbolInfo(lambda).Symbol;
        }
        else
        {
            symbol = _model.GetDeclaredSymbol(body.Parent);
        }
        return symbol is IMethodSymbol { ReturnsVoid: true };
    }

    /// What an expression body is replaced by a block in: the arrow up to
    /// the member's semicolon, or a lambda's body.
    static TextSpan BodySpan(ExpressionSyntax body)
    {
        if (body.Parent is ArrowExpressionClauseSyntax arrow)
        {
            return TextSpan.FromBounds(
                arrow.SpanStart, Semicolon(arrow).Span.End);
        }
        return body.Span;
    }

    static SyntaxToken Semicolon(ArrowExpressionClauseSyntax arrow)
    {
        switch (arrow.Parent)
        {
            case BaseMethodDeclarationSyntax method:
                return method.SemicolonToken;
            case AccessorDeclarationSyntax accessor:
                return accessor.SemicolonToken;
            case LocalFunctionStatementSyntax local:
                return local.SemicolonToken;
            case PropertyDeclarationSyntax property:
                return property.SemicolonToken;
            case IndexerDeclarationSyntax indexer:
                return indexer.SemicolonToken;
            default:
                throw new Stop("An expression body in a "
                    + arrow.Parent.Kind() + " has no semicolon.");
        }
    }
}
