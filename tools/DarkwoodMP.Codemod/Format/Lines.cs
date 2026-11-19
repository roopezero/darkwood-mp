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

    /// True when nothing but whitespace comes before the statement on its
    /// line in the original source.
    public static bool StartsItsLine(StatementSyntax original)
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

    /// True for a block of several statements with a comment behind its
    /// closing brace that can go on the line of the block's header. Such a
    /// comment explains the whole block, not its last statement. A bare block
    /// has no header. The comments must keep their order, so no other comment
    /// may be inside the block, and the header's line can't already end in a
    /// line comment. The original tree only.
    public static bool CommentOnHeader(BlockSyntax block)
    {
        int behind = CommentsOf(block.CloseBraceToken.TrailingTrivia).Count;
        if (block.Statements.Count < 2
            || behind == 0
            || block.Parent is BlockSyntax
            || block.Parent is SwitchSectionSyntax)
        {
            return false;
        }
        var header = block.OpenBraceToken.GetPreviousToken();
        return !header.TrailingTrivia.Any(SyntaxKind.SingleLineCommentTrivia)
            && block.DescendantTrivia().Count(IsComment) == behind;
    }

    /// The last token of a block's header with the comments from behind the
    /// block's closing brace at the end of its line. The opening brace goes on
    /// the next line, so a line comment can't swallow it.
    public static SyntaxToken OnHeader(
        SyntaxToken header, SyntaxTriviaList comments)
    {
        var moved = Behind(header, comments);
        if (moved.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia))
        {
            return moved;
        }
        return moved.WithTrailingTrivia(moved.TrailingTrivia.Add(Break));
    }

    /// A block whose closing brace gave its comments to the block's header.
    public static BlockSyntax WithoutCloseComments(BlockSyntax block)
    {
        var close = block.CloseBraceToken;
        return block.WithCloseBraceToken(
            close.WithTrailingTrivia(LineEnds(close.TrailingTrivia)));
    }

    /// Comments behind a block's closing brace move in front of it, so they
    /// don't read as notes on the code after the block. They go behind the
    /// last statement if it ends in a semicolon, and otherwise on a line of
    /// their own at the end of the block. See CommentOnHeader for the blocks
    /// whose comment goes to the header instead.
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

    /// True for a case section on one line of one statement and "break;",
    /// with a comment closing the line behind the break. The comment explains
    /// the statement, not the break, so the layout stage hangs it on that
    /// statement. With more statements on the line, see FirstOfLine.
    public static bool CommentBehindBreak(SwitchSectionSyntax section)
    {
        var statements = section.Statements;
        if (statements.Count != 2
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

    /// When the statement at "last" ends a line of several statements and a
    /// comment closes that line, the index of the line's first statement;
    /// otherwise -1. The comment was written for the whole line, so the layout
    /// stage puts it above the first statement instead of leaving it on the
    /// last one. It can't pass another comment, so any comment inside the
    /// line's statements leaves it where it is, and a section of one statement
    /// and a break has its own rule (see CommentBehindBreak). The original tree
    /// only.
    public static int FirstOfLine(
        SyntaxList<StatementSyntax> statements, int last)
    {
        var closing = statements[last];
        var end = closing.GetLastToken();
        if (!end.IsKind(SyntaxKind.SemicolonToken)
            || CommentsOf(end.TrailingTrivia).Count == 0
            || !end.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia)
            || (closing.Parent is SwitchSectionSyntax section
                && CommentBehindBreak(section)))
        {
            return -1;
        }
        int first = last;
        while (first > 0
            && Same(statements[first - 1].GetFirstToken(), end)
            && Same(statements[first - 1].GetLastToken(), end))
        {
            first--;
        }
        if (first == last || !OnlyAtTheEnds(statements, first, last))
        {
            return -1;
        }
        return first;
    }

    /// True when the statements from first to last hold no comments but those
    /// above the first and behind the last.
    static bool OnlyAtTheEnds(
        SyntaxList<StatementSyntax> statements, int first, int last)
    {
        int all = 0;
        for (int i = first; i <= last; i++)
        {
            all += statements[i].DescendantTrivia().Count(IsComment);
        }
        return all == CommentsOf(statements[first].GetLeadingTrivia()).Count
            + CommentsOf(statements[last].GetTrailingTrivia()).Count;
    }

    /// The visited statements of a list, with the comment that closes a line
    /// of several statements (see FirstOfLine) above the line's first one.
    public static SyntaxList<StatementSyntax> CommentsAbove(
        SyntaxList<StatementSyntax> original,
        SyntaxList<StatementSyntax> visited)
    {
        for (int last = 1; last < original.Count; last++)
        {
            int first = FirstOfLine(original, last);
            if (first < 0)
            {
                continue;
            }
            var comments = CommentsOf(
                original[last].GetLastToken().TrailingTrivia);
            var closing = visited[last];
            var end = closing.GetLastToken();
            visited = visited.Replace(closing, closing.ReplaceToken(
                end, end.WithTrailingTrivia(LineEnds(end.TrailingTrivia))));
            bool startsLine = StartsItsLine(original[first]);
            visited = visited.Replace(
                visited[first], Under(visited[first], comments, startsLine));
        }
        return visited;
    }

    /// A statement under comments on lines of their own. One that shared its
    /// line with what came before gets a line break in front of the comments.
    static StatementSyntax Under(
        StatementSyntax statement, SyntaxTriviaList comments, bool startsLine)
    {
        var leading = statement.GetLeadingTrivia().ToList();
        if (!startsLine)
        {
            leading.Add(Break);
        }
        leading.AddRange(OwnLines(comments));
        return statement.WithLeadingTrivia(leading);
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
