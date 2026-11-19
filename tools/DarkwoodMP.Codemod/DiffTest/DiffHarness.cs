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
/// compares the results, the logged order of evaluation and what each run left
/// in the object arguments and the case's static fields. The only allowed
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
                // The rewrite treats a destroyed object as null, so the run
                // must leave it as it was made.
                for (int i = 0; i < row.Length; i++)
                {
                    var input = inputs[i][row[i]];
                    if (input.Destroyed)
                    {
                        expected.Objects[i] = ObjectState(
                            original, parameters[i].Name, input.Make(original));
                    }
                }
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
            result.Log.Select(line => line.Replace("destroyed", "null")).ToList(),
            result.Objects);
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
        else
        {
            var changed = expected.Objects
                .Zip(actual.Objects)
                .FirstOrDefault(pair => pair.First != pair.Second);
            if (changed.First != null)
            {
                outcome.Mismatches.Add(
                    $"{label}: {changed.First} became {changed.Second}");
            }
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
        var run = RunMethod(assembly);
        string outcome;
        try
        {
            var value = run.Invoke(null, arguments);
            outcome = Finish(assembly, run, value);
        }
        catch (TargetInvocationException e)
        {
            outcome = "threw " + e.InnerException.GetType().Name;
        }
        outcome += Statics(assembly);
        var entries = (System.Collections.IEnumerable)trace
            .GetField("Entries").GetValue(null);
        var names = run.GetParameters().Select(p => p.Name).ToArray();
        var objects = arguments
            .Select((argument, i) => ObjectState(assembly, names[i], argument))
            .ToList();
        return new RunResult(outcome, entries.Cast<string>().ToList(), objects);
    }

    /// How many values an iterator may yield before the harness stops it. A
    /// coroutine can loop for ever.
    const int StepLimit = 1000;

    /// What a run returned. An iterator is run to its end, or for StepLimit
    /// values, and a task is waited for, because otherwise their bodies
    /// wouldn't run here (or would run on another thread during later rows).
    static string Finish(Assembly assembly, MethodInfo run, object value)
    {
        if (value is Task task)
        {
            return Await(assembly, run, task);
        }
        var enumerator = value as System.Collections.IEnumerator;
        if (value is System.Collections.IEnumerable sequence
            && value is not string
            && value is not System.Collections.ICollection)
        {
            enumerator = sequence.GetEnumerator();
        }
        if (enumerator == null)
        {
            return "returned " + Describe(assembly, value);
        }
        var yielded = new List<string>();
        try
        {
            while (yielded.Count < StepLimit && enumerator.MoveNext())
            {
                yielded.Add(Describe(assembly, enumerator.Current));
            }
        }
        catch (Exception e)
        {
            return $"yielded [{string.Join(", ", yielded)}], then threw "
                + e.GetType().Name;
        }
        var text = $"yielded [{string.Join(", ", yielded)}]";
        if (yielded.Count == StepLimit)
        {
            text += $", stopped after {StepLimit} values";
        }
        return text;
    }

    static string Await(Assembly assembly, MethodInfo run, Task task)
    {
        try
        {
            if (!task.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new Stop(
                    "A differential case's task didn't finish in 10 seconds.");
            }
        }
        catch (AggregateException e)
        {
            return "threw " + e.InnerException.GetType().Name;
        }
        // An async Task is a Task<VoidTaskResult> underneath, so the declared
        // type says whether there is a result.
        var type = run.ReturnType;
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return "completed";
        }
        var result = type.GetProperty("Result").GetValue(task);
        return "returned " + Describe(assembly, result);
    }

    static string Describe(Assembly assembly, object value)
    {
        var describe = assembly.GetType("DiffFixture.Trace").GetMethod("Describe");
        return (string)describe.Invoke(null, new[] { value });
    }

    /// The static fields of the case after a run, so that a dropped store to
    /// one shows.
    static string Statics(Assembly assembly)
    {
        var fields = assembly.GetType("DiffFixture.Case")
            .GetFields(AnyStatic)
            .Where(field => !field.IsLiteral)
            .OrderBy(field => field.Name, StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var field in fields)
        {
            var value = Describe(assembly, field.GetValue(null));
            text.Append($"; {field.Name}={value}");
        }
        return text.ToString();
    }

    /// The fields of an object argument after a run (the fake Unity object's
    /// Dead, Count and Next), so that a dropped store to one shows. Empty for
    /// null and for a value that can't change.
    static string ObjectState(Assembly assembly, string name, object argument)
    {
        if (argument == null
            || argument is string
            || argument.GetType().IsValueType)
        {
            return "";
        }
        var parts = argument.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => field.Name + "="
                + Describe(assembly, field.GetValue(argument)));
        return name + "{" + string.Join(", ", parts) + "}";
    }

    /// The inputs for a parameter of this type: bools, numbers (with NaN and
    /// the infinities for double and float), nulls and the fake Unity object
    /// (null, alive and destroyed).
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
        // NaN fails every comparison, so it shows an inverted one:
        // "d > 0 ? a : b" is not "d <= 0 ? b : a".
        if (type == typeof(double))
        {
            var values = Values(0.0, 1.5, -2.0);
            values.Add(Value("nan", double.NaN));
            values.Add(Value("+inf", double.PositiveInfinity));
            values.Add(Value("-inf", double.NegativeInfinity));
            return values;
        }
        if (type == typeof(float))
        {
            var values = Values(0f, 1.5f, -2f);
            values.Add(Value("nan", float.NaN));
            values.Add(Value("+inf", float.PositiveInfinity));
            values.Add(Value("-inf", float.NegativeInfinity));
            return values;
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

    /// Objects holds the final state of each argument, in order.
    sealed record RunResult(
        string Outcome, List<string> Log, List<string> Objects);
}
