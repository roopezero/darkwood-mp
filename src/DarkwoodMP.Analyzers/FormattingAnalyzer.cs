using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Analyzers
{
    /// The formatting rules of docs/ARCHITECTURE.md that .editorconfig can't
    /// express. Every rule defaults to a warning; .editorconfig raises them
    /// to errors folder by folder.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FormattingAnalyzer : DiagnosticAnalyzer
    {
        public const int MaxColumns = 80;

        const string Category = "DarkwoodFormatting";

        static readonly DiagnosticDescriptor LineTooLong =
            new DiagnosticDescriptor(
                "DW0001",
                "Line over 80 columns",
                "Line is {0} columns long; the limit is 80",
                Category,
                DiagnosticSeverity.Warning,
                true);

        static readonly DiagnosticDescriptor OneLineBlock =
            new DiagnosticDescriptor(
                "DW0002",
                "One-line block",
                "Put the braces of this block on their own lines",
                Category,
                DiagnosticSeverity.Warning,
                true);

        static readonly DiagnosticDescriptor Ternary =
            new DiagnosticDescriptor(
                "DW0003",
                "Ternary operator",
                "Write an if and an else instead of the ternary operator",
                Category,
                DiagnosticSeverity.Warning,
                true);

        static readonly DiagnosticDescriptor Coalesce =
            new DiagnosticDescriptor(
                "DW0004",
                "Null-coalescing operator",
                "Check for null with an if instead of the ?? operator",
                Category,
                DiagnosticSeverity.Warning,
                true);

        static readonly DiagnosticDescriptor CoalesceAssign =
            new DiagnosticDescriptor(
                "DW0005",
                "Null-coalescing assignment",
                "Check for null with an if instead of the ??= operator",
                Category,
                DiagnosticSeverity.Warning,
                true);

        static readonly DiagnosticDescriptor NullConditional =
            new DiagnosticDescriptor(
                "DW0006",
                "Null-conditional operator",
                "Check for null with an if instead of a null-conditional",
                Category,
                DiagnosticSeverity.Warning,
                true);

        public override ImmutableArray<DiagnosticDescriptor>
            SupportedDiagnostics =>
                ImmutableArray.Create(
                    LineTooLong,
                    OneLineBlock,
                    Ternary,
                    Coalesce,
                    CoalesceAssign,
                    NullConditional);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxTreeAction(CheckLines);
            context.RegisterSyntaxNodeAction(
                CheckBraces,
                SyntaxKind.Block,
                SyntaxKind.SwitchStatement,
                SyntaxKind.ClassDeclaration,
                SyntaxKind.StructDeclaration,
                SyntaxKind.InterfaceDeclaration,
                SyntaxKind.RecordDeclaration,
                SyntaxKind.RecordStructDeclaration,
                SyntaxKind.EnumDeclaration,
                SyntaxKind.NamespaceDeclaration);
            context.RegisterSyntaxNodeAction(
                CheckTernary, SyntaxKind.ConditionalExpression);
            context.RegisterSyntaxNodeAction(
                CheckCoalesce, SyntaxKind.CoalesceExpression);
            context.RegisterSyntaxNodeAction(
                CheckCoalesceAssign,
                SyntaxKind.CoalesceAssignmentExpression);
            context.RegisterSyntaxNodeAction(
                CheckNullConditional,
                SyntaxKind.ConditionalAccessExpression);
        }

        static void CheckLines(SyntaxTreeAnalysisContext context)
        {
            var text = context.Tree.GetText(context.CancellationToken);
            foreach (var line in text.Lines)
            {
                // Span leaves out the line break, so CRLF and LF files
                // measure the same.
                var length = line.Span.Length;
                if (length <= MaxColumns)
                {
                    continue;
                }

                var over = TextSpan.FromBounds(
                    line.Start + MaxColumns, line.End);
                var place = Location.Create(context.Tree, over);
                context.ReportDiagnostic(
                    Diagnostic.Create(LineTooLong, place, length));
            }
        }

        static void CheckBraces(SyntaxNodeAnalysisContext context)
        {
            SyntaxToken open;
            SyntaxToken close;
            switch (context.Node)
            {
                case BlockSyntax block:
                    open = block.OpenBraceToken;
                    close = block.CloseBraceToken;
                    break;
                case SwitchStatementSyntax choice:
                    open = choice.OpenBraceToken;
                    close = choice.CloseBraceToken;
                    break;
                case BaseTypeDeclarationSyntax type:
                    open = type.OpenBraceToken;
                    close = type.CloseBraceToken;
                    break;
                case NamespaceDeclarationSyntax space:
                    open = space.OpenBraceToken;
                    close = space.CloseBraceToken;
                    break;
                default:
                    return;
            }

            // "record R(int X);" has no braces, and broken code may miss one.
            var absent = open.IsKind(SyntaxKind.None);
            if (absent || open.IsMissing || close.IsMissing)
            {
                return;
            }

            var tree = context.Node.SyntaxTree;
            var lines = tree.GetText(context.CancellationToken).Lines;
            var first = lines.IndexOf(open.SpanStart);
            var last = lines.IndexOf(close.SpanStart);
            if (first != last)
            {
                return;
            }

            var span = TextSpan.FromBounds(open.SpanStart, close.Span.End);
            context.ReportDiagnostic(
                Diagnostic.Create(OneLineBlock, Location.Create(tree, span)));
        }

        static void CheckTernary(SyntaxNodeAnalysisContext context)
        {
            var node = (ConditionalExpressionSyntax)context.Node;
            Report(context, Ternary, node.QuestionToken);
        }

        static void CheckCoalesce(SyntaxNodeAnalysisContext context)
        {
            var node = (BinaryExpressionSyntax)context.Node;
            Report(context, Coalesce, node.OperatorToken);
        }

        static void CheckCoalesceAssign(SyntaxNodeAnalysisContext context)
        {
            var node = (AssignmentExpressionSyntax)context.Node;
            Report(context, CoalesceAssign, node.OperatorToken);
        }

        static void CheckNullConditional(SyntaxNodeAnalysisContext context)
        {
            var node = (ConditionalAccessExpressionSyntax)context.Node;
            Report(context, NullConditional, node.OperatorToken);
        }

        static void Report(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor rule,
            SyntaxToken token)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(rule, token.GetLocation()));
        }
    }
}
