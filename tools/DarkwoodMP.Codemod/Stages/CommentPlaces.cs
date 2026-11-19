using DarkwoodMP.Codemod.Format;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Stages;

/// Whether every comment is still between the same two tokens of code. The
/// compiled DLL and the comment words can't show a comment that moved past
/// code, so the layout stage proves it here. Two moves are the stage's own:
/// from behind a block's closing brace to in front of it, and from behind the
/// "break;" of a case section on one line to the statement before the break.
static class CommentPlaces
{
    /// Null when the comments are where they were, or moved as the stage
    /// moves them; otherwise the first one that isn't.
    public static string Difference(SyntaxNode before, SyntaxNode after)
    {
        var was = Of(before);
        var now = Of(after);
        if (was.Count != now.Count)
        {
            return $"{was.Count} comments became {now.Count}";
        }
        for (int i = 0; i < was.Count; i++)
        {
            if (!Allowed(was[i], now[i]))
            {
                return $"the comment \"{was[i].Words}\" moved or changed: it "
                    + $"was after {was[i].Tokens} tokens of code, and "
                    + $"\"{now[i].Words}\" is after {now[i].Tokens}";
            }
        }
        return null;
    }

    static bool Allowed(Place was, Place now)
    {
        if (was.Words != now.Words)
        {
            return false;
        }
        return now.Tokens == was.Tokens
            || (was.Back > 0 && now.Tokens == was.Tokens - was.Back);
    }

    /// Every comment with the number of code tokens in front of it, and how
    /// many of those the layout stage may move it back over.
    static List<Place> Of(SyntaxNode root)
    {
        var found = new List<Place>();
        int code = 0;
        foreach (var token in root.DescendantTokens())
        {
            Collect(token.LeadingTrivia, code, 0, found);
            code++;
            Collect(token.TrailingTrivia, code, Back(token), found);
        }
        return found;
    }

    /// The closing brace itself, or the "break" and ";" of a section whose
    /// comment goes to the statement before the break.
    static int Back(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.CloseBraceToken)
            && token.Parent is BlockSyntax)
        {
            return 1;
        }
        if (token.Parent is BreakStatementSyntax stop
            && stop.SemicolonToken == token
            && stop.Parent is SwitchSectionSyntax section
            && section.Statements.Last() == stop
            && Lines.CommentBehindBreak(section))
        {
            return 2;
        }
        return 0;
    }

    static void Collect(
        SyntaxTriviaList trivia, int code, int back, List<Place> found)
    {
        foreach (var item in trivia)
        {
            if (Lines.IsComment(item))
            {
                var words = item.ToFullString().Split(
                    (char[])null, StringSplitOptions.RemoveEmptyEntries);
                found.Add(new Place(code, string.Join(" ", words), back));
            }
        }
    }

    readonly record struct Place(int Tokens, string Words, int Back);
}
