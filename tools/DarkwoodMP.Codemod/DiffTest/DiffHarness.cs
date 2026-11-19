using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Rules;
using DarkwoodMP.Codemod.SelfTest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.DiffTest;

/// What one case showed.
sealed class DiffOutcome
{
    public int Rows;
    public int DestroyedRows;
    public bool Rewritten;
    public string RewrittenText;
    public readonly List<string> Mismatches = new();

    public bool Passed => Rewritten && Mismatches.Count == 0;
}

/// Runs a rule over one case (guide section 4.5): compiles the original and
/// the form the rule produces, runs both over every combination of inputs and
/// compares the results and the logged order of evaluation. The only allowed
/// difference is a destroyed object under the rule named unity-null, and only
/// the difference that rule exists to make: there a destroyed object must
/// behave as the original did for null.
static class DiffHarness
{
    const BindingFlags AnyStatic =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static DiffOutcome Run(Rule rule, DiffCase diffCase)
    {
        var parse = SnippetCompiler.Parse;
        var source = Passes.Layout(DiffFixture.Wrap(diffCase.Method), parse);
        var fixture = Tree(DiffFixture.Source, "fixture.cs");
        var caseTree = Tree(source, "case.cs");
        var compilation = CSharpCompilation.Create(
            "DiffCase",
            new[] { fixture, caseTree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));
        RequireCompiles(compilation, diffCase.Name);

        var outcome = new DiffOutcome();
        var result = RuleEngine.Run(
            rule, compilation, new[] { caseTree }, ReceiverFilter.All, ".");
        if (!result.Rewritten.TryGetValue(caseTree.FilePath, out var after))
        {
            var reasons = result.Queue.Select(q => q.Reason);
            outcome.Mismatches.Add(
                "the rule left the case alone: " + string.Join("; ", reasons));
            return outcome;
        }
        outcome.Rewritten = true;
        outcome.RewrittenText = after;
        var rewritten = compilation.ReplaceSyntaxTree(
            caseTree, Tree(after, "case.cs"));
        RequireCompiles(rewritten, diffCase.Name + " (rewritten)");
        // Two contexts, because both assemblies have the same name.
        var before = new AssemblyLoadContext("before", isCollectible: true);
        var now = new AssemblyLoadContext("after", isCollectible: true);
        try
        {
            CompareRows(
                Load(before, compilation),
                Load(now, rewritten),
                rule.Name == "unity-null",
                outcome);
        }
        finally
        {
            before.Unload();
            now.Unload();
        }
        return outcome;
    }

    static SyntaxTree Tree(string text, string path)
    {
        var source = SourceText.From(text, Encoding.UTF8, SourceHashAlgorithm.Sha256);
        return CSharpSyntaxTree.ParseText(source, SnippetCompiler.Parse, path);
    }

