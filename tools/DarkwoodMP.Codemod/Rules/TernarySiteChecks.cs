using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// The checks that decide whether a ?: may be rewritten, and what the
/// rewrite must keep: the natural type, the scope of declared variables and
/// the string constants of a target written twice.
sealed partial class TernarySite
{
    /// The target is written by every branch but a throw, so its string
    /// constants are in the code once per such branch instead of once.
    List<string> Copies(ExpressionSyntax target)
    {
        var strings = new List<string>();
        Constants(target, strings);
        int writes = TernaryChain.Leaves(_chain)
            .Count(leaf =>
                TernaryChain.Strip(leaf) is not ThrowExpressionSyntax);
        var added = new List<string>();
        for (int i = 1; i < writes; i++)
        {
            added.AddRange(strings);
        }
        return added;
    }

    /// The strings the compiler loads for this code: each largest constant
    /// string expression in it, such as "key" or a const field.
    void Constants(SyntaxNode node, List<string> into)
    {
        if (node is ExpressionSyntax expression
            && _model.GetConstantValue(expression) is
            {
                HasValue: true,
                Value: string text,
            })
        {
            into.Add(text);
            return;
        }
        foreach (var child in node.ChildNodes())
        {
            Constants(child, into);
        }
    }

    /// The target is written once per branch, so it must be a name, a member
    /// or an element, and hold nothing that would then be there twice.
    static string Target(ExpressionSyntax target)
    {
        if (target is not IdentifierNameSyntax
            && !target.IsKind(SyntaxKind.SimpleMemberAccessExpression)
            && target is not ElementAccessExpressionSyntax)
        {
            return "the target is not a name, a member or an element";
        }
        foreach (var node in target.DescendantNodesAndSelf())
        {
            if (OperatorCount.KindOf(node) != null)
            {
                return "the target holds another operator, which would be "
                    + "written twice";
            }
            if (node is AnonymousFunctionExpressionSyntax)
            {
                return "the target holds a lambda, which would be written "
                    + "twice";
            }
        }
        return null;
    }

