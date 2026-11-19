using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// The forms of the coalesce rule that write into a local or a parameter
/// instead of into a temporary of their own.
sealed partial class CoalescePlanner
{
    /// "x = a ?? b;" for a local or a parameter x: "x = a; if (x == null) {
    /// x = b; }", or "if (x == null) { x = b; }" when x is the first value
    /// itself, which is then never stored to when it isn't null. In the
    /// first form x holds the first value while the later ones run, so it
    /// needs that nothing can see x until the chain is done (Unseen).
    bool IntoLocal(CoalesceChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not AssignmentExpressionSyntax
            {
                Parent: ExpressionStatementSyntax statement,
            } assignment
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || assignment.Right != outer
            || !_types.Identity(
                _types.TypeOf(assignment.Left), chain.Natural))
        {
            return false;
        }
        var target = LocalOf(assignment.Left);
        if (target == null)
        {
            return false;
        }
        var names = new CoalesceNames
        {
            Name = TernaryChain.Strip(assignment.Left).ToString(),
        };
        if (!IsSelf(chain, target))
        {
            if (!Unseen(chain, target, statement))
            {
                return false;
            }
            names.Assigned = true;
        }
        if (!Temporaries(chain, names, statement, new List<string>()))
        {
            return true;
        }
        Direct(chain.Sites(), statement, text => text.Chain(chain, names));
        return true;
    }

    /// "x ?? (x = v)" for a local or a parameter x is "x ??= v", and
    /// "x = f(x ?? b);" is "if (x == null) { x = b; } x = f(x);". Both put
    /// the ifs in front of the statement and x in the place of the chain.
    bool HoistIntoLocal(
        CoalesceChain chain,
        ExpressionSyntax replaced,
        StatementSyntax statement)
    {
        Func<CoalesceText, string> make;
        var store = FillsItself(chain);
        var names = new CoalesceNames();
        if (store != null)
        {
            make = text => text.Assign(store, names, false);
        }
        else if (RefillsTarget(chain, statement))
        {
            names.Name = TernaryChain.Strip(chain.First).ToString();
            make = text => text.Chain(chain, names);
        }
        else
        {
            return false;
        }
        int column = Column(statement.SpanStart);
        _hoists.Add(new CoalesceHoist
        {
            Replaced = replaced,
            Moved = chain.Top,
            Sites = chain.Sites(),
            Statement = statement,
            Name = TernaryChain.Strip(chain.First).ToString(),
            Make = text => make(new CoalesceText(text, column)),
        });
        return true;
    }

    /// The store of "x ?? (x = v)" for a local or a parameter x whose type
    /// is the type of the ??, or null.
    AssignmentExpressionSyntax FillsItself(CoalesceChain chain)
    {
        if (chain.Values.Count != 2
            || chain.Form != CoalesceForm.Fill
            || chain.Access != null
            || CoalesceTypes.IsNullable(chain.Natural)
            || chain.Values[1] is not AssignmentExpressionSyntax store
            || !store.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || TernaryChain.Strip(store.Left) is not IdentifierNameSyntax
            || !_types.Identity(_types.TypeOf(chain.First), chain.Natural))
        {
            return null;
        }
        var target = LocalOf(chain.First);
        if (target == null || !Same(store.Left, target))
        {
            return null;
        }
        return store;
    }

    /// Whether the statement is "x = f(x ?? b);" for a local or a parameter
    /// x that nothing else in it reads. Filling x in front of it is unseen
    /// when x can't be read from elsewhere and no catch or finally can see
    /// x when f throws, since the store at the end writes x anyway.
    bool RefillsTarget(CoalesceChain chain, StatementSyntax statement)
    {
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment,
            }
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || chain.Form != CoalesceForm.Fill
            || chain.Access != null
            || chain.First is not IdentifierNameSyntax first
            || !_types.Identity(_types.TypeOf(first), chain.Natural))
        {
            return false;
        }
        var target = LocalOf(assignment.Left);
        if (target == null
            || !Same(first, target)
            || IsRef(target)
            || Captured(target, statement)
            || InTry(statement))
        {
            return false;
        }
        foreach (var name in assignment.Right.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>())
        {
            if (name != first && Same(name, target))
            {
                return false;
            }
        }
        return true;
    }

    /// Whether nothing can read x while "x = a; if (x == null) { x = b; }"
    /// runs: no lambda or local function captures x, x is no ref, no value
    /// after the first reads x (nor the first, when it is an opened ?.,
    /// whose parts run after "x = null"), and in a try only literals and
    /// locals, which can't throw, come after the first store.
    bool Unseen(
        CoalesceChain chain, ISymbol target, StatementSyntax statement)
    {
        if (IsRef(target) || Captured(target, statement))
        {
            return false;
        }
        var later = chain.Values.Skip(1).ToList();
        if (chain.Access != null)
        {
            later.Add(chain.First);
        }
        if (later.Any(value => Reads(value, target)))
        {
            return false;
        }
        if (!InTry(statement))
        {
            return true;
        }
        return chain.Access == null
            && chain.Values.Skip(1).All(
                value => TernaryChain.IsLiteral(value) || IsLocal(value));
    }

    /// "x = x ?? b": the first value is the target itself.
    bool IsSelf(CoalesceChain chain, ISymbol target)
    {
        return chain.Form == CoalesceForm.Fill
            && chain.Access == null
            && chain.First is IdentifierNameSyntax first
            && Same(first, target);
    }

    /// The local or the parameter the expression names, or null.
    ISymbol LocalOf(ExpressionSyntax expression)
    {
        if (!IsLocal(expression))
        {
            return null;
        }
        return _model.GetSymbolInfo(TernaryChain.Strip(expression)).Symbol;
    }

    bool Same(ExpressionSyntax expression, ISymbol symbol)
    {
        return SymbolEqualityComparer.Default.Equals(
            _model.GetSymbolInfo(TernaryChain.Strip(expression)).Symbol,
            symbol);
    }

    static bool IsRef(ISymbol symbol)
    {
        return symbol is IParameterSymbol { RefKind: not RefKind.None }
            || symbol is ILocalSymbol { IsRef: true };
    }

    /// Whether the code names the variable anywhere.
    bool Reads(SyntaxNode code, ISymbol symbol)
    {
        return code.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(name => Same(name, symbol));
    }

    /// Whether a lambda or a local function of the member names the
    /// variable, other than one it is declared in.
    bool Captured(ISymbol symbol, SyntaxNode site)
    {
        SyntaxNode scope = site.SyntaxTree.GetRoot();
        foreach (var ancestor in site.Ancestors())
        {
            if (ancestor is MemberDeclarationSyntax
                && ancestor is not BaseTypeDeclarationSyntax)
            {
                scope = ancestor;
                break;
            }
        }
        foreach (var function in scope.DescendantNodes())
        {
            if ((function is AnonymousFunctionExpressionSyntax
                    || function is LocalFunctionStatementSyntax)
                && !DeclaredIn(symbol, function)
                && Reads(function, symbol))
            {
                return true;
            }
        }
        return false;
    }

    static bool DeclaredIn(ISymbol symbol, SyntaxNode node)
    {
        return symbol.DeclaringSyntaxReferences.Any(
            reference => reference.SyntaxTree == node.SyntaxTree
                && node.Span.Contains(reference.Span));
    }

    /// Whether a catch or a finally could run after the statement throws.
    static bool InTry(StatementSyntax statement)
    {
        return statement.Ancestors().Any(node => node is TryStatementSyntax);
    }
}
