using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Expression bodies (codemod.md section 7.3): a body that is the chain
/// gets a block holding the statement or the return form, and a chain
/// deeper inside a body moves into the block the body becomes, in front
/// of the value it returns.
sealed partial class AccessPlanner
{
    const string OnMembersLine = "on the member's line";

    /// An expression body gets a block: a void member's or lambda's the
    /// statement form, any other the return form. A property or an indexer
    /// gets a get accessor with the block.
    /// A value that converts by more than a reference conversion is left
    /// to Hoist, which keeps its natural type in a temporary in the block.
    bool Body(AccessChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is ArrowExpressionClauseSyntax arrow)
        {
            if (!Returns(chain, _model.GetDeclaredSymbol(arrow.Parent)))
            {
                return false;
            }
            Arrow(chain, arrow);
            return true;
        }
        if (outer.Parent is LambdaExpressionSyntax lambda
            && lambda.ExpressionBody == outer)
        {
            if (!Returns(chain, _model.GetSymbolInfo(lambda).Symbol))
            {
                return false;
            }
            Lambda(chain, outer, lambda);
            return true;
        }
        return false;
    }

    /// The member returns nothing, or the chain's value by an identity or
    /// a reference conversion.
    bool Returns(AccessChain chain, ISymbol symbol)
    {
        ITypeSymbol returned;
        switch (symbol)
        {
            case IMethodSymbol { ReturnsVoid: true }:
                return true;
            case IMethodSymbol method:
                returned = method.ReturnType;
                break;
            case IPropertySymbol property:
                returned = property.Type;
                break;
            default:
                return true;
        }
        return _types.Keeps(_types.TypeOf(chain.Top), returned);
    }

    void Arrow(AccessChain chain, ArrowExpressionClauseSyntax arrow)
    {
        var symbol = _model.GetDeclaredSymbol(arrow.Parent);
        bool discards = false;
        switch (symbol)
        {
            case IMethodSymbol method:
                discards = method.ReturnsVoid;
                if (method.IsAsync)
                {
                    Queue(chain, "an async member's expression body");
                    return;
                }
                break;
            case IPropertySymbol:
                break;
            default:
                Queue(chain, "not a context of the pattern table: an "
                    + "expression body of a " + arrow.Parent.Kind());
                return;
        }
        if (WhyNotBody(chain))
        {
            return;
        }
        var names = Names(chain, arrow.SpanStart);
        if (names == null)
        {
            return;
        }
        var comment = Behind(Semicolon(arrow), out var behind);
        if (comment != null)
        {
            _plan.NoteComment(chain.Sites(), OnMembersLine + ": " + comment);
        }
        var span = TextSpan.FromBounds(arrow.SpanStart, behind.End);
        Func<EditText, string> body = text => Statements(
            new AccessText(text, chain, names, _types, 0), discards);
        if (arrow.Parent is BasePropertyDeclarationSyntax property)
        {
            _edits.Add(new PlannedEdit
            {
                Span = TextSpan.FromBounds(property.SpanStart, behind.End),
                Member = property,
                Sites = chain.Sites(),
                Build = text => Header(text, property, arrow, comment)
                    + "{\nget\n{\n" + body(text) + "}\n}",
            });
            return;
        }
        _edits.Add(new PlannedEdit
        {
            Span = span,
            Sites = chain.Sites(),
            Build = text => Block(comment, body(text)),
        });
    }

    void Lambda(
        AccessChain chain,
        ExpressionSyntax outer,
        LambdaExpressionSyntax lambda)
    {
        if (IsTree(lambda))
        {
            Queue(chain, "a lambda that becomes an expression tree, which "
                + "can't hold a block");
            return;
        }
        if (_model.GetSymbolInfo(lambda).Symbol is not IMethodSymbol method
            || method.IsAsync)
        {
            Queue(chain, "a lambda whose method can't be told, or an async "
                + "one");
            return;
        }
        if (WhyNotBody(chain))
        {
            return;
        }
        var names = Names(chain, outer.SpanStart);
        if (names == null)
        {
            return;
        }
        _edits.Add(new PlannedEdit
        {
            Span = outer.Span,
            Sites = chain.Sites(),
            Build = text => "{\n" + Statements(
                new AccessText(text, chain, names, _types, 0),
                method.ReturnsVoid) + "}",
        });
    }

    /// True after the chain went to the queue: statements can't go in an
    /// interpolation hole.
    bool WhyNotBody(AccessChain chain)
    {
        if (chain.Top.Ancestors().Any(node => node is InterpolationSyntax))
        {
            Queue(chain, InHole);
            return true;
        }
        return false;
    }

    static string Statements(AccessText text, bool discards)
    {
        if (discards)
        {
            return text.ReceiverLine()
                + text.Ifs(value => text.Line(value + ";"));
        }
        return Returns(text);
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

    /// True for a lambda converted to an Expression<T>.
    bool IsTree(SyntaxNode lambda)
    {
        var type = _model.GetTypeInfo(lambda).ConvertedType;
        return type is INamedTypeSymbol { Name: "Expression", Arity: 1 } named
            && named.ContainingNamespace.ToDisplayString()
                == "System.Linq.Expressions";
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

    /// The expression body the expression is in, when no statement is
    /// around it in between: a member's or a lambda's.
    static ExpressionSyntax BodyOf(SyntaxNode node)
    {
        for (var child = node; child.Parent != null; child = child.Parent)
        {
            switch (child.Parent)
            {
                case StatementSyntax:
                    return null;
                case ArrowExpressionClauseSyntax arrow:
                    return arrow.Expression;
                case LambdaExpressionSyntax lambda
                        when lambda.ExpressionBody == child:
                    return lambda.ExpressionBody;
                case AnonymousFunctionExpressionSyntax:
                    return null;
            }
        }
        return null;
    }

    /// Why the expression body can't become a block that returns its
    /// value, or null.
    string WhyNotBlock(ExpressionSyntax body)
    {
        if (body.Parent is LambdaExpressionSyntax lambda)
        {
            if (IsTree(lambda))
            {
                return "a lambda that becomes an expression tree, which "
                    + "can't hold a block";
            }
            if (_model.GetSymbolInfo(lambda).Symbol
                is not IMethodSymbol { IsAsync: false })
            {
                return "a lambda whose method can't be told, or an async one";
            }
            return null;
        }
        switch (_model.GetDeclaredSymbol(body.Parent.Parent))
        {
            case IMethodSymbol { IsAsync: false }:
            case IPropertySymbol:
                return null;
            default:
                return "an expression body of a " + body.Parent.Parent.Kind()
                    + " that can't become a block";
        }
    }

    /// The column a hoist's lines that hold a line break keep: the
    /// statement's, or none in the block of a body.
    int PlaceColumn(SyntaxNode place)
    {
        if (place is StatementSyntax)
        {
            return Column(place.SpanStart);
        }
        return 0;
    }

    /// The expression body becomes a block: the hoists, then the value,
    /// returned unless the member or lambda returns nothing. A property or
    /// an indexer gets a get accessor with the block.
    void BodyEdit(ExpressionSyntax body, List<AccessHoist> hoists)
    {
        var sites = hoists.SelectMany(h => h.Sites).ToList();
        Func<EditText, string> statements = text =>
            string.Concat(hoists.Select(h => h.Text))
                + Value(text, body, Discards(body));
        if (body.Parent is LambdaExpressionSyntax)
        {
            _edits.Add(new PlannedEdit
            {
                Span = body.Span,
                Sites = new List<SyntaxNode>(),
                Build = text => "{\n" + statements(text) + "}",
            });
            return;
        }
        var arrow = (ArrowExpressionClauseSyntax)body.Parent;
        var comment = Behind(Semicolon(arrow), out var behind);
        if (comment != null)
        {
            _plan.NoteComment(sites, OnMembersLine + ": " + comment);
        }
        if (arrow.Parent is BasePropertyDeclarationSyntax property)
        {
            _edits.Add(new PlannedEdit
            {
                Span = TextSpan.FromBounds(property.SpanStart, behind.End),
                Member = property,
                Sites = new List<SyntaxNode>(),
                Build = text => Header(text, property, arrow, comment)
                    + "{\nget\n{\n" + statements(text) + "}\n}",
            });
            return;
        }
        _edits.Add(new PlannedEdit
        {
            Span = TextSpan.FromBounds(arrow.SpanStart, behind.End),
            Sites = new List<SyntaxNode>(),
            Build = text => Block(comment, statements(text)),
        });
    }

    /// "return v;", or "v;" when the value is thrown away. A value over
    /// several lines keeps the column it started at.
    string Value(EditText text, ExpressionSyntax body, bool discards)
    {
        var value = text.Of(body.Span);
        if (!discards)
        {
            value = "return " + value;
        }
        if (value.Contains('\n'))
        {
            value = new string(' ', Column(body.SpanStart)) + value;
        }
        return value + ";\n";
    }

    bool Discards(ExpressionSyntax body)
    {
        ISymbol symbol;
        if (body.Parent is LambdaExpressionSyntax lambda)
        {
            symbol = _model.GetSymbolInfo(lambda).Symbol;
        }
        else
        {
            symbol = _model.GetDeclaredSymbol(body.Parent.Parent);
        }
        return symbol is IMethodSymbol { ReturnsVoid: true };
    }
}
