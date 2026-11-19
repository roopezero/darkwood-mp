using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Format;

/// Puts every pair of braces that is on one line onto separate lines: blocks,
/// switch statements, type bodies and namespaces, the things the build's
/// DW0002 rule reports. It only decides where line breaks go, and where a
/// comment behind a closing brace, a "break;" or a line of several statements
/// ends up: the Formatter does the rest. Accessor lists ({ get; set; }) and
/// initializers stay on one line, as the rule allows, except an initializer
/// behind a multi-line argument list.
sealed class OneLineBraces : CSharpSyntaxRewriter
{
    /// The comments that go on a block's header line, by the header's last
    /// token. That token isn't part of the block, so it is found up front.
    readonly Dictionary<SyntaxToken, SyntaxTriviaList> _headers = new();

    public OneLineBraces(SyntaxNode root)
    {
        foreach (var block in root.DescendantNodes().OfType<BlockSyntax>())
        {
            if (Lines.CommentOnHeader(block))
            {
                _headers[block.OpenBraceToken.GetPreviousToken()] =
                    Lines.CommentsOf(block.CloseBraceToken.TrailingTrivia);
            }
        }
    }

    public override SyntaxToken VisitToken(SyntaxToken token)
    {
        var visited = base.VisitToken(token);
        if (_headers.TryGetValue(token, out var comments))
        {
            return Lines.OnHeader(visited, comments);
        }
        return visited;
    }

    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax)base.VisitBlock(node);
        visited = visited.WithStatements(
            Lines.CommentsAbove(node.Statements, visited.Statements));
        if (Lines.Same(node.OpenBraceToken, node.CloseBraceToken))
        {
            visited = Opened(visited);
        }
        if (Lines.CommentOnHeader(node))
        {
            return Lines.WithoutCloseComments(visited);
        }
        return Lines.CommentsInside(visited);
    }

    static BlockSyntax Opened(BlockSyntax block)
    {
        var comments = Lines.MovedComments(block.OpenBraceToken);
        var open = Lines.OpenLine(block.OpenBraceToken);
        if (block.Statements.Count == 0)
        {
            return block
                .WithOpenBraceToken(open)
                .WithCloseBraceToken(
                    Lines.CloseEmpty(block.CloseBraceToken, comments));
        }
        var first = block.Statements[0];
        return block
            .WithStatements(block.Statements.Replace(
                first, Lines.WithComments(first, comments)))
            .WithOpenBraceToken(open)
            .WithCloseBraceToken(Lines.CloseLine(block.CloseBraceToken));
    }

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
    {
        var visited = (SwitchSectionSyntax)base.VisitSwitchSection(node);
        visited = visited.WithStatements(
            Lines.CommentsAbove(node.Statements, visited.Statements));
        if (Lines.CommentBehindBreak(node))
        {
            visited = visited.WithStatements(
                Lines.CommentOffBreak(visited.Statements));
        }
        return KeptAligned(node, visited);
    }

    /// A statement over several lines that starts on its case label's line
    /// moves to a line of its own. Its old column goes with it, for the
    /// Formatter to replace: the Formatter then moves the continuation lines
    /// by as much as it moves the statement, so what lined up still does.
    static SwitchSectionSyntax KeptAligned(
        SwitchSectionSyntax node, SwitchSectionSyntax visited)
    {
        var first = node.Statements.FirstOrDefault();
        if (first == null
            || !Lines.Same(
                node.Labels.Last().GetLastToken(), first.GetFirstToken())
            || Lines.Same(first.GetFirstToken(), first.GetLastToken()))
        {
            return visited;
        }
        var text = node.SyntaxTree.GetText();
        int column = text.Lines.GetLinePosition(first.SpanStart).Character;
        var moved = visited.Statements[0];
        var leading = SyntaxFactory.TriviaList(
                Lines.Break, SyntaxFactory.Whitespace(new string(' ', column)))
            .AddRange(moved.GetLeadingTrivia());
        return visited.WithStatements(visited.Statements.Replace(
            moved, moved.WithLeadingTrivia(leading)));
    }

    public override SyntaxNode VisitSwitchStatement(SwitchStatementSyntax node)
    {
        var visited = (SwitchStatementSyntax)base.VisitSwitchStatement(node);
        if (!Lines.Same(node.OpenBraceToken, node.CloseBraceToken))
        {
            return visited;
        }
        var comments = Lines.MovedComments(visited.OpenBraceToken);
        var open = Lines.OpenLine(visited.OpenBraceToken);
        var sections = visited.Sections;
        if (sections.Count == 0)
        {
            return visited
                .WithOpenBraceToken(open)
                .WithCloseBraceToken(
                    Lines.CloseEmpty(visited.CloseBraceToken, comments));
        }
        // The Formatter puts the sections after the first on their own lines.
        sections = sections.Replace(
            sections[0], Lines.WithComments(sections[0], comments));
        return visited
            .WithSections(sections)
            .WithOpenBraceToken(open)
            .WithCloseBraceToken(Lines.CloseLine(visited.CloseBraceToken));
    }

    public override SyntaxNode VisitClassDeclaration(
        ClassDeclarationSyntax node)
    {
        return TypeBody(
            node, (ClassDeclarationSyntax)base.VisitClassDeclaration(node));
    }

    public override SyntaxNode VisitStructDeclaration(
        StructDeclarationSyntax node)
    {
        return TypeBody(
            node, (StructDeclarationSyntax)base.VisitStructDeclaration(node));
    }

    public override SyntaxNode VisitInterfaceDeclaration(
        InterfaceDeclarationSyntax node)
    {
        return TypeBody(
            node,
            (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node));
    }

    public override SyntaxNode VisitRecordDeclaration(
        RecordDeclarationSyntax node)
    {
        return TypeBody(
            node, (RecordDeclarationSyntax)base.VisitRecordDeclaration(node));
    }

    public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var visited = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node);
        if (!OnOneLine(node))
        {
            return visited;
        }
        // Every member gets its line: the break goes after each comma.
        var members = OnePerLine(visited.Members);
        var comments = Lines.MovedComments(visited.OpenBraceToken);
        var open = Lines.OpenLine(visited.OpenBraceToken);
        if (members.Count == 0)
        {
            return visited
                .WithOpenBraceToken(open)
                .WithCloseBraceToken(
                    Lines.CloseEmpty(visited.CloseBraceToken, comments));
        }
        members = members.Replace(
            members[0], Lines.WithComments(members[0], comments));
        return visited
            .WithMembers(members)
            .WithOpenBraceToken(open)
            .WithCloseBraceToken(Lines.CloseLine(visited.CloseBraceToken));
    }

    public override SyntaxNode VisitNamespaceDeclaration(
        NamespaceDeclarationSyntax node)
    {
        var visited =
            (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node);
        if (!Lines.Same(node.OpenBraceToken, node.CloseBraceToken))
        {
            return visited;
        }
        var comments = Lines.MovedComments(visited.OpenBraceToken);
        var open = Lines.OpenLine(visited.OpenBraceToken);
        if (visited.Members.Count == 0)
        {
            return visited
                .WithOpenBraceToken(open)
                .WithCloseBraceToken(
                    Lines.CloseEmpty(visited.CloseBraceToken, comments));
        }
        return visited
            .WithMembers(Separated(visited.Members, comments))
            .WithOpenBraceToken(open)
            .WithCloseBraceToken(Lines.CloseLine(visited.CloseBraceToken));
    }

    /// An object initializer behind a multi-line argument list gets a line
    /// per member. On one line, the Formatter leaves it under the list's
    /// closing parenthesis, where it looks like a block after the statement.
    public override SyntaxNode VisitObjectCreationExpression(
        ObjectCreationExpressionSyntax node)
    {
        var visited = (ObjectCreationExpressionSyntax)
            base.VisitObjectCreationExpression(node);
        if (!OpensInitializer(node.ArgumentList, node.Initializer))
        {
            return visited;
        }
        return visited.WithInitializer(OnePerLine(visited.Initializer));
    }

    public override SyntaxNode VisitImplicitObjectCreationExpression(
        ImplicitObjectCreationExpressionSyntax node)
    {
        var visited = (ImplicitObjectCreationExpressionSyntax)
            base.VisitImplicitObjectCreationExpression(node);
        if (!OpensInitializer(node.ArgumentList, node.Initializer))
        {
            return visited;
        }
        return visited.WithInitializer(OnePerLine(visited.Initializer));
    }

    static bool OpensInitializer(
        ArgumentListSyntax arguments, InitializerExpressionSyntax initializer)
    {
        return arguments != null
            && initializer != null
            && initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
            && initializer.Expressions.Count > 0
            && Lines.Same(
                initializer.OpenBraceToken, initializer.CloseBraceToken)
            && !Lines.Same(arguments.OpenParenToken, arguments.CloseParenToken);
    }

    static InitializerExpressionSyntax OnePerLine(
        InitializerExpressionSyntax initializer)
    {
        var comments = Lines.MovedComments(initializer.OpenBraceToken);
        var members = OnePerLine(initializer.Expressions);
        return initializer
            .WithOpenBraceToken(Lines.OpenLine(initializer.OpenBraceToken))
            .WithExpressions(members.Replace(
                members[0], Lines.WithComments(members[0], comments)))
            .WithCloseBraceToken(Lines.CloseLine(initializer.CloseBraceToken));
    }

    /// A line break after every comma but a trailing one, which is followed
    /// by the closing brace's own break.
    static SeparatedSyntaxList<T> OnePerLine<T>(SeparatedSyntaxList<T> items)
        where T : SyntaxNode
    {
        var parts = items.GetWithSeparators();
        var opened = new List<SyntaxNodeOrToken>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].IsToken && i < parts.Count - 1)
            {
                opened.Add(AfterComma(parts[i].AsToken()));
            }
            else
            {
                opened.Add(parts[i]);
            }
        }
        return SyntaxFactory.SeparatedList<T>(opened);
    }

    /// The comma's comment stays on the line of the member before it.
    static SyntaxToken AfterComma(SyntaxToken comma)
    {
        var comments = Lines.CommentsOf(comma.TrailingTrivia);
        var kept = Lines.Behind(comma.WithTrailingTrivia(), comments);
        return kept.WithTrailingTrivia(kept.TrailingTrivia.Add(Lines.Break));
    }

    static bool OnOneLine(BaseTypeDeclarationSyntax node)
    {
        // "record R(int X);" has no braces.
        return !node.OpenBraceToken.IsKind(SyntaxKind.None)
            && Lines.Same(node.OpenBraceToken, node.CloseBraceToken);
    }

    static SyntaxNode TypeBody(
        TypeDeclarationSyntax node, TypeDeclarationSyntax visited)
    {
        if (!OnOneLine(node))
        {
            return visited;
        }
        var comments = Lines.MovedComments(visited.OpenBraceToken);
        var open = Lines.OpenLine(visited.OpenBraceToken);
        if (visited.Members.Count == 0)
        {
            return visited
                .WithOpenBraceToken(open)
                .WithCloseBraceToken(
                    Lines.CloseEmpty(visited.CloseBraceToken, comments));
        }
        return visited
            .WithMembers(Separated(visited.Members, comments))
            .WithOpenBraceToken(open)
            .WithCloseBraceToken(Lines.CloseLine(visited.CloseBraceToken));
    }

    /// Every member after the first starts on its own line, and the first
    /// takes the comments that followed the opening brace.
    static SyntaxList<MemberDeclarationSyntax> Separated(
        SyntaxList<MemberDeclarationSyntax> members,
        SyntaxTriviaList comments)
    {
        for (int i = 1; i < members.Count; i++)
        {
            members = members.Replace(members[i], OnNewLine(members[i]));
        }
        return members.Replace(
            members[0], Lines.WithComments(members[0], comments));
    }

    static T OnNewLine<T>(T node) where T : SyntaxNode
    {
        return node.WithLeadingTrivia(
            node.GetLeadingTrivia().Insert(0, Lines.Break));
    }
}
