using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DarkwoodMP.Codemod.Format;

/// Wraps the embedded statement of every if, else, for, foreach, while, do,
/// using, lock and fixed in a block. "else if" stays as it is, and each using
/// of a stack gets its own block.
sealed class BraceRewriter : CSharpSyntaxRewriter
{
    /// How many statements were wrapped.
    public int Added { get; private set; }

    public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
    {
        var visited = (IfStatementSyntax)base.VisitIfStatement(node);
        if (visited.Statement is not BlockSyntax)
        {
            visited = visited.WithStatement(
                Wrap(node.Statement, visited.Statement));
        }
        var otherwise = visited.Else;
        if (otherwise != null
            && otherwise.Statement is not BlockSyntax
            && otherwise.Statement is not IfStatementSyntax)
        {
            visited = visited.WithElse(otherwise.WithStatement(
                Wrap(node.Else.Statement, otherwise.Statement)));
        }
        return visited;
    }

    public override SyntaxNode VisitForStatement(ForStatementSyntax node)
    {
        var visited = (ForStatementSyntax)base.VisitForStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitForEachStatement(
        ForEachStatementSyntax node)
    {
        var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitForEachVariableStatement(
        ForEachVariableStatementSyntax node)
    {
        var visited = (ForEachVariableStatementSyntax)
            base.VisitForEachVariableStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
    {
        var visited = (WhileStatementSyntax)base.VisitWhileStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
    {
        var visited = (DoStatementSyntax)base.VisitDoStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitUsingStatement(UsingStatementSyntax node)
    {
        var visited = (UsingStatementSyntax)base.VisitUsingStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitLockStatement(LockStatementSyntax node)
    {
        var visited = (LockStatementSyntax)base.VisitLockStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    public override SyntaxNode VisitFixedStatement(FixedStatementSyntax node)
    {
        var visited = (FixedStatementSyntax)base.VisitFixedStatement(node);
        return visited.WithStatement(
            Braced(node.Statement, visited.Statement));
    }

    /// A one-line block with a newly wrapped statement inside it spreads over
    /// several lines anyway, so its own braces move to their lines now.
    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax)base.VisitBlock(node);
        if (visited == node
            || !Lines.Same(node.OpenBraceToken, node.CloseBraceToken))
        {
            return visited;
        }
        var comments = Lines.MovedComments(visited.OpenBraceToken);
        var statements = visited.Statements.Replace(
            visited.Statements[0],
            Lines.WithComments(visited.Statements[0], comments));
        return visited
            .WithStatements(statements)
            .WithOpenBraceToken(Lines.OpenLine(visited.OpenBraceToken))
            .WithCloseBraceToken(Lines.CloseLine(visited.CloseBraceToken))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    StatementSyntax Braced(StatementSyntax original, StatementSyntax visited)
    {
        if (visited is BlockSyntax)
        {
            return visited;
        }
        return Wrap(original, visited);
    }

    BlockSyntax Wrap(StatementSyntax original, StatementSyntax visited)
    {
        Added++;
        var trailing = visited.GetTrailingTrivia().ToList();
        var closeTrailing = SyntaxFactory.TriviaList();
        if (trailing.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia)))
        {
            // A line comment keeps its own line break, or it would swallow
            // the closing brace. The brace gets one of its own.
            closeTrailing = SyntaxFactory.TriviaList(Lines.Break);
        }
        else
        {
            int lineEnd = trailing.FindLastIndex(
                t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            if (lineEnd >= 0)
            {
                closeTrailing = SyntaxFactory.TriviaList(
                    trailing.Skip(lineEnd));
                trailing = trailing.Take(lineEnd).ToList();
            }
        }
        var inner = visited
            .WithLeadingTrivia(Before(visited.GetLeadingTrivia()))
            .WithTrailingTrivia(trailing);
        var block = SyntaxFactory.Block(inner);
        if (StartsItsLine(original))
        {
            // The statement keeps its indentation for now: the Formatter
            // moves its continuation lines by as much as it moves the
            // statement, so a call's aligned arguments stay aligned.
            block = block.WithOpenBraceToken(
                block.OpenBraceToken.WithTrailingTrivia(Lines.Break));
        }
        else
        {
            inner = inner.WithLeadingTrivia(
                SyntaxFactory.TriviaList(
                    inner.GetLeadingTrivia().Where(Lines.IsComment)));
            block = SyntaxFactory.Block(inner);
        }
        return block
            .WithCloseBraceToken(
                block.CloseBraceToken.WithTrailingTrivia(closeTrailing))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    static bool StartsItsLine(StatementSyntax original)
    {
        var first = original.GetFirstToken();
        var previous = first.GetPreviousToken();
        if (previous.IsKind(SyntaxKind.None))
        {
            return true;
        }
        var lines = original.SyntaxTree.GetText().Lines;
        return lines.GetLineFromPosition(previous.Span.End).LineNumber
            != lines.GetLineFromPosition(first.SpanStart).LineNumber;
    }

    /// What stays in front of a statement that moves into a block: comments
    /// above it and its indentation. Blank lines before the first comment go.
    static SyntaxTriviaList Before(SyntaxTriviaList leading)
    {
        var kept = leading.ToList();
        int comment = kept.FindIndex(Lines.IsComment);
        if (comment >= 0)
        {
            return SyntaxFactory.TriviaList(kept.Skip(comment));
        }
        // Only the indentation in front of the statement itself.
        var last = kept.LastOrDefault();
        if (last.IsKind(SyntaxKind.WhitespaceTrivia))
        {
            return SyntaxFactory.TriviaList(last);
        }
        return SyntaxFactory.TriviaList();
    }
}
