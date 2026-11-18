using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public static SyntaxToken CloseLine(SyntaxToken close)
    {
        return close.WithLeadingTrivia(
            CommentsOf(close.LeadingTrivia).Insert(0, Break));
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
}
