using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// The way up from an expression that ternary-hoist would move to where its
/// temporary goes, and what runs on that way before the expression
/// (codemod.md section 5.3). Every parent on the way must be one this class
/// knows the order of evaluation of: any other sends the site to the queue,
/// so a context the rule wasn't written for is never rewritten by accident.
sealed class HoistPath
{
    /// Why the expression can't move, or null.
    public string Refusal { get; private set; }

    /// The statement the temporary goes in front of; null when the walk
    /// ended at the stop it was given.
    public StatementSyntax Statement { get; private set; }

    /// What runs before the expression: earlier operands and arguments, the
    /// receiver of a call or the delegate it invokes, the parts of an
    /// assignment's target, earlier holes and declarators.
    public readonly List<ExpressionSyntax> Earlier = new();

    /// The earlier values that are made into text before the expression
    /// can run: an earlier interpolation hole, the left side of a string +.
    public readonly List<ExpressionSyntax> Formatted = new();

    readonly SemanticModel _model;

    HoistPath(SemanticModel model)
    {
        _model = model;
    }

    /// Walks up from the expression to its statement, or to stop if the walk
    /// meets it first: a branch or the condition of an outer ?: that moves,
    /// whose parts become statements of their own.
    public static HoistPath Walk(
        ExpressionSyntax expression, SyntaxNode stop, SemanticModel model)
    {
        var path = new HoistPath(model);
        SyntaxNode child = expression;
        while (child != stop)
        {
            var parent = child.Parent;
            if (parent == null)
            {
                path.Refusal = "not inside a statement";
                return path;
            }
            if (parent is StatementSyntax statement)
            {
                path.Statement = statement;
                path.Refusal = path.Head(statement, child);
                return path;
            }
            path.Refusal = path.Step(parent, child);
            if (path.Refusal != null)
            {
                return path;
            }
            child = parent;
        }
        return path;
    }

    /// The statement the walk ended at, seen from the part of it that holds
    /// the expression. Each of these runs that part first, and once.
    string Head(StatementSyntax statement, SyntaxNode child)
    {
        switch (statement)
        {
            case ExpressionStatementSyntax:
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
            case YieldStatementSyntax:
                return null;
            case LocalDeclarationStatementSyntax local:
                if (local.IsConst)
                {
                    return "a const declaration";
                }
                return null;
            case IfStatementSyntax test:
                if (test.Parent is ElseClauseSyntax)
                {
                    return Purity.MayNotRun + ": an else-if condition";
                }
                return null;
            case CommonForEachStatementSyntax loop
                    when loop.Expression == child:
                return null;
            case SwitchStatementSyntax choice when choice.Expression == child:
                return null;
            case LockStatementSyntax gate when gate.Expression == child:
                return null;
            case UsingStatementSyntax use
                    when use.Expression == child || use.Declaration == child:
                return null;
            case WhileStatementSyntax:
            case DoStatementSyntax:
                return Purity.MayNotRun + ": a loop condition";
            case ForStatementSyntax:
                return Purity.MayNotRun + ": a for header";
            default:
                return "not a statement a temporary can go in front of: "
                    + statement.Kind();
        }
    }

