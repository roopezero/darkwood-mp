using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Format;

/// Puts every pair of braces that is on one line onto separate lines: blocks,
/// switch statements, type bodies and namespaces, the things the build's
/// DW0002 rule reports. It only decides where line breaks go: the Formatter
/// does the rest. Accessor lists ({ get; set; }) and initializers stay on one
/// line, as the rule allows.
sealed class OneLineBraces : CSharpSyntaxRewriter
{
    public override SyntaxNode VisitBlock(BlockSyntax node)
    {
        var visited = (BlockSyntax)base.VisitBlock(node);
        if (!Lines.Same(node.OpenBraceToken, node.CloseBraceToken))
        {
            return visited;
        }
        var comments = Lines.MovedComments(visited.OpenBraceToken);
        var open = Lines.OpenLine(visited.OpenBraceToken);
        if (visited.Statements.Count == 0)
        {
            return visited
                .WithOpenBraceToken(open)
                .WithCloseBraceToken(
                    Lines.CloseEmpty(visited.CloseBraceToken, comments));
        }
        var first = visited.Statements[0];
        return visited
            .WithStatements(visited.Statements.Replace(
                first, Lines.WithComments(first, comments)))
            .WithOpenBraceToken(open)
            .WithCloseBraceToken(Lines.CloseLine(visited.CloseBraceToken));
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
        for (int i = 1; i < sections.Count; i++)
        {
            sections = sections.Replace(sections[i], OnNewLine(sections[i]));
        }
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
        var parts = visited.Members.GetWithSeparators();
        var opened = new List<SyntaxNodeOrToken>();
        for (int i = 0; i < parts.Count; i++)
        {
            // A trailing comma is followed by the closing brace's own break.
            if (parts[i].IsToken && i < parts.Count - 1)
            {
                opened.Add(AfterComma(parts[i].AsToken()));
            }
            else
            {
                opened.Add(parts[i]);
            }
        }
        var members =
            SyntaxFactory.SeparatedList<EnumMemberDeclarationSyntax>(opened);
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

    /// The comma's comment stays on the line of the member before it.
    static SyntaxToken AfterComma(SyntaxToken comma)
    {
        return comma.WithTrailingTrivia(
            Lines.CommentsOf(comma.TrailingTrivia).Add(Lines.Break));
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
