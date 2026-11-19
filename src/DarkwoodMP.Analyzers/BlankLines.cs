using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Analyzers
{
    /// Where rule DW0007 wants a blank line: after a statement, member or
    /// accessor that holds a { } pair spread over more than one line, unless
    /// the next token is a } or the file ends. FormattingAnalyzer reports
    /// these places. Find is public so that a codemod stage can add the
    /// missing lines from the same definition.
    public static class BlankLines
    {
        /// The last token of each statement, member and accessor that needs
        /// a blank line after it and has none, in the order of the file.
        public static ImmutableArray<SyntaxToken> Find(
            SyntaxTree tree,
            CancellationToken cancel = default)
        {
            var root = tree.GetRoot(cancel);
            var lines = tree.GetText(cancel).Lines;

            // One pass over the tree, in the order of the file: where each
            // { } pair spread over lines opens, and the nodes to check.
            var opens = new List<int>();
            var checks = new List<SyntaxNode>();
            foreach (var item in root.DescendantNodesAndTokens())
            {
                if (item.IsNode)
                {
                    if (IsChecked(item.AsNode()))
                    {
                        checks.Add(item.AsNode());
                    }

                    continue;
                }

                var token = item.AsToken();
                if (OpensSpread(token, lines))
                {
                    opens.Add(token.SpanStart);
                }
            }

            var found = new List<SyntaxToken>();
            foreach (var node in checks)
            {
                if (Holds(opens, node.Span) && LacksBlank(node, lines))
                {
                    found.Add(node.GetLastToken());
                }
            }

            // A member is checked before the statements in it, and its last
            // token comes after theirs.
            found.Sort((a, b) => a.SpanStart.CompareTo(b.SpanStart));
            return found.ToImmutableArray();
        }

        /// A statement in a block's or a switch section's statement list, a
        /// member of a type or a namespace, or an accessor. A file's own
        /// declarations are members of the global namespace, and so is each
        /// top-level statement: Roslyn wraps it in a GlobalStatementSyntax,
        /// a member of the file.
        static bool IsChecked(SyntaxNode node)
        {
            var parent = node.Parent;
            if (node is StatementSyntax)
            {
                return parent is BlockSyntax || parent is SwitchSectionSyntax;
            }

            if (node is MemberDeclarationSyntax)
            {
                return parent is BaseTypeDeclarationSyntax
                    || parent is BaseNamespaceDeclarationSyntax
                    || parent is CompilationUnitSyntax;
            }

            return node is AccessorDeclarationSyntax;
        }

        /// Whether token is the { of a pair whose } is on another line. Both
        /// braces belong to the same node, whatever its kind; the braces of
        /// an interpolation in $"..." don't count.
        static bool OpensSpread(SyntaxToken token, TextLineCollection lines)
        {
            if (!token.IsKind(SyntaxKind.OpenBraceToken) || token.IsMissing)
            {
                return false;
            }

            var owner = token.Parent;
            if (owner.IsKind(SyntaxKind.Interpolation))
            {
                return false;
            }

            foreach (var close in owner.ChildTokens())
            {
                if (close.IsKind(SyntaxKind.CloseBraceToken))
                {
                    var first = lines.IndexOf(token.SpanStart);
                    var last = lines.IndexOf(close.SpanStart);
                    return !close.IsMissing && first != last;
                }
            }

            return false;
        }

        /// Whether a pair opens inside span. opens is in the order of the
        /// file, so this is a binary search.
        static bool Holds(List<int> opens, TextSpan span)
        {
            var index = opens.BinarySearch(span.Start);
            if (index < 0)
            {
                index = ~index;
            }

            return index < opens.Count && opens[index] < span.End;
        }

        /// Whether the line after node's last token should be blank and
        /// isn't.
        static bool LacksBlank(SyntaxNode node, TextLineCollection lines)
        {
            // GetNextToken skips comments and directives. The end of the
            // file has no width, so past the last token it gives None.
            var last = node.GetLastToken();
            var next = last.GetNextToken();
            var end = next.IsKind(SyntaxKind.None);
            if (end || next.IsKind(SyntaxKind.CloseBraceToken))
            {
                return false;
            }

            // A next token on the same line is IDE0055's and DW0002's case.
            var line = lines.IndexOf(last.Span.End);
            if (lines.IndexOf(next.SpanStart) == line)
            {
                return false;
            }

            return !IsBlank(lines[line + 1]);
        }

        /// Whether line holds only whitespace. A comment or a directive
        /// isn't blank.
        static bool IsBlank(TextLine line)
        {
            var text = line.Text;
            for (var at = line.Start; at < line.End; at++)
            {
                if (!char.IsWhiteSpace(text[at]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
