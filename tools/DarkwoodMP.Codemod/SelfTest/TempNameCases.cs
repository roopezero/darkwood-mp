using DarkwoodMP.Codemod.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.SelfTest;

/// Temporaries: the names they get, and that a name is free across the whole
/// member.
static class TempNameCases
{
    const string Source = """
        using System;
        namespace N
        {
            public class Holder
            {
                public static Holder Instance;
                public int Size;
            }

            public class C
            {
                int _foo;
                @class = 0;
                void F(int amount, string label) { }
                int Get() { return 1; }

                void M(int count, int n, bool c)
                {
                    switch (n)
                    {
                        case 0:
                            int total = c ? 1 : 2;
                            F(c ? 1 : 2, $"{(c ? "a" : "b")}");
                            break;
                        case 1:
                            Action<int> a = text2 => { int inner = 1; };
                            int sum = _foo + Holder.Instance.Size + Get();
                            break;
                        case 2:
        #if NEVER_DEFINED
                            int hidden = 3;
        #endif
                            break;
                    }
                }
            }
        }
        """;

    public const int Count = 11;

    public static List<string> Failures()
    {
        var problems = new List<string>();
        var source = Source.Replace("@class = 0;", "int @class = 0;");
        var tree = CSharpSyntaxTree.ParseText(source, SnippetCompiler.Parse);
        var compilation = CSharpCompilation.Create(
            "temps",
            new[] { tree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            problems.Add("  doesn't compile: " + errors[0]);
            return problems;
        }
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == "M");
        var ternaries = method.DescendantNodes()
            .OfType<ConditionalExpressionSyntax>()
            .ToList();
        var sum = method.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .Last(b => b.IsKind(SyntaxKind.AddExpression));

        Expect(problems, "declared name", "total", TempNames.Hint(ternaries[0], model));
        Expect(problems, "parameter", "amount", TempNames.Hint(ternaries[1], model));
        Expect(problems, "interpolation hole", "text", TempNames.Hint(ternaries[2], model));
        Expect(problems, "field", "foo", TempNames.Hint(sum.Left, model));
        Expect(problems, "receiver", "size", TempNames.Hint(sum.Right, model));

        var names = new TempNames(method);
        Expect(problems, "taken in another section", "total2", names.Unique("total"));
        Expect(problems, "taken in a lambda", "inner2", names.Unique("inner"));
        Expect(problems, "free", "fresh", names.Unique("fresh"));
        Expect(problems, "second temp", "fresh2", names.Unique("fresh"));
        // A build with the symbol defined would declare it.
        Expect(problems, "declared only in disabled code", "hidden2",
            names.Unique("hidden"));
        // From an @object parameter: "object" wouldn't compile.
        Expect(problems, "a keyword", "object2", names.Unique("object"));
        return problems;
    }

    static void Expect(
        List<string> problems, string what, string expected, string actual)
    {
        if (expected != actual)
        {
            problems.Add($"  {what}: expected {expected}, got {actual}");
        }
    }
}
