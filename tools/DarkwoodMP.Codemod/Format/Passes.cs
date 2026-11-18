using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace DarkwoodMP.Codemod.Format;

/// The two whitespace stages as functions from source text to source text.
/// Each one plans its edits on the tree of the text it was given and runs the
/// Formatter on the result straight away: reparsing in between would lose the
/// elastic trivia the Formatter relies on.
static class Passes
{
    /// Stage 1: every braceless statement gets a block. Only the new blocks
    /// are formatted, so the rest of the file stays as it was.
    public static string Braces(string source, CSharpParseOptions parse)
    {
        var root = CSharpSyntaxTree.ParseText(source, parse).GetRoot();
        var rewriter = new BraceRewriter();
        var rewritten = rewriter.Visit(root);
        if (rewriter.Added == 0)
        {
            return source;
        }
        var formatted = Formatter.Format(
            rewritten,
            Formatter.Annotation,
            CodeStyle.Workspace,
            CodeStyle.BraceOptions);
        return formatted.ToFullString();
    }

    /// Stage 2: braces on their own lines, one statement per line and the
    /// Formatter over the whole file.
    public static string Layout(string source, CSharpParseOptions parse)
    {
        var root = CSharpSyntaxTree.ParseText(source, parse).GetRoot();
        var opened = new OneLineBraces().Visit(root);
        var formatted = Formatter.Format(
            opened, CodeStyle.Workspace, CodeStyle.Options);
        return formatted.ToFullString();
    }
}