    /// The checks every rewrite needs. Span is what the rewrite builds anew,
    /// so trivia in it that isn't a comment would be lost without the
    /// engine's comment check seeing it.
    string Common(TextSpan span)
    {
        foreach (var site in _chain)
        {
            if (TernaryChain.Strip(site.WhenTrue) is RefExpressionSyntax
                || TernaryChain.Strip(site.WhenFalse) is RefExpressionSyntax)
            {
                return "a ref ?:";
            }
            if (_model.GetConstantValue(site.Condition).HasValue)
            {
                return "the condition is a constant: an if would leave "
                    + "unreachable code";
            }
            if (!Same(_model.GetTypeInfo(site).Type, _natural))
            {
                return "a nested ?: has a type of its own";
            }
        }
        var root = _plan.Root;
        foreach (var trivia in root.DescendantTrivia(span))
        {
            if ((trivia.IsDirective
                    || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
                && span.Contains(trivia.Span))
            {
                return "a preprocessor directive inside the statement";
            }
        }
        return Narrowed();
    }

    /// A variable declared in a branch (out var, a pattern) is in scope in
    /// the whole block today, and after the rewrite only in its own if or
    /// statement. A name outside that part could then mean something else,
    /// such as a field, so no other token may spell it.
    string Narrowed()
    {
        var leaves = TernaryChain.Leaves(_chain);
        var region = _site.Ancestors().FirstOrDefault(node =>
            node is BlockSyntax
            || node is ArrowExpressionClauseSyntax
            || node is AnonymousFunctionExpressionSyntax);
        if (region == null)
        {
            region = _plan.Root;
        }
        foreach (var branch in new[] { _site.WhenTrue, _site.WhenFalse })
        {
            foreach (var declared in Designations(branch))
            {
                var home = Home(declared, leaves);
                var name = declared.Identifier.ValueText;
                foreach (var token in region.DescendantTokens())
                {
                    if (token.IsKind(SyntaxKind.IdentifierToken)
                        && token.ValueText == name
                        && (home == null || !home.Span.Contains(token.Span)))
                    {
                        return "a branch declares " + name + ", which is "
                            + "also named outside its branch";
                    }
                }
            }
        }
        return null;
    }

    /// The part of the chain a declared variable stays in scope in: its
    /// branch, or the nested ?: whose condition declares it.
    SyntaxNode Home(SyntaxNode declared, List<ExpressionSyntax> leaves)
    {
        foreach (var node in declared.Ancestors())
        {
            if (node is ExpressionSyntax value && leaves.Contains(value))
            {
                return node;
            }
            if (node is ConditionalExpressionSyntax nested
                && _chain.Contains(nested)
                && nested.Condition.Span.Contains(declared.Span))
            {
                return node;
            }
        }
        return null;
    }

    /// The variables an expression declares (out var, a pattern, a
    /// deconstruction), apart from those of a lambda or local function in it.
    static IEnumerable<SingleVariableDesignationSyntax> Designations(
        ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf(node =>
                node is not AnonymousFunctionExpressionSyntax
                && node is not LocalFunctionStatementSyntax)
            .OfType<SingleVariableDesignationSyntax>();
    }

    static bool Declares(ExpressionSyntax expression)
    {
        return Designations(expression).Any();
    }

    /// Which branches need a cast to keep the natural type (hazard 1). sink
    /// is the type each value is stored or returned as; null means the
    /// operator decides, as in a compound assignment, so every branch of
    /// another type needs one. A ?: with no natural type is target-typed:
    /// each branch is already converted straight to the target. Where the
    /// natural type converts to the sink by identity, as a tuple to one with
    /// other element names, every branch already converts as the ?: did.
    string CastsFor(ITypeSymbol sink, int position, out Casts casts)
    {
        casts = Casts.None;
        if (_natural == null || (sink != null && Identity(_natural, sink)))
        {
            return null;
        }
        var leaves = new HashSet<SyntaxNode>();
        foreach (var leaf in TernaryChain.Leaves(_chain))
        {
            var bare = TernaryChain.Strip(leaf);
            if (bare is ThrowExpressionSyntax
                || NullToReference(bare, sink))
            {
                continue;
            }
            if (!TargetTyped(bare)
                && Identity(_model.GetTypeInfo(bare).Type, _natural))
            {
                continue;
            }
            leaves.Add(leaf);
        }
        if (leaves.Count == 0)
        {
            return null;
        }
        var type = Spell(_natural, position, out var refused);
        if (type == null)
        {
            return refused;
        }
        casts = new Casts(leaves, type);
        return null;
    }

    /// True for a value whose type comes from where it is written: default,
    /// new(), a collection or switch expression, a lambda, or a tuple with
    /// one of them or a null in it. Roslyn reports the type it was converted
    /// to, the natural type, but written as "o = default" it would take the
    /// target's type instead and box null where the ?: boxed 0.
    static bool TargetTyped(ExpressionSyntax value)
    {
        switch (value)
        {
            case LiteralExpressionSyntax literal:
                return literal.IsKind(SyntaxKind.DefaultLiteralExpression)
                    || literal.IsKind(SyntaxKind.NullLiteralExpression);
            case ImplicitObjectCreationExpressionSyntax:
            case CollectionExpressionSyntax:
            case SwitchExpressionSyntax:
            case AnonymousFunctionExpressionSyntax:
            case ConditionalExpressionSyntax:
                return true;
            case TupleExpressionSyntax tuple:
                return tuple.Arguments.Any(
                    a => TargetTyped(TernaryChain.Strip(a.Expression)));
            default:
                return false;
        }
    }

    /// null or default is a null reference both ways when the natural type
    /// and the target are reference types and the one converts to the other
    /// as a reference. A user-defined conversion is a call: JToken's from
    /// string makes a JValue of type String from a null string, so
    /// "o[k] = c ? s : null" stores that, and "o[k] = null" a null JToken.
    bool NullToReference(ExpressionSyntax value, ITypeSymbol sink)
    {
        bool nullish = value.IsKind(SyntaxKind.NullLiteralExpression)
            || value.IsKind(SyntaxKind.DefaultLiteralExpression);
        if (!nullish || sink == null || !sink.IsReferenceType
            || !_natural.IsReferenceType)
        {
            return false;
        }
        var conversion =
            _model.Compilation.ClassifyCommonConversion(_natural, sink);
        return conversion.IsImplicit && conversion.IsReference;
    }

    /// True when a value of type a is a value of type b without any
    /// conversion code: the same type, or tuples whose elements only have
    /// other names. dynamic stays apart, because its conversions are bound
    /// when the code runs.
    bool Identity(ITypeSymbol a, ITypeSymbol b)
    {
        if (Same(a, b))
        {
            return true;
        }
        if (a == null || b == null || a.TypeKind == TypeKind.Dynamic
            || b.TypeKind == TypeKind.Dynamic)
        {
            return false;
        }
        return _model.Compilation.ClassifyCommonConversion(a, b).IsIdentity;
    }

    /// The type as written at this place in the file, or null and why it
    /// can't be. The text must bind back to the same type there.
    string Spell(ITypeSymbol type, int position, out string refused)
    {
        refused = null;
        if (type == null || type.TypeKind == TypeKind.Error)
        {
            refused = "the ?: has no type of its own";
            return null;
        }
        return TypeSpelling.Spell(_model, type, position, out refused);
    }

    static bool Same(ITypeSymbol a, ITypeSymbol b)
    {
        return SymbolEqualityComparer.Default.Equals(a, b);
    }

    List<SyntaxNode> Sites()
    {
        return _chain.Cast<SyntaxNode>().ToList();
    }
}

/// What a file says about the spelling of types: the types it writes as
/// "global::Name" and the namespaces its using directives import.
sealed class FileSpellings
{
    static readonly ConditionalWeakTable<SyntaxTree, FileSpellings> Files =
        new();