    static void RequireCompiles(CSharpCompilation compilation, string what)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new Stop(
                $"Differential case {what} doesn't compile:\n  "
                    + string.Join("\n  ", errors.Take(5)));
        }
    }

    static Assembly Load(AssemblyLoadContext context, CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new Stop("A differential case didn't emit:\n  "
                + string.Join("\n  ", result.Diagnostics.Take(5)));
        }
        stream.Position = 0;
        return context.LoadFromStream(stream);
    }

    static void CompareRows(
        Assembly original,
        Assembly rewritten,
        bool unityRule,
        DiffOutcome outcome)
    {
        var parameters = RunMethod(original).GetParameters();
        var inputs = parameters.Select(p => InputsFor(p.ParameterType)).ToArray();
        foreach (var row in Rows(inputs))
        {
            outcome.Rows++;
            var actual = Execute(rewritten, row, inputs);
            var label = Label(parameters, inputs, row);
            bool destroyed = row.Where((index, i) => inputs[i][index].Destroyed).Any();
            if (destroyed && unityRule)
            {
                // Asserted, not waived: a destroyed object must now behave
                // as the original did for null.
                outcome.DestroyedRows++;
                var asNull = row.Select((index, i) => NullIndex(inputs[i], index));
                var expected = Execute(original, asNull.ToArray(), inputs);
                Compare(label, Normal(expected), Normal(actual), outcome);
                continue;
            }
            Compare(label, Execute(original, row, inputs), actual, outcome);
        }
    }

    /// The null input in place of a destroyed one.
    static int NullIndex(List<Input> inputs, int index)
    {
        if (inputs[index].Destroyed)
        {
            return inputs.FindIndex(input => input.Label == "null");
        }
        return index;
    }

    static RunResult Normal(RunResult result)
    {
        return new RunResult(
            result.Outcome.Replace("destroyed", "null"),
            result.Log.Select(line => line.Replace("destroyed", "null")).ToList());
    }

    static void Compare(
        string label, RunResult expected, RunResult actual, DiffOutcome outcome)
    {
        if (expected.Outcome != actual.Outcome)
        {
            outcome.Mismatches.Add(
                $"{label}: result {expected.Outcome} became {actual.Outcome}");
        }
        else if (!expected.Log.SequenceEqual(actual.Log))
        {
            outcome.Mismatches.Add(
                $"{label}: order [{string.Join(", ", expected.Log)}] became "
                    + $"[{string.Join(", ", actual.Log)}]");
        }
    }

    static string Label(
        ParameterInfo[] parameters, List<Input>[] inputs, int[] row)
    {
        var parts = row.Select(
            (index, i) => parameters[i].Name + "=" + inputs[i][index].Label);
        return "(" + string.Join(", ", parts) + ")";
    }

    static MethodInfo RunMethod(Assembly assembly)
    {
        var type = assembly.GetType("DiffFixture.Case");
        var run = type.GetMethod("Run", AnyStatic);
        if (run == null)
        {
            throw new Stop("A differential case has no static method Run.");
        }
        return run;
    }

    /// Every combination of the parameters' inputs.
    static IEnumerable<int[]> Rows(List<Input>[] inputs)
    {
        var row = new int[inputs.Length];
        while (true)
        {
            yield return (int[])row.Clone();
            int i = inputs.Length - 1;
            while (i >= 0 && row[i] == inputs[i].Count - 1)
            {
                row[i] = 0;
                i--;
            }
            if (i < 0)
            {
                yield break;
            }
            row[i]++;
        }
    }

    static RunResult Execute(Assembly assembly, int[] row, List<Input>[] inputs)
    {
        var trace = assembly.GetType("DiffFixture.Trace");
        trace.GetMethod("Reset").Invoke(null, null);
        var reset = assembly.GetType("DiffFixture.Case").GetMethod("Reset", AnyStatic);
        if (reset != null)
        {
            reset.Invoke(null, null);
        }
        var arguments = row.Select(
            (index, i) => inputs[i][index].Make(assembly)).ToArray();
        string outcome;
        try
        {
            var value = RunMethod(assembly).Invoke(null, arguments);
            var describe = trace.GetMethod("Describe");
            outcome = "returned " + describe.Invoke(null, new[] { value });
        }
        catch (TargetInvocationException e)
        {
            outcome = "threw " + e.InnerException.GetType().Name;
        }
        var entries = (System.Collections.IEnumerable)trace
            .GetField("Entries").GetValue(null);
        return new RunResult(outcome, entries.Cast<string>().ToList());
    }

    /// The inputs for a parameter of this type: bools, numbers, nulls and the
    /// fake Unity object (null, alive and destroyed).
    static List<Input> InputsFor(Type type)
    {
        var inner = Nullable.GetUnderlyingType(type);
        if (inner != null)
        {
            var values = new List<Input> { Value("null", null) };
            values.AddRange(InputsFor(inner));
            return values;
        }
        if (type == typeof(bool))
        {
            return Values(false, true);
        }
        if (type == typeof(int))
        {
            return Values(0, 1, -1, 5);
        }
        if (type == typeof(long))
        {
            return Values(0L, 1L, -1L, 5L);
        }
        if (type == typeof(double))
        {
            return Values(0.0, 1.5, -2.0);
        }
        if (type == typeof(float))
        {
            return Values(0f, 1.5f, -2f);
        }
        if (type == typeof(string))
        {
            return new List<Input>
            {
                Value("null", null), Value("\"\"", ""), Value("\"s\"", "s"),
            };
        }
        if (type == typeof(object))
        {
            return new List<Input>
            {
                Value("null", null), Value("\"o\"", "o"), Value("1", 1),
            };
        }
        if (type.FullName == "DiffFixture.UnityObject")
        {
            return new List<Input>
            {
                Value("null", null),
                new Input("alive", a => NewObject(a, false), false),
                new Input("destroyed", a => NewObject(a, true), true),
            };
        }
        throw new Stop("The harness has no inputs for parameter type " + type);
    }

    static object NewObject(Assembly assembly, bool dead)
    {
        var type = assembly.GetType("DiffFixture.UnityObject");
        var value = Activator.CreateInstance(type);
        type.GetField("Dead").SetValue(value, dead);
        return value;
    }

    static List<Input> Values(params object[] values)
    {
        return values.Select(v => Value(v.ToString().ToLowerInvariant(), v)).ToList();
    }

    static Input Value(string label, object value)
    {
        return new Input(label, _ => value, false);
    }

    sealed class Input
    {
        public readonly string Label;
        public readonly Func<Assembly, object> Make;
        public readonly bool Destroyed;

        public Input(string label, Func<Assembly, object> make, bool destroyed)
        {
            Label = label;
            Make = make;
            Destroyed = destroyed;
        }
    }

    sealed record RunResult(string Outcome, List<string> Log);
}
