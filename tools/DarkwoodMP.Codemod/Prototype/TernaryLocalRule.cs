using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace DarkwoodMP.Codemod.Prototype;

/// One representative IL-changing rule: T x = c ? a : b;  ->  T x; if (c) { x = a; } else { x = b; }
/// Anything the rule can't prove safe is left alone and reported.
sealed class TernaryLocalRule : CSharpSyntaxRewriter
{
    public readonly Dictionary<LocalDeclarationStatementSyntax, StatementSyntax[]> Sites = new();
    public readonly List<string> Skipped = new();

    public void Plan(SyntaxNode root, SemanticModel model, HashSet<int> banned)
    {
        foreach (var d in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (d.Parent is not BlockSyntax && d.Parent is not SwitchSectionSyntax) { continue; }
            if (d.Declaration.Variables.Count != 1) { continue; }
            var v = d.Declaration.Variables[0];
            if (v.Initializer == null) { continue; }
            ExpressionSyntax value = v.Initializer.Value;
            while (value is ParenthesizedExpressionSyntax p) { value = p.Expression; }
            if (value is not ConditionalExpressionSyntax c) { continue; }
            if (banned.Contains(d.SpanStart)) { Skipped.Add("did not compile after the rewrite"); continue; }
            if (d.IsConst || d.UsingKeyword != default || d.Modifiers.Count > 0 || d.Declaration.Type is RefTypeSyntax) { Skipped.Add("const, using or ref"); continue; }
            if (c.QuestionToken.GetAllTrivia().Concat(c.ColonToken.GetAllTrivia()).Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia))) { Skipped.Add("comment on ? or :"); continue; }

            var natural = model.GetTypeInfo(c).Type;
            var declared = model.GetTypeInfo(d.Declaration.Type).Type;
            string typeText;
            if (d.Declaration.Type.IsVar)
            {
                if (natural == null || natural.IsAnonymousType || natural.ToDisplayString().Contains("anonymous")) { Skipped.Add("var of an anonymous type"); continue; }
                typeText = natural.ToMinimalDisplayString(model, d.SpanStart);
            }
            else
            {
                typeText = d.Declaration.Type.ToString();
            }
            if (natural != null && declared != null && !SymbolEqualityComparer.Default.Equals(natural, declared))
            {
                var t = model.GetTypeInfo(c.WhenTrue).Type;
                var f = model.GetTypeInfo(c.WhenFalse).Type;
                if (!SymbolEqualityComparer.Default.Equals(t, natural) || !SymbolEqualityComparer.Default.Equals(f, natural))
                {
                    // object o = c ? 1 : 2.0 boxes a double in both branches; o = 1 would box an int.
                    Skipped.Add("a branch is converted before the declared type's conversion");
                    continue;
                }
            }
            string name = v.Identifier.Text;
            var tag = new SyntaxAnnotation("site", d.SpanStart.ToString());
            var list = new List<StatementSyntax>();
            var first = SyntaxFactory.ParseStatement($"{typeText} {name};\n")
                .WithLeadingTrivia(d.GetLeadingTrivia())
                .WithTrailingTrivia(d.GetTrailingTrivia())
                .WithAdditionalAnnotations(tag, Formatter.Annotation);
            list.Add(first);
            list.Add(SyntaxFactory.ParseStatement($"if ({c.Condition})\n{{\n{name} = {c.WhenTrue};\n}}\nelse\n{{\n{name} = {c.WhenFalse};\n}}\n")
                .WithAdditionalAnnotations(tag, Formatter.Annotation));
            Sites[d] = list.ToArray();
        }
    }

    public override SyntaxNode VisitBlock(BlockSyntax node) =>
        node.WithStatements(SyntaxFactory.List(Map(node.Statements)));

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node) =>
        node.WithStatements(SyntaxFactory.List(Map(node.Statements)));

    IEnumerable<StatementSyntax> Map(SyntaxList<StatementSyntax> statements)
    {
        foreach (var s in statements)
        {
            if (s is LocalDeclarationStatementSyntax d && Sites.TryGetValue(d, out var rep))
            {
                foreach (var r in rep) { yield return r; }
            }
            else
            {
                yield return (StatementSyntax)Visit(s);
            }
        }
    }

    /// The member (as Cecil names it) that holds a site.
    public static string MemberOf(SyntaxNode n, SemanticModel model)
    {
        var member = n.Ancestors().OfType<MemberDeclarationSyntax>().First(m => m is not GlobalStatementSyntax
            && m is not BaseTypeDeclarationSyntax && m is not NamespaceDeclarationSyntax);
        var type = n.Ancestors().OfType<BaseTypeDeclarationSyntax>().First();
        var sym = model.GetDeclaredSymbol(type);
        string typeName = sym.ToDisplayString(new SymbolDisplayFormat(typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces)).Replace('.', '/');
        // Cecil: Namespace.Outer/Inner. Rebuild it from containing types.
        string ns = sym.ContainingNamespace.IsGlobalNamespace ? "" : sym.ContainingNamespace.ToDisplayString() + ".";
        var chain = new Stack<string>();
        for (var t = sym; t != null; t = t.ContainingType) { chain.Push(t.MetadataName); }
        typeName = ns + string.Join("/", chain);
        string name = member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor",
            PropertyDeclarationSyntax p => (n.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault()?.Keyword.ValueText ?? "get") + "_" + p.Identifier.Text,
            IndexerDeclarationSyntax => (n.Ancestors().OfType<AccessorDeclarationSyntax>().FirstOrDefault()?.Keyword.ValueText ?? "get") + "_Item",
            OperatorDeclarationSyntax o => "op_" + o.OperatorToken.Text,
            ConversionOperatorDeclarationSyntax => "op_Implicit",
            DestructorDeclarationSyntax => "Finalize",
            _ => member.Kind().ToString()
        };
        return typeName + "::" + name;
    }
}
