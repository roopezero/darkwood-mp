using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Format;

/// Line facts and trivia helpers shared by the rewriters.
static class Lines
{
    /// A line break the Formatter must keep. Elastic trivia is only a hint and
    /// could be joined back onto one line.
    public static readonly SyntaxTrivia Break = SyntaxFactory.LineFeed;

    /// True when both tokens are on the same line of the original source.
    public static bool Same(SyntaxToken open, SyntaxToken close)
    {
        var lines = open.SyntaxTree.GetText().Lines;
        return lines.GetLineFromPosition(open.SpanStart).LineNumber
            == lines.GetLineFromPosition(close.SpanStart).LineNumber;
    }

    /// The comments in a trivia list, without the spaces and line breaks.
    public static SyntaxTriviaList CommentsOf(SyntaxTriviaList list)
    {
        return SyntaxFactory.TriviaList(list.Where(IsComment));
    }

    public static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    /// An opening brace with a line break after it. Comments that followed
    /// the brace move in front of what comes next: see MoveComments.
    public static SyntaxToken OpenLine(SyntaxToken open)
    {
        return open.WithTrailingTrivia(Break);
    }

    /// A closing brace on its own line, after something on the line before.
    /// Nothing else can be in front of the brace of a one-line body.
    public static SyntaxToken CloseLine(SyntaxToken close)
    {
        return close.WithLeadingTrivia(Break);
    }

    /// The comments that followed an opening brace, to keep them with what
    /// came after the brace.
    public static SyntaxTriviaList MovedComments(SyntaxToken open)
    {
        return CommentsOf(open.TrailingTrivia);
    }

    /// The first item of a body, with the comments from the brace in front.
    public static T WithComments<T>(T first, SyntaxTriviaList comments)
        where T : SyntaxNode
    {
        if (comments.Count == 0)
        {
            return first;
        }
        return first.WithLeadingTrivia(
            comments.AddRange(first.GetLeadingTrivia()));
    }

    /// The closing brace of an empty body, under the comments it held.
    public static SyntaxToken CloseEmpty(
        SyntaxToken close, SyntaxTriviaList comments)
    {
        if (comments.Count == 0)
        {
            return close;
        }
        return close.WithLeadingTrivia(comments.Add(Break));
    }

    /// Comments behind a block's closing brace move in front of it, so they
    /// don't read as notes on the code after the block. They go behind the
    /// last statement if it ends in a semicolon, and otherwise on a line of
    /// their own at the end of the block.
    public static BlockSyntax CommentsInside(BlockSyntax block)
    {
        var close = block.CloseBraceToken;
        var comments = CommentsOf(close.TrailingTrivia);
        if (comments.Count == 0)
        {
            return block;
        }
        var bare = close.WithTrailingTrivia(LineEnds(close.TrailingTrivia));
        if (TrailsLastStatement(block))
        {
            var last = block.Statements.Last().GetLastToken();
            return block
                .ReplaceToken(last, Behind(last, comments))
                .WithCloseBraceToken(bare);
        }
        return block.WithCloseBraceToken(bare.WithLeadingTrivia(
            close.LeadingTrivia.AddRange(OwnLines(comments))));
    }

    /// Whether comments can go at the end of the last statement's line.
    /// Comments already in front of the brace must stay in front of the moved
    /// ones, and a line comment needs a line break behind it.
    static bool TrailsLastStatement(BlockSyntax block)
    {
        var close = block.CloseBraceToken;
        if (block.Statements.Count == 0
            || CommentsOf(close.LeadingTrivia).Count > 0)
        {
            return false;
        }
        var last = block.Statements.Last().GetLastToken();
        var breaksLine = last.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia)
            || close.LeadingTrivia.FirstOrDefault()
                .IsKind(SyntaxKind.EndOfLineTrivia);
        return breaksLine
            && last.IsKind(SyntaxKind.SemicolonToken)
            && !last.TrailingTrivia.Any(SyntaxKind.SingleLineCommentTrivia);
    }

    /// True for a case section on one line that ends in "break;" after another
    /// statement, with a comment closing the line behind the break. The
    /// comment explains the section, not the break, so the layout stage hangs
    /// it on the statement before the break.
    public static bool CommentBehindBreak(SwitchSectionSyntax section)
    {
        var statements = section.Statements;
        if (statements.Count < 2
            || statements.Last() is not BreakStatementSyntax)
        {
            return false;
        }
        var end = statements.Last().GetLastToken();
        var prior = statements[statements.Count - 2].GetLastToken();
        return CommentsOf(end.TrailingTrivia).Count > 0
            && end.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia)
            && prior.IsKind(SyntaxKind.SemicolonToken)
            && Same(section.GetFirstToken(), end);
    }

    /// Moves the comment behind the final "break;" to the statement before it.
    /// The break starts its own line, under the comment.
    public static SyntaxList<StatementSyntax> CommentOffBreak(
        SyntaxList<StatementSyntax> statements)
    {
        int last = statements.Count - 1;
        var stop = statements[last];
        var end = stop.GetLastToken();
        var comments = CommentsOf(end.TrailingTrivia);
        var bare = stop.ReplaceToken(
            end, end.WithTrailingTrivia(LineEnds(end.TrailingTrivia)));
        var prior = statements[last - 1];
        var priorEnd = prior.GetLastToken();
        statements = statements.Replace(
            prior, prior.ReplaceToken(priorEnd, Behind(priorEnd, comments)));
        return statements.Replace(
            statements[last],
            bare.WithLeadingTrivia(bare.GetLeadingTrivia().Insert(0, Break)));
    }

    /// A token with comments added at the end of its line.
    public static SyntaxToken Behind(
        SyntaxToken token, SyntaxTriviaList comments)
    {
        var line = token.TrailingTrivia
            .TakeWhile(t => !t.IsKind(SyntaxKind.EndOfLineTrivia))
            .ToList();
        while (line.Count > 0 && line[^1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            line.RemoveAt(line.Count - 1);
        }
        foreach (var comment in comments)
        {
            line.Add(SyntaxFactory.Space);
            line.Add(comment);
        }
        line.AddRange(token.TrailingTrivia.SkipWhile(
            t => !t.IsKind(SyntaxKind.EndOfLineTrivia)));
        return token.WithTrailingTrivia(line);
    }

    /// Only the line breaks of a trivia list.
    static SyntaxTriviaList LineEnds(SyntaxTriviaList list)
    {
        return SyntaxFactory.TriviaList(
            list.Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)));
    }

    /// Each comment followed by a line break.
    static SyntaxTriviaList OwnLines(SyntaxTriviaList comments)
    {
        var list = new List<SyntaxTrivia>();
        foreach (var comment in comments)
        {
            list.Add(comment);
            list.Add(Break);
        }
        return SyntaxFactory.TriviaList(list);
    }
}
