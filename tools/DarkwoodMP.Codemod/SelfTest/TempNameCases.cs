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
        using System.Collections.Generic;
        using System.Linq;
        namespace N
        {
            public class Holder
            {
                public static Holder Instance;
                public int Size;
                public int depth;
                public string type;
                public int NPCs;
                public int IPAddress;
                public int XMax;
                public static Holder Make() { return null; }
                public Holder Child() { return null; }
            }

            public class Traverse
            {
                public static Traverse Create(object o) { return null; }
                public Traverse Field(string name) { return null; }
                public Traverse Property(string name) { return null; }
                public T GetValue<T>() { return default; }
            }

            public class C
            {
                int _foo;
                @class = 0;
                void F(int amount, string label) { }
                int Get() { return 1; }
                List<int> getItems() { return null; }

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
                            int deep = Holder.Instance.depth;
                            var items = getItems();
                            var size = new List<Holder>()
                                .Select(h => h.Size).FirstOrDefault();
                            var one = new List<Holder>().FirstOrDefault();
                            var kind = new List<Holder>()
                                .Select(h => h.type).FirstOrDefault();
                            var pairs = new List<(string shade, bool lit)>();
                            F(amount: 1, label: "x");
                            var pair = (hue: 1, tone: 2);
                            bool typed = one is { type: "a" };
                            var runs = (one.NPCs, one.IPAddress, one.XMax);
                            var made = Holder.Make().Child();
                            var mesh = Traverse.Create(one)
                                .Field("descriptionText").GetValue<Holder>();
                            var flag = Traverse.Create(one)
                                .Property("_isShown").GetValue<bool>();
                            break;
                    }
                }
            }
        }
        """;

    public const int Count = 36;

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

        // Pick, for the coalesce rule: a name after a dot is looked up in
        // what is in front of the dot, so no local can clash with it.
        Expect(problems, "after a dot, for Unique", "depth2",
            names.Unique("depth"));
        Expect(problems, "after a dot, for Pick", "depth",
            Pick(names, new[] { "depth" }));
        Expect(problems, "another variable's name is passed over", "tally",
            Pick(names, new[] { "count", "tally" }));
        Expect(problems, "a related name gets a number", "count2",
            names.Pick(new List<string> { "count" }, new List<string>
            {
                "count",
            }));
        Expect(problems, "another temp's name gets a number", "tally2",
            Pick(names, new[] { "tally" }));
        Expect(problems, "all taken by others: the first gets a number",
            "n2", Pick(names, new[] { "n", "c" }));
        Expect(problems, "value comes last", "spare",
            Pick(names, new[] { "value", "spare" }));
        // A tuple element's name, or one in front of a colon, is looked up
        // in the tuple, the method called or the type tested.
        Expect(problems, "a tuple element's name is free", "shade",
            Pick(names, new[] { "shade" }));
        Expect(problems, "an argument's name is free", "label",
            Pick(names, new[] { "label" }));
        Expect(problems, "a name in a tuple literal is free", "hue",
            Pick(names, new[] { "hue" }));
        Expect(problems, "a property pattern's name is free", "type",
            Pick(names, new[] { "type" }));
        Expect(problems, "a tuple element's name, for Unique", "lit2",
            names.Unique("lit"));
        var calls = method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .ToList();
        Expect(problems, "a get call without its get", "items",
            TempNames.Named(calls.First(c => c.ToString() == "getItems()"),
                model));
        Expect(problems, "an element picked after a Select", "size",
            TempNames.Named(calls.First(
                c => c.ToString().EndsWith(".FirstOrDefault()")
                    && c.ToString().Contains("Select")), model));
        Expect(problems, "an element named after its type", "holder",
            TempNames.Named(calls.Last(
                c => c.ToString() == "new List<Holder>().FirstOrDefault()"),
                model));
        Expect(problems, "type is a name: a keyword only before attributes",
            "type", TempNames.Named(calls.First(
                c => c.ToString().EndsWith(".FirstOrDefault()")
                    && c.ToString().Contains("h.type")), model));
        Expect(problems, "Math.Max's val2 is no word", "False",
            TempNames.IsWord("val2").ToString());
        Expect(problems, "ReferenceEquals's objA says nothing", "False",
            TempNames.IsWord("objA").ToString());
        var reads = method.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .ToList();
        Expect(problems, "a plural run of capitals goes small", "npcs",
            TempNames.Named(reads.First(r => r.Name.ToString() == "NPCs"),
                model));
        Expect(problems, "the run's last capital starts a word", "ipAddress",
            TempNames.Named(
                reads.First(r => r.Name.ToString() == "IPAddress"), model));
        Expect(problems, "one capital in front is a word", "xMax",
            TempNames.Named(reads.First(r => r.Name.ToString() == "XMax"),
                model));
        Expect(problems, "a call named for what it does: its type", "holder",
            TempNames.Named(calls.First(c => c.ToString() == "Holder.Make()"),
                model));
        Expect(problems, "a call named for what it gives: its method", "child",
            TempNames.Named(
                calls.First(c => c.ToString() == "Holder.Make().Child()"),
                model));
        Expect(problems, "a field's value got by its name: the field",
            "descriptionText", TempNames.Named(calls.First(
                c => c.ToString().EndsWith(".GetValue<Holder>()")), model));
        Expect(problems, "a property's value got by its name: the property",
            "isShown", TempNames.Named(calls.First(
                c => c.ToString().EndsWith(".GetValue<bool>()")), model));
        return problems;
    }

    static string Pick(TempNames names, string[] hints)
    {
        return names.Pick(hints.ToList(), new List<string>());
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
