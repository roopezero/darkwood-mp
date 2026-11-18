using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Stages;

/// Whether two trees hold the same code, whatever the whitespace and comments.
static class SameCode
{
    /// Uses the overload that walks the children. The topLevel overload can
    /// report two parses of the same code as different when they only differ
    /// in layout: "try { return 1; } catch { return 0; }" on one line and the
    /// same code over several lines are not "equivalent" with it. (The parser
    /// can wrap a single statement in a list node; the child walk sees through
    /// that wrapper.)
    public static bool Of(SyntaxNode a, SyntaxNode b)
    {
        return SyntaxFactory.AreEquivalent(a, b, ignoreChildNode: _ => false);
    }
}