    static readonly ConditionalWeakTable<Compilation, HashSet<string>>
        Shared = new();

    public readonly HashSet<ISymbol> WrittenGlobal =
        new(SymbolEqualityComparer.Default);

    public readonly List<INamespaceSymbol> Imports = new();

    FileSpellings(SemanticModel model)
    {
        var root = model.SyntaxTree.GetRoot();
        foreach (var name in root.DescendantNodes()
            .OfType<AliasQualifiedNameSyntax>())
        {
            if (name.Alias.Identifier.IsKind(SyntaxKind.GlobalKeyword)
                && model.GetSymbolInfo(name).Symbol is INamedTypeSymbol type)
            {
                WrittenGlobal.Add(type.OriginalDefinition);
            }
        }
        foreach (var directive in root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>())
        {
            if (directive.Alias == null
                && directive.StaticKeyword.IsKind(SyntaxKind.None)
                && directive.Name != null
                && model.GetSymbolInfo(directive.Name).Symbol is
                    INamespaceSymbol space)
            {
                Imports.Add(space);
            }
        }
    }

    public static FileSpellings Of(SemanticModel model)
    {
        return Files.GetValue(model.SyntaxTree, _ => new FileSpellings(model));
    }

    /// The metadata names ("Cursor", "List`1") of the public types in System
    /// and UnityEngine and the namespaces inside them.
    public static HashSet<string> NetAndUnity(Compilation compilation)
    {
        return Shared.GetValue(compilation, c =>
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var space in c.GlobalNamespace.GetNamespaceMembers())
            {
                if (space.Name == "System" || space.Name == "UnityEngine")
                {
                    AddTypes(space, names);
                }
            }
            return names;
        });
    }

    static void AddTypes(INamespaceSymbol space, HashSet<string> names)
    {
        foreach (var type in space.GetTypeMembers())
        {
            if (Visible(type))
            {
                names.Add(type.MetadataName);
            }
        }
        foreach (var inner in space.GetNamespaceMembers())
        {
            AddTypes(inner, names);
        }
    }

    public static bool Visible(INamedTypeSymbol type)
    {
        return type.DeclaredAccessibility == Accessibility.Public
            || type.Locations.Any(l => l.IsInSource);
    }
}