    /// One step up. Null when the parent runs nothing before the child but
    /// what the lists collect; otherwise why the expression can't move.
    string Step(SyntaxNode parent, SyntaxNode child)
    {
        switch (parent)
        {
            case ParenthesizedExpressionSyntax:
            case CastExpressionSyntax:
            case PrefixUnaryExpressionSyntax:
            case PostfixUnaryExpressionSyntax:
            case AwaitExpressionSyntax:
            case ThrowExpressionSyntax:
            case InterpolationSyntax:
            case ArgumentSyntax:
            case InvocationExpressionSyntax:
            case ObjectCreationExpressionSyntax:
            case ImplicitObjectCreationExpressionSyntax:
            case ArrayTypeSyntax:
            case ArrayCreationExpressionSyntax:
            case MemberBindingExpressionSyntax:
            case ElementBindingExpressionSyntax:
            case VariableDeclaratorSyntax:
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax }:
                return null;
            case MemberAccessExpressionSyntax access
                    when access.Expression == child:
                return null;
            case IsPatternExpressionSyntax test when test.Expression == child:
                return null;
            case SwitchExpressionSyntax choice
                    when choice.GoverningExpression == child:
                return null;
            case WithExpressionSyntax copy when copy.Expression == child:
                return null;
            case ConditionalAccessExpressionSyntax access:
                if (access.Expression == child)
                {
                    return null;
                }
                return Purity.MayNotRun + ": the part after ?.";
            case ArgumentListSyntax list:
                AddCallee(list.Parent);
                AddArguments(list.Arguments, child);
                return null;
            case BracketedArgumentListSyntax list:
                AddArguments(list.Arguments, child);
                return null;
            case ElementAccessExpressionSyntax element:
                if (element.ArgumentList == child)
                {
                    Earlier.Add(element.Expression);
                }
                return null;
            case BinaryExpressionSyntax binary:
                return Binary(binary, child);
            case AssignmentExpressionSyntax assignment:
                return Assignment(assignment, child);
            case ConditionalExpressionSyntax other:
                if (other.Condition == child)
                {
                    return null;
                }
                return Purity.MayNotRun + ": a branch of ?:";
            case InterpolatedStringExpressionSyntax text:
                foreach (var hole in text.Contents
                    .TakeWhile(part => part != child)
                    .OfType<InterpolationSyntax>())
                {
                    Earlier.Add(hole.Expression);
                    Formatted.Add(hole.Expression);
                }
                return null;
            case TupleExpressionSyntax tuple:
                AddArguments(tuple.Arguments, child);
                return null;
            case ArrayRankSpecifierSyntax rank:
                Earlier.AddRange(rank.Sizes.TakeWhile(size => size != child));
                return null;
            case RangeExpressionSyntax range:
                if (range.RightOperand == child && range.LeftOperand != null)
                {
                    Earlier.Add(range.LeftOperand);
                }
                return null;
            case VariableDeclarationSyntax declaration:
                return Declaration(declaration, child);
            case InitializerExpressionSyntax initializer:
                return Initializer(initializer);
            case SwitchExpressionArmSyntax:
                return Purity.MayNotRun + ": a switch expression arm";
            case WhenClauseSyntax:
                return Purity.MayNotRun + ": a when clause";
            case AnonymousFunctionExpressionSyntax:
                return Purity.MayNotRun + ": a lambda body";
            case ArrowExpressionClauseSyntax:
                return "an expression body: make it a block first";
            case CheckedExpressionSyntax:
                return "inside checked() or unchecked(), which the temporary "
                    + "would be outside of";
            default:
                return "not a context the rule knows: " + parent.Kind();
        }
    }

    /// An initializer always runs, but only after what it fills has been
    /// made, and its elements run in order, so a temporary in front of the
    /// statement would run before those.
    static string Initializer(InitializerExpressionSyntax initializer)
    {
        if (initializer.IsKind(SyntaxKind.ArrayInitializerExpression))
        {
            return "an array initializer: the array is made and the earlier "
                + "elements stored first";
        }
        if (initializer.IsKind(SyntaxKind.WithInitializerExpression))
        {
            return "a with initializer: the copy is made first";
        }
        return "an object or collection initializer: the constructor and the "
            + "earlier elements run first";
    }

    /// The left side runs first. The right side of &&, || and ?? runs only
    /// sometimes, and a string + makes its left side into text.
    string Binary(BinaryExpressionSyntax binary, SyntaxNode child)
    {
        if (binary.Left == child)
        {
            return null;
        }
        if (binary.IsKind(SyntaxKind.LogicalAndExpression)
            || binary.IsKind(SyntaxKind.LogicalOrExpression)
            || binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            return Purity.MayNotRun + ": the right side of &&, || or ??";
        }
        Earlier.Add(binary.Left);
        var type = _model.GetTypeInfo(binary).Type;
        if (binary.IsKind(SyntaxKind.AddExpression)
            && type != null && type.SpecialType == SpecialType.System_String)
        {
            Formatted.Add(binary.Left);
        }
        return null;
    }

    /// The target's parts run before the value; the right side of ??= runs
    /// only when the target is null.
    string Assignment(AssignmentExpressionSyntax assignment, SyntaxNode child)
    {
        if (assignment.Left == child)
        {
            return null;
        }
        if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            return Purity.MayNotRun + ": the right side of ??=";
        }
        if (assignment.Left is TupleExpressionSyntax
            || assignment.Left is DeclarationExpressionSyntax)
        {
            return "a deconstruction, whose targets' parts run first";
        }
        Earlier.AddRange(Purity.TargetParts(assignment));
        return null;
    }

    /// The declarators before this one run first. A for header runs its
    /// parts more than once or apart from the loop.
    string Declaration(VariableDeclarationSyntax declaration, SyntaxNode child)
    {
        switch (declaration.Parent)
        {
            case LocalDeclarationStatementSyntax:
            case UsingStatementSyntax:
                break;
            case ForStatementSyntax:
                return Purity.MayNotRun + ": a for header";
            default:
                return "not a local declaration: " + declaration.Parent.Kind();
        }
        foreach (var declarator in declaration.Variables)
        {
            if (declarator == child)
            {
                break;
            }
            if (declarator.Initializer != null)
            {
                Earlier.Add(declarator.Initializer.Value);
            }
        }
        return null;
    }

    /// What a call reads before its arguments: the object a method is
    /// called on, or the delegate it invokes.
    void AddCallee(SyntaxNode call)
    {
        if (call is not InvocationExpressionSyntax invocation)
        {
            return;
        }
        var callee = invocation.Expression;
        if (_model.GetSymbolInfo(callee).Symbol is IMethodSymbol)
        {
            if (callee is MemberAccessExpressionSyntax access)
            {
                Earlier.Add(access.Expression);
            }
            return;
        }
        Earlier.Add(callee);
    }

    /// Arguments run in the order they are written, named ones too.
    void AddArguments(
        SeparatedSyntaxList<ArgumentSyntax> arguments, SyntaxNode child)
    {
        foreach (var argument in arguments)
        {
            if (argument == child)
            {
                return;
            }
            Earlier.Add(argument.Expression);
        }
    }
}
