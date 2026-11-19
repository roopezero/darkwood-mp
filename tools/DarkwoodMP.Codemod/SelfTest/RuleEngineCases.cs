using System.Text;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.SelfTest;

/// The rule engine on small snippets: what it rewrites, what it leaves in the
/// queue and what stops it.
static class RuleEngineCases
{
    const string Source = """
        namespace N
        {
            public class C
            {
                public int M(bool c)
                {
                    return c ? 1 : 2;
                }

                public int K(bool c)
                {
                    int v = c ? 3 : 4;
                    return c ? v : 5;
                }
            }
        }
        """;

    const string Rewritten = """
        namespace N
        {
            public class C
            {
                public int M(bool c)
                {
                    if (c)
                    {
                        return 1;
                    }
                    return 2;
                }

                public int K(bool c)
                {
                    int v = c ? 3 : 4;
                    if (c)
                    {
                        return v;
                    }
                    return 5;
                }
            }
        }
        """;

    public const int Count = 8;

    public static List<string> Failures()
    {
        var problems = new List<string>();
        Check(problems, "rewrites and records", RewritesAndRecords);
        Check(problems, "queues what does not compile", QueuesWhatDoesNotCompile);
        Check(problems, "skips a site that would lose a comment", KeepsComments);
        Check(problems, "refuses a new lambda", RefusesNewFrames);
        Check(problems, "stops when the census doesn't add up", StopsOnBadCensus);
        Check(problems, "refuses a statement outside a list", NeedsAStatementList);
        Check(problems, "manifest keys match the DLL", KeysMatchTheDll);
        Check(problems, "--receivers tells Unity objects apart", Receivers);
        return problems;
    }

    static void Check(List<string> problems, string name, Func<string> test)
    {
        string problem;
        try
        {
            problem = test();
        }
        catch (Stop stop)
        {
            problem = "stopped: " + stop.Message;
        }
        if (problem != null)
        {
            problems.Add($"  {name}: {problem}");
        }
    }

