using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Rules;

/// Whether an expression may move to a temporary in front of its statement
/// (guide section 4.6, codemod.md section 5.3). The answer is no unless the
/// move can't be noticed.
sealed class Purity
{
    /// The reasons a hoist is refused. Queue entries start with one of these.
    public const string MayNotRun = "conditionally evaluated";
    public const string CallEarlier = "a call runs earlier in the statement";
    public const string ReadEarlier =
        "the hoisted code has effects and a mutable value is read earlier";

    readonly SemanticModel _model;

    public Purity(SemanticModel model)
    {
        _model = model;
    }

    /// Null when the expression may be hoisted in front of the statement,
    /// otherwise the reason it may not.
    public string WhyNotHoist(ExpressionSyntax expression, StatementSyntax statement)
    {
        var context = ConditionalContext(expression, statement);
        if (context != null)
        {
            return MayNotRun + ": " + context;
        }
        return WhyNotBefore(expression, Earlier(expression, statement));
    }

    /// Null when the expression may run before these operands, which ran
    /// before it, otherwise the reason it may not.
    public string WhyNotBefore(
        ExpressionSyntax expression, IReadOnlyList<ExpressionSyntax> earlier)
    {
        if (!HasEffects(expression))
        {
            foreach (var operand in earlier)
            {
                if (HasEffects(operand))
                {
                    return CallEarlier + ": "
                        + NameCall(operand, Array.Empty<SyntaxNode>());
                }
            }
            return null;
        }
        foreach (var operand in earlier)
        {
            if (!IsStable(operand, expression))
            {
                return ReadEarlier + ": " + operand;
            }
        }
        return null;
    }

    /// What makes the expression run only sometimes, or more than once; null
    /// when it runs exactly once each time the statement runs.
    public static string ConditionalContext(
        ExpressionSyntax expression, StatementSyntax statement)
    {
        SyntaxNode child = expression;
        for (var parent = child.Parent;
            parent != null && child != statement;
            child = parent, parent = parent.Parent)
        {
            var reason = ContextOf(parent, child);
            if (reason != null)
            {
                return reason;
            }
            // A loop body, a branch of an if or a case section runs more
            // than once or only sometimes. So does any other statement inside
            // the one the expression would move in front of.
            if (parent is StatementSyntax && parent != statement)
            {
                return "a statement inside the statement";
            }
        }
        return null;
    }

