using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Census;

/// What the braces and layout stages work on: statements without braces,
/// blocks and bodies on one line, and opening braces that end a line.
sealed class BraceCensus
{
    public readonly Tally Braceless =
        new Tally("braceless embedded statements");

    public readonly Tally OneLineBlocks = new Tally("one-line blocks");

    /// The other braces DW0002 reports: switch statements, type bodies and
    /// namespaces on one line.
    public readonly Tally OneLineBodies =
        new Tally("one-line switch statements, type bodies and namespaces");

    /// "if (x) ;" and "else ;"; each still counts as braceless above.
    public readonly Tally Empty = new Tally("embedded empty statements");

    public readonly Tally ElseIf = new Tally("else and if on separate lines");

    public int OpenBracesEndingALine;

    public void Count(SyntaxNode root, string file)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case IfStatementSyntax s:
                    CountIf(s, file);
                    break;
                case ForStatementSyntax s:
                    Embedded("for", s.Statement, file);
                    break;
                case ForEachStatementSyntax s:
                    Embedded("foreach", s.Statement, file);
                    break;
                case ForEachVariableStatementSyntax s:
                    Embedded("foreach", s.Statement, file);
                    break;
                case WhileStatementSyntax s:
                    Embedded("while", s.Statement, file);
                    break;
                case DoStatementSyntax s:
                    Embedded("do", s.Statement, file);
                    break;
                case UsingStatementSyntax s:
                    Embedded(UsingKey(s), s.Statement, file);
                    break;
                case LockStatementSyntax s:
                    Embedded("lock", s.Statement, file);
                    break;
                case FixedStatementSyntax s:
                    Embedded("fixed", s.Statement, file);
                    break;
                case BlockSyntax block:
                    CountBlock(block, file);
                    break;
                case SwitchStatementSyntax s:
                    CountBody("switch", s.OpenBraceToken, s.CloseBraceToken, file);
                    break;
                case BaseTypeDeclarationSyntax t:
                    CountBody(TypeWord(t), t.OpenBraceToken, t.CloseBraceToken, file);
                    break;
                case NamespaceDeclarationSyntax n:
                    CountBody("namespace", n.OpenBraceToken, n.CloseBraceToken, file);
                    break;
            }
        }
    }

    static string TypeWord(BaseTypeDeclarationSyntax type)
    {
        if (type is TypeDeclarationSyntax declared)
        {
            return declared.Keyword.Text;
        }
        return "enum";
    }

    static string UsingKey(UsingStatementSyntax node)
    {
        if (node.Statement is UsingStatementSyntax)
        {
            return "using (stacked)";
        }
        return "using";
    }

    void CountIf(IfStatementSyntax node, string file)
    {
        Embedded("if", node.Statement, file);
        var otherwise = node.Else;
        if (otherwise == null)
        {
            return;
        }
        if (otherwise.Statement is IfStatementSyntax chained)
        {
            CountElseIf(otherwise, chained, file);
            return;
        }
        Embedded("else", otherwise.Statement, file);
    }

    void CountElseIf(
        ElseClauseSyntax otherwise, IfStatementSyntax chained, string file)
    {
        var elseWord = otherwise.ElseKeyword;
        var ifWord = chained.IfKeyword;
        if (Format.Lines.Same(elseWord, ifWord))
        {
            return;
        }
        bool comment = elseWord.TrailingTrivia.Any(Format.Lines.IsComment)
            || ifWord.LeadingTrivia.Any(Format.Lines.IsComment);
        if (comment)
        {
            ElseIf.Add("with a comment between them", file);
        }
        else
        {
            ElseIf.Add("no comment", file);
        }
    }

    void Embedded(string kind, StatementSyntax statement, string file)
    {
        if (statement is BlockSyntax)
        {
            return;
        }
        Braceless.Add(kind, file);
        if (statement is EmptyStatementSyntax)
        {
            Empty.Add(kind, file);
        }
    }

    void CountBlock(BlockSyntax block, string file)
    {
        var open = block.OpenBraceToken;
        if (Format.Lines.Same(open, block.CloseBraceToken))
        {
            OneLineBlocks.Add(EmptyOrNot(block), file);
        }
        var previous = open.GetPreviousToken();
        if (!previous.IsKind(SyntaxKind.None)
            && Format.Lines.Same(previous, open))
        {
            OpenBracesEndingALine++;
        }
    }

    static string EmptyOrNot(BlockSyntax block)
    {
        if (block.Statements.Count == 0)
        {
            return "empty { }";
        }
        return "with statements";
    }

    void CountBody(
        string kind, SyntaxToken open, SyntaxToken close, string file)
    {
        // "record R(int X);" has no braces at all.
        if (open.IsKind(SyntaxKind.None) || open.IsMissing || close.IsMissing)
        {
            return;
        }
        if (Format.Lines.Same(open, close))
        {
            OneLineBodies.Add(kind, file);
        }
    }

    public void Print()
    {
        Braceless.Print();
        Empty.Print();
        ElseIf.Print();
        OneLineBlocks.Print();
        OneLineBodies.Print();
    }
}