    static CSharpCompilation Compile(string source, out SyntaxTree tree)
    {
        var text = SourceText.From(
            Passes.Layout(source, SnippetCompiler.Parse),
            Encoding.UTF8,
            SourceHashAlgorithm.Sha256);
        tree = CSharpSyntaxTree.ParseText(text, SnippetCompiler.Parse, "/x/case.cs");
        return CSharpCompilation.Create(
            "case",
            new[] { tree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    static RuleResult Apply(Rule rule, string source, out SyntaxTree tree)
    {
        var compilation = Compile(source, out tree);
        return RuleEngine.Run(
            rule, compilation, new[] { tree }, ReceiverFilter.All, "/x");
    }

    static string RewritesAndRecords()
    {
        var result = Apply(
            new ProbeReturnRule(ProbeForm.Correct), Source, out var tree);
        var ternary = (int)Operator.Ternary;
        if (!result.Rewritten.TryGetValue(tree.FilePath, out var after))
        {
            return "the file was not rewritten";
        }
        if (after != Rewritten)
        {
            return "unexpected text:\n" + LineDiff.Of(Rewritten, after);
        }
        var members = string.Join(",", result.Manifest.Members);
        if (members != "N.C::K(System.Boolean),N.C::M(System.Boolean)")
        {
            return "manifest members: " + members;
        }
        if (result.Sites.Count != 2 || result.Sites[0].File != "case.cs"
            || result.Sites[0].Line != 7)
        {
            return "sites: " + result.Sites.Count;
        }
        if (result.Before[ternary] != 3 || result.Removed[ternary] != 2
            || result.Queued[ternary] != 0 || result.After[ternary] != 1
            || result.LeftFor(Operator.Ternary) != 1)
        {
            return "census: " + result.Before[ternary] + "/"
                + result.Removed[ternary] + "/" + result.After[ternary];
        }
        return null;
    }

    static string QueuesWhatDoesNotCompile()
    {
        var result = Apply(new BrokenRule(), Source, out var tree);
        var ternary = (int)Operator.Ternary;
        if (!result.Rewritten.TryGetValue(tree.FilePath, out var after))
        {
            return "the good rewrite was dropped";
        }
        if (result.Queue.Count != 1
            || !result.Queue[0].Reason.StartsWith("did not compile"))
        {
            return "queue: " + string.Join("; ", result.Queue.Select(q => q.Reason));
        }
        if (after.Contains("missingName") || !after.Contains("return c ? v : 5;"))
        {
            return "the bad site should be left as it was:\n" + after;
        }
        if (result.Removed[ternary] != 1 || result.Queued[ternary] != 1)
        {
            return "census: " + result.Removed[ternary] + "/" + result.Queued[ternary];
        }
        return null;
    }

    static string KeepsComments()
    {
        var source = Source.Replace("return c ? 1 : 2;", "return c /* why */ ? 1 : 2;");
        var result = Apply(
            new ProbeReturnRule(ProbeForm.Correct), source, out var tree);
        var reasons = result.Queue.Select(q => q.Reason).ToList();
        if (reasons.Count != 1 || !reasons[0].Contains("comment"))
        {
            return "queue: " + string.Join("; ", reasons);
        }
        if (result.Rewritten[tree.FilePath].Contains("why") != true)
        {
            return "the comment is gone";
        }
        return null;
    }

    static string RefusesNewFrames()
    {
        var result = Apply(new LambdaRule(), Source, out _);
        if (result.Queue.Count == 0
            || !result.Queue.All(q => q.Reason.Contains("lambda")))
        {
            return "queue: " + string.Join("; ", result.Queue.Select(q => q.Reason));
        }
        return null;
    }

    static string StopsOnBadCensus()
    {
        try
        {
            Apply(new KeepsTheOperatorRule(), Source, out _);
        }
        catch (Stop stop)
        {
            if (stop.Message.StartsWith("Census of ?:"))
            {
                return null;
            }
            return "stopped for another reason: " + stop.Message;
        }
        return "the engine accepted a rewrite that kept the operator";
    }

    static string NeedsAStatementList()
    {
        var source = Source.Replace(
            "int v = c ? 3 : 4;", "int v = 0;\n        if (c) v = c ? 3 : 4;");
        var result = Apply(new EmbeddedRule(), source, out _);
        if (result.Queue.Count != 1
            || !result.Queue[0].Reason.Contains("block or switch section"))
        {
            return "queue: " + string.Join("; ", result.Queue.Select(q => q.Reason));
        }
        return null;
    }

    static string Receivers()
    {
        const string source = """
            namespace UnityEngine
            {
                public class Object
                {
                }

                public class Component : Object
                {
                }
            }

            namespace N
            {
                public class C
                {
                    public int M(UnityEngine.Component u, string s)
                    {
                        return u.GetHashCode() + s.Length;
                    }
                }
            }
            """;
        var compilation = Compile(source, out var tree);
        var model = compilation.GetSemanticModel(tree);
        var names = tree.GetRoot().DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .ToList();
        var unity = names.First(n => n.Identifier.Text == "u");
        var text = names.First(n => n.Identifier.Text == "s");
        var both = new RulePlan("x", model, ReceiverFilter.All, "case.cs");
        var onlyUnity = new RulePlan("x", model, ReceiverFilter.Unity, "case.cs");
        var others = new RulePlan("x", model, ReceiverFilter.Other, "case.cs");
        if (!both.ReceiverMatches(unity) || !both.ReceiverMatches(text)
            || !onlyUnity.ReceiverMatches(unity) || onlyUnity.ReceiverMatches(text)
            || others.ReceiverMatches(unity) || !others.ReceiverMatches(text))
        {
            return "the filter picked the wrong receivers";
        }
        return null;
    }

    /// Every kind of signature the plugin uses must be spelled as Cecil does.
    static string KeysMatchTheDll()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            namespace N.Inner
            {
                public class Outer<T>
                {
                    public class Nested { public int M(T t, List<T>[] l) { return 1; } }
                    public int G<U>(U u, Dictionary<string, U> d, ref int r, out int o,
                        int[,] grid, params object[] rest)
                    {
                        o = 1; return 1;
                    }
                    public int this[int i] { get { return i; } set { } }
                    public static implicit operator int(Outer<T> o) { return 1; }
                    public int P => 1;
                    public int Q { get { return 2; } }
                    public event Action E { add { } remove { } }
                    ~Outer() { }
                }
                public class Plain : IDisposable
                {
                    int _f = Make();
                    static int Make() { return 1; }
                    void IDisposable.Dispose() { }
                    public (int a, string b) Tup((int, string) t, int? n) { return t; }
                }
            }
            """;
        var compilation = Compile(source, out var tree);
        var model = compilation.GetSemanticModel(tree);
        var manifest = new Il.Manifest();
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            bool member = node is BaseMethodDeclarationSyntax
                || node is AccessorDeclarationSyntax
                || node is ArrowExpressionClauseSyntax
                    { Parent: BasePropertyDeclarationSyntax }
                || node is VariableDeclaratorSyntax
                    { Parent.Parent: FieldDeclarationSyntax };
            if (member)
            {
                manifest.Members.Add(MemberKeys.Of(node, model));
            }
        }
        if (manifest.Members.Count < 14)
        {
            return "only found " + manifest.Members.Count + " members";
        }
        try
        {
            RuleCommand.CheckMembers(compilation, manifest);
        }
        catch (Stop stop)
        {
            return stop.Message;
        }
        return null;
    }

    /// Proposes one return it can do and one that can't compile.
    sealed class BrokenRule : Rule
    {
        public override string Name => "broken";

        public override void Plan(RulePlan plan)
        {
            var targets = plan.Root.DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(r => r.Expression is ConditionalExpressionSyntax)
                .ToList();
            var first = (ConditionalExpressionSyntax)targets[0].Expression;
            plan.Propose(first, targets[0], "if (c)\n{\nreturn 1;\n}\nreturn 2;");
            var second = (ConditionalExpressionSyntax)targets[1].Expression;
            plan.Propose(second, targets[1], "return missingName;");
        }
    }

    sealed class LambdaRule : Rule
    {
        public override string Name => "lambda";

        public override void Plan(RulePlan plan)
        {
            var target = plan.Root.DescendantNodes()
                .OfType<ReturnStatementSyntax>().First();
            plan.Propose(
                target.Expression,
                target,
                "System.Func<int> f = () => 1;\nreturn f();");
        }
    }

    sealed class KeepsTheOperatorRule : Rule
    {
        public override string Name => "keeps";

        public override void Plan(RulePlan plan)
        {
            var target = plan.Root.DescendantNodes()
                .OfType<ReturnStatementSyntax>().First();
            plan.Propose(target.Expression, target, "return c ? 1 : 3;");
        }
    }

    /// Proposes to replace a statement that isn't in a block.
    sealed class EmbeddedRule : Rule
    {
        public override string Name => "embedded";

        public override void Plan(RulePlan plan)
        {
            var target = plan.Root.DescendantNodes()
                .OfType<ExpressionStatementSyntax>()
                .First(s => s.Parent is IfStatementSyntax);
            var site = target.DescendantNodes()
                .OfType<ConditionalExpressionSyntax>().First();
            plan.Propose(site, target, "v = 3;");
        }
    }
}
