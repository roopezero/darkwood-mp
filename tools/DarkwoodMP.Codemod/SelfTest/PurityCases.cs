using DarkwoodMP.Codemod.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.SelfTest;

/// The hazards of guide section 4.6, one statement each. The last ?: of the
/// last statement is what the purity analysis is asked about.
static class PurityCases
{
    const string Template = """
        using System;
        using System.Collections.Generic;
        namespace N
        {
            public class Mod
            {
                public int Auto { get; set; }
                public int Computed { get { return Auto + 1; } }
                public int this[int i] { get { return i; } }
                public int Value;
                public static Mod operator +(Mod a, Mod b) { return a; }
            }

            public class C
            {
                int _n;
                readonly int _fixed = 1;
                readonly List<int> _list = new List<int>();
                readonly Mod _mod = new Mod();
                int Bump() { return ++_n; }
                int F(int a, int b) { return a + b; }
                void M(bool c, int local, Mod other)
                {
                    STATEMENTS
                }
            }
        }
        """;

    const string Ok = "";

    static readonly (string Statements, string Expected)[] Cases =
    {
        ("F(1, c ? 2 : 3);", Ok),
        ("F(Bump(), c ? 2 : 3);", Purity.CallEarlier),
        ("F(_n, c ? Bump() : 0);", Purity.ReadEarlier),
        ("F(_fixed, c ? Bump() : 0);", Ok),
        ("F(local, c ? Bump() : 0);", Ok),
        ("Action a = () => local = 5;\nF(local, c ? Bump() : 0);", Purity.ReadEarlier),
        ("F(_list.Count, c ? 2 : 3);", Ok),
        ("F(_mod.Auto, c ? 2 : 3);", Ok),
        ("F(_mod.Computed, c ? 2 : 3);", Purity.CallEarlier),
        ("F(_mod[1], c ? 2 : 3);", Purity.CallEarlier),
        ("F((other + other).Value, c ? 2 : 3);", Purity.CallEarlier),
        ("bool b = c && (c ? true : false);", Purity.MayNotRun),
        ("bool b = c || (c ? true : false);", Purity.MayNotRun),
        ("Mod m = other ?? (c ? _mod : other);", Purity.MayNotRun),
        ("int d = local > 0 ? 3 : (c ? 1 : 2);", Purity.MayNotRun),
        ("Func<int> f = () => c ? 1 : 2;", Purity.MayNotRun),
        ("while (c ? local > 0 : local < 0)\n{\nlocal--;\n}", Purity.MayNotRun),
        ("for (int i = 0; i < 1; i += c ? 1 : 2)\n{\n}", Purity.MayNotRun),
        ("other?.Equals(c ? 1 : 2);", Purity.MayNotRun),
        ("Mod m = new Mod { Value = c ? 1 : 2 };", Purity.MayNotRun),
        ("if (local > 0)\n{\n}\nelse if (c ? true : false)\n{\n}", Purity.MayNotRun),
        ("_list[c ? 0 : 1] = 5;", Ok),
        ("int[] a = new[] { 1 };\na[Bump()] = c ? 1 : 2;", Purity.CallEarlier),
        ("_n += c ? Bump() : 2;", Purity.ReadEarlier),
        ("F(_n, c ? (local = 1) : 0);", Purity.ReadEarlier),
        ("F(1, c ? throw new Exception() : 3);", Ok),
        ("F(Bump(), c ? throw new Exception() : 3);", Purity.ReadEarlier),
    };

    public static int Count => Cases.Length + 2;

    public static List<string> Failures()
    {
        var problems = new List<string>();
        foreach (var (statements, expected) in Cases)
        {
            var verdict = Ask(statements, out var failure);
            if (failure != null)
            {
                problems.Add($"  {statements}: {failure}");
            }
            else if (expected == Ok && verdict != null)
            {
                problems.Add($"  {statements}: should hoist, but: {verdict}");
            }
            else if (expected != Ok && (verdict == null || !verdict.StartsWith(expected)))
            {
                problems.Add(
                    $"  {statements}: should say '{expected}', but: {verdict}");
            }
        }
        CheckHelpers(problems);
        return problems;
    }

    /// Natural types and receivers, asked directly.
    static void CheckHelpers(List<string> problems)
    {
        const string statements =
            "object o = c ? 1 : 2.0;\nobject p = c ? 1.0 : 2;\n"
            + "int i = c ? 1 : 2;\nF(i, _list.Count);";
        var source = Template.Replace("STATEMENTS", statements);
        var tree = CSharpSyntaxTree.ParseText(source, SnippetCompiler.Parse);
        var compilation = CSharpCompilation.Create(
            "helpers",
            new[] { tree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var purity = new Purity(compilation.GetSemanticModel(tree));
        var sites = tree.GetRoot().DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .ToList();
        if (purity.BranchesHaveNaturalType(sites[0])
            || purity.BranchesHaveNaturalType(sites[1])
            || !purity.BranchesHaveNaturalType(sites[2]))
        {
            problems.Add("  a ?: whose branch converts first must be told apart");
        }
        var names = tree.GetRoot().DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .ToList();
        var parameter = names.First(n => n.Identifier.Text == "c");
        var local = names.Last(n => n.Identifier.Text == "i");
        var field = names.First(n => n.Identifier.Text == "_list");
        if (purity.NeedsTemporary(parameter) || purity.NeedsTemporary(local)
            || !purity.NeedsTemporary(field))
        {
            problems.Add("  a parameter is read once, a field needs a temporary");
        }
    }

    static string Ask(string statements, out string failure)
    {
        var source = Template.Replace("STATEMENTS", statements);
        var tree = CSharpSyntaxTree.ParseText(source, SnippetCompiler.Parse);
        var compilation = CSharpCompilation.Create(
            "purity",
            new[] { tree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        failure = null;
        if (errors.Count > 0)
        {
            failure = "doesn't compile: " + errors[0];
            return null;
        }
        var body = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "M")
            .Body;
        var statement = body.Statements.Last();
        var site = statement.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .Last();
        var purity = new Purity(compilation.GetSemanticModel(tree));
        return purity.WhyNotHoist(site, statement);
    }
}