    static string ContextOf(SyntaxNode parent, SyntaxNode child)
    {
        switch (parent)
        {
            case BinaryExpressionSyntax b when b.Right == child
                    && (b.IsKind(SyntaxKind.LogicalAndExpression)
                        || b.IsKind(SyntaxKind.LogicalOrExpression)
                        || b.IsKind(SyntaxKind.CoalesceExpression)):
                return "the right side of &&, || or ??";
            case AssignmentExpressionSyntax a when a.Right == child
                    && a.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                return "the right side of ??=";
            case ConditionalExpressionSyntax c when c.Condition != child:
                return "a branch of ?:";
            case ConditionalAccessExpressionSyntax a when a.WhenNotNull == child:
                return "the part after ?.";
            case AnonymousFunctionExpressionSyntax:
                return "a lambda body";
            case ArrowExpressionClauseSyntax:
                return "an expression body";
            case WhileStatementSyntax w when w.Condition == child:
                return "a loop condition";
            case DoStatementSyntax d when d.Condition == child:
                return "a loop condition";
            case ForStatementSyntax f when f.Statement != child:
                return "a for header";
            case InitializerExpressionSyntax:
                return "an object or collection initializer";
            case SwitchExpressionArmSyntax:
                return "a switch expression arm";
            case WhenClauseSyntax:
                return "a when clause";
            case IfStatementSyntax i when i.Condition == child
                    && i.Parent is ElseClauseSyntax:
                return "an else-if condition";
            case EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent.Parent: FieldDeclarationSyntax,
                },
            }:
                return "a field initializer";
            default:
                return null;
        }
    }

    /// The operands the expression's statement evaluates before it.
    public static List<ExpressionSyntax> Earlier(
        ExpressionSyntax expression, StatementSyntax statement)
    {
        var earlier = new List<ExpressionSyntax>();
        SyntaxNode child = expression;
        for (var parent = child.Parent;
            parent != null && child != statement;
            child = parent, parent = parent.Parent)
        {
            AddEarlier(parent, child, earlier);
        }
        return earlier;
    }

    static void AddEarlier(
        SyntaxNode parent, SyntaxNode child, List<ExpressionSyntax> earlier)
    {
        switch (parent)
        {
            case BinaryExpressionSyntax b when b.Right == child:
                earlier.Add(b.Left);
                break;
            case AssignmentExpressionSyntax a when a.Right == child:
                AddTarget(a, earlier);
                break;
            case ArgumentListSyntax list:
                AddArguments(list.Arguments, child, earlier);
                if (list.Parent is InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax access,
                    })
                {
                    earlier.Add(access.Expression);
                }
                break;
            case BracketedArgumentListSyntax list:
                AddArguments(list.Arguments, child, earlier);
                break;
            case ElementAccessExpressionSyntax e when e.ArgumentList == child:
                earlier.Add(e.Expression);
                break;
            case TupleExpressionSyntax tuple:
                AddArguments(tuple.Arguments, child, earlier);
                break;
            case InterpolatedStringExpressionSyntax text:
                foreach (var hole in text.Contents
                    .TakeWhile(c => c != child)
                    .OfType<InterpolationSyntax>())
                {
                    earlier.Add(hole.Expression);
                }
                break;
            case ArrayRankSpecifierSyntax rank:
                earlier.AddRange(rank.Sizes.TakeWhile(s => s != child));
                break;
        }
    }

    /// A plain assignment reads only the parts of its target; a compound one
    /// reads the target itself first.
    static void AddTarget(
        AssignmentExpressionSyntax assignment, List<ExpressionSyntax> earlier)
    {
        earlier.AddRange(TargetParts(assignment));
    }

    /// What an assignment evaluates of its target before its right side.
    public static List<ExpressionSyntax> TargetParts(
        AssignmentExpressionSyntax assignment)
    {
        var parts = new List<ExpressionSyntax>();
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            parts.Add(assignment.Left);
            return parts;
        }
        switch (assignment.Left)
        {
            case MemberAccessExpressionSyntax member:
                parts.Add(member.Expression);
                break;
            case ElementAccessExpressionSyntax element:
                parts.Add(element.Expression);
                parts.AddRange(
                    element.ArgumentList.Arguments.Select(a => a.Expression));
                break;
        }
        return parts;
    }

    static void AddArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        SyntaxNode child,
        List<ExpressionSyntax> earlier)
    {
        foreach (var argument in arguments)
        {
            if (argument == child)
            {
                return;
            }
            earlier.Add(argument.Expression);
        }
    }

    /// The kinds of operation that do nothing by themselves. Calls, getters,
    /// operators and new are looked at one by one; any kind not listed, such
    /// as an assignment, ++, await, throw or a dynamic call, counts as an
    /// effect.
    static readonly HashSet<OperationKind> Inert = new HashSet<OperationKind>
    {
        OperationKind.Literal,
        OperationKind.LocalReference,
        OperationKind.ParameterReference,
        OperationKind.FieldReference,
        OperationKind.InstanceReference,
        OperationKind.PropertyReference,
        OperationKind.ArrayElementReference,
        OperationKind.Invocation,
        OperationKind.ObjectCreation,
        OperationKind.Conversion,
        OperationKind.Unary,
        OperationKind.Binary,
        OperationKind.Conditional,
        OperationKind.Coalesce,
        OperationKind.ConditionalAccess,
        OperationKind.ConditionalAccessInstance,
        OperationKind.ArrayCreation,
        OperationKind.ArrayInitializer,
        OperationKind.Argument,
        OperationKind.InterpolatedString,
        OperationKind.Interpolation,
        OperationKind.InterpolatedStringText,
        OperationKind.Tuple,
        OperationKind.DefaultValue,
        OperationKind.TypeOf,
        OperationKind.NameOf,
        OperationKind.SizeOf,
        OperationKind.Discard,
        OperationKind.DeclarationExpression,
        OperationKind.IsType,
        OperationKind.IsPattern,
        OperationKind.ConstantPattern,
        OperationKind.DeclarationPattern,
        OperationKind.TypePattern,
        OperationKind.RelationalPattern,
        OperationKind.NegatedPattern,
        OperationKind.BinaryPattern,
        OperationKind.DiscardPattern,
        OperationKind.RecursivePattern,
        OperationKind.PropertySubpattern,
        OperationKind.MethodReference,
        OperationKind.EventReference,
        OperationKind.AnonymousFunction,
        OperationKind.DelegateCreation,
    };

    /// True when running the expression can change anything or run code the
    /// tool can't see: assignments, ++, await, calls, getters and operators of
    /// the game or the mod, new of their types.
    public bool HasEffects(ExpressionSyntax expression)
    {
        return HasEffects(expression, Array.Empty<SyntaxNode>());
    }

    /// The same for what is left of the expression once the parts in moved
    /// have gone into temporaries in front of its statement. What moved
    /// counts no more, but a conversion the compiler applies to its value
    /// still runs where it was.
    public bool HasEffects(
        ExpressionSyntax expression, ICollection<SyntaxNode> moved)
    {
        if (IsTypeName(expression))
        {
            return false;
        }
        var operation = _model.GetOperation(expression);
        return operation == null || Effects(operation, moved);
    }

    /// The type in front of a static member, as Math in Math.Max(a, b) or
    /// Mod in "Mod.Shared = x". It is not a value, so nothing runs for it.
    bool IsTypeName(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax wrapped)
        {
            expression = wrapped.Expression;
        }
        return _model.GetSymbolInfo(expression).Symbol is INamespaceOrTypeSymbol;
    }

    bool Effects(IOperation operation, ICollection<SyntaxNode> moved)
    {
        return FirstEffect(operation, moved) != null;
    }

    /// The first operation in the tree whose own work has an effect, in the
    /// order they run: the operands of a node run before the node itself.
    /// What moved and the bodies of lambdas are left out.
    static IOperation FirstEffect(
        IOperation operation, ICollection<SyntaxNode> moved)
    {
        if (operation is not IConversionOperation
            && moved.Contains(operation.Syntax))
        {
            return null;
        }
        if (operation is IAnonymousFunctionOperation
            || operation is IDelegateCreationOperation)
        {
            return null;
        }
        foreach (var child in operation.ChildOperations)
        {
            var found = FirstEffect(child, moved);
            if (found != null)
            {
                return found;
            }
        }
        if (OwnEffect(operation))
        {
            return operation;
        }
        return null;
    }

    /// Whether this one operation, apart from what it holds, can change
    /// anything or run code the tool can't see.
    static bool OwnEffect(IOperation operation)
    {
        if (!Inert.Contains(operation.Kind))
        {
            return true;
        }
        switch (operation)
        {
            case IInvocationOperation invocation:
                return !KnownPure.IsPureCall(invocation.TargetMethod);
            case IPropertyReferenceOperation property:
                return !KnownPure.IsPureGetter(property.Property);
            case IObjectCreationOperation creation:
                return creation.Constructor == null
                    || !KnownPure.IsPureCreation(creation.Constructor);
            case IBinaryOperation { OperatorMethod: not null } binary:
                return !KnownPure.IsPureOperator(binary.OperatorMethod);
            case IUnaryOperation { OperatorMethod: not null } unary:
                return !KnownPure.IsPureOperator(unary.OperatorMethod);
            case IConversionOperation { OperatorMethod: not null } conversion:
                return !KnownPure.IsPureOperator(conversion.OperatorMethod);
            default:
                return false;
        }
    }

    /// What a queue entry names an earlier operand with effects by: the
    /// operand when it is a call, otherwise the first call, getter or
    /// operator in it, in the order they run, so a long text or a ?: doesn't
    /// hide what runs. A user-defined operator says whose it is, because
    /// "x != null" doesn't look like a call.
    public string NameCall(
        ExpressionSyntax operand, ICollection<SyntaxNode> moved)
    {
        if (TernaryChain.Strip(operand) is InvocationExpressionSyntax)
        {
            return Cut(operand.ToString());
        }
        // A parenthesized expression has no operation of its own, such as
        // a ?: in an interpolation hole.
        var operation = _model.GetOperation(TernaryChain.Strip(operand));
        if (operation == null)
        {
            return Cut(operand.ToString());
        }
        var first = FirstEffect(operation, moved);
        if (first == null)
        {
            return Cut(operand.ToString());
        }
        var name = Cut(first.Syntax.ToString());
        var owner = OperatorOwner(first);
        if (owner != null)
        {
            name += " (an operator of " + owner.ToDisplayString() + ")";
        }
        return name;
    }

    /// The type that declares the user-defined operator the operation
    /// calls, or null for any other operation.
    static INamedTypeSymbol OperatorOwner(IOperation operation)
    {
        switch (operation)
        {
            case IBinaryOperation { OperatorMethod: not null } binary:
                return binary.OperatorMethod.ContainingType;
            case IUnaryOperation { OperatorMethod: not null } unary:
                return unary.OperatorMethod.ContainingType;
            case IConversionOperation { OperatorMethod: not null } conversion:
                return conversion.OperatorMethod.ContainingType;
            default:
                return null;
        }
    }

    /// The text on one line, cut to about 60 characters for a queue entry.
    public static string Cut(string text)
    {
        var line = System.Text.RegularExpressions.Regex.Replace(
            text, @"\s*\r?\n\s*", " ");
        if (line.Length <= 60)
        {
            return line;
        }
        return line.Substring(0, 57) + "...";
    }

    /// Must a receiver go into a temporary before its null check? A second
    /// read of anything but a local, a parameter or this can see another value,
    /// and Net/ runs on several threads. A delegate always goes through a local.
    public bool NeedsTemporary(ExpressionSyntax receiver)
    {
        while (receiver is ParenthesizedExpressionSyntax wrapped)
        {
            receiver = wrapped.Expression;
        }
        if (receiver is ThisExpressionSyntax
            || _model.GetTypeInfo(receiver).Type?.TypeKind == TypeKind.Delegate)
        {
            return receiver is not ThisExpressionSyntax;
        }
        switch (_model.GetSymbolInfo(receiver).Symbol)
        {
            case ILocalSymbol local:
                return local.IsRef;
            case IParameterSymbol parameter:
                return parameter.RefKind != RefKind.None;
            default:
                return true;
        }
    }

    /// True when both branches of a ?: have the type of the ?: itself. In
    /// "object o = c ? 1 : 2.0" both branches box a double, and writing the
    /// branches as statements would box an int: such a site needs a cast or
    /// the queue.
    public bool BranchesHaveNaturalType(ConditionalExpressionSyntax site)
    {
        var natural = _model.GetTypeInfo(site).Type;
        var comparer = SymbolEqualityComparer.Default;
        return natural != null
            && comparer.Equals(natural, _model.GetTypeInfo(site.WhenTrue).Type)
            && comparer.Equals(natural, _model.GetTypeInfo(site.WhenFalse).Type);
    }

    /// True for a value the hoisted code can't change or observe being read
    /// later: a literal, this, a readonly field, a getter that only reads a
    /// readonly field (KnownPure.FieldOf), or a local or parameter that the
    /// hoisted code doesn't write and no closure captures.
    public bool IsStable(ExpressionSyntax operand, ExpressionSyntax hoisted)
    {
        while (operand is ParenthesizedExpressionSyntax wrapped)
        {
            operand = wrapped.Expression;
        }
        if (_model.GetConstantValue(operand).HasValue
            || operand is ThisExpressionSyntax
            || operand is BaseExpressionSyntax
            || operand is TypeOfExpressionSyntax
            || IsTypeName(operand))
        {
            return true;
        }
        switch (_model.GetSymbolInfo(operand).Symbol)
        {
            case ILocalSymbol local:
                return !local.IsRef && Quiet(local, hoisted);
            case IParameterSymbol parameter:
                return parameter.RefKind == RefKind.None
                    && Quiet(parameter, hoisted);
            case IFieldSymbol field when field.IsReadOnly || field.IsConst:
                return ReceiverIsStable(operand, hoisted);
            case IPropertySymbol property
                    when KnownPure.FieldOf(property) is { IsReadOnly: true }:
                return ReceiverIsStable(operand, hoisted);
            default:
                return false;
        }
    }

    /// The object a member is read from, when the source names one.
    bool ReceiverIsStable(ExpressionSyntax operand, ExpressionSyntax hoisted)
    {
        if (operand is MemberAccessExpressionSyntax member)
        {
            return IsStable(member.Expression, hoisted);
        }
        return true;
    }

    /// Not written by the hoisted code and captured by no closure.
    bool Quiet(ISymbol symbol, ExpressionSyntax hoisted)
    {
        var flow = _model.AnalyzeDataFlow(hoisted);
        if (flow.Succeeded && flow.WrittenInside.Contains(symbol))
        {
            return false;
        }
        var body = hoisted.AncestorsAndSelf().LastOrDefault(IsBody);
        if (body == null)
        {
            return false;
        }
        var whole = _model.AnalyzeDataFlow(body);
        return whole.Succeeded && !whole.Captured.Contains(symbol);
    }

    static bool IsBody(SyntaxNode node)
    {
        return node is BlockSyntax
            && node.Parent is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax;
    }
}
