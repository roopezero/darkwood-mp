using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// What a rewrite made of a case: the new text, or null and why it
    /// left the case alone.
    public delegate (string After, string Why) Rewrite(
        CSharpCompilation compilation, SyntaxTree tree);

    public static DiffOutcome Run(Rule rule, DiffCase diffCase)
    {
        Rewrite rewrite = (compilation, tree) =>
        {
            var result = RuleEngine.Run(
                rule, compilation, new[] { tree }, ReceiverFilter.All, ".");
            if (result.Rewritten.TryGetValue(tree.FilePath, out var after))
            {
                return (after, null);
            }
            var reasons = result.Queue.Select(q => q.Reason);
            return (null, string.Join("; ", reasons));
        };
        return Run(
            diffCase,
            rewrite,
            rule.Name == "unity-null",
            SnippetCompiler.References);
    }

    /// The same for any rewrite, compiled against these references. Stage
    /// 11 compiles its cases against the game's own mscorlib, which has no
    /// interpolated string handler, so they are lowered to string.Format
    /// as in the plugin.
    public static DiffOutcome Run(
        DiffCase diffCase,
        Rewrite rewrite,
        bool unityRule,
        IEnumerable<MetadataReference> references)
    {
        var parse = SnippetCompiler.Parse;
        var source = Passes.Layout(DiffFixture.Wrap(diffCase.Method), parse);
        var fixtureParse = parse;
        if (unityRule)
        {
            fixtureParse = parse.WithPreprocessorSymbols(
                DiffFixture.UnitySymbol);
        }
        var fixture = Tree(DiffFixture.Source, "fixture.cs", fixtureParse);
        var caseTree = Tree(source, "case.cs", parse);
        var compilation = CSharpCompilation.Create(
            "DiffCase",
            new[] { fixture, caseTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));
        RequireCompiles(compilation, diffCase.Name);

        var outcome = new DiffOutcome();
        var (after, why) = rewrite(compilation, caseTree);
        if (after == null)
        {
            outcome.Mismatches.Add("the rule left the case alone: " + why);
            return outcome;
        }
        outcome.Rewritten = true;
        outcome.RewrittenText = after;
        var rewritten = compilation.ReplaceSyntaxTree(
            caseTree, Tree(after, "case.cs", parse));
        RequireCompiles(rewritten, diffCase.Name + " (rewritten)");
        // Two contexts, because both assemblies have the same name.
        var before = new AssemblyLoadContext("before", isCollectible: true);
        var now = new AssemblyLoadContext("after", isCollectible: true);
        try
        {
            CompareRows(
                Load(before, compilation),
                Load(now, rewritten),
                unityRule,
                outcome);
        }
        finally
        {
            before.Unload();
            now.Unload();
        }
        return outcome;
    }

    static SyntaxTree Tree(string text, string path, CSharpParseOptions parse)
    {
        var source = SourceText.From(text, Encoding.UTF8, SourceHashAlgorithm.Sha256);
        return CSharpSyntaxTree.ParseText(source, parse, path);
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
        var inputs = parameters
            .Select(p => InputsFor(p.ParameterType, p.Position))
            .ToArray();
        var through = parameters.Select(PassesThrough).ToArray();
        foreach (var row in Rows(inputs))
        {
            outcome.Rows++;
            var actual = Execute(rewritten, row, inputs);
            var label = Label(parameters, inputs, row);
            // The destroyed inputs the rewrite must treat as null.
            var asNull = Enumerable.Range(0, row.Length)
                .Where(i => inputs[i][row[i]].Destroyed && !through[i])
                .ToList();
            if (asNull.Count > 0 && unityRule)
            {
                // Asserted, not waived: a destroyed object must now behave
                // as the original did for null.
                outcome.DestroyedRows++;
                var nulls = (int[])row.Clone();
                foreach (int i in asNull)
                {
                    nulls[i] = inputs[i].FindIndex(input => input.Label == "null");
                }
                var expected = Execute(original, nulls, inputs);
                // The rewrite treats a destroyed object as null, so the run
                // must leave it as it was made.
                foreach (int i in asNull)
                {
                    expected.Objects[i] = ObjectState(
                        original, parameters[i].Name, inputs[i][row[i]].Make(original));
                }
                // The log names the destroyed object where the original
                // logged null. With one such object the outcome must match
                // to the letter: the rewrite must not return it. With several,
                // returning one is right where the original returned another.
                if (asNull.Count == 1)
                {
                    Compare(label, NormalLog(expected), NormalLog(actual), outcome);
                }
                else
                {
                    Compare(label, Normal(expected), Normal(actual), outcome);
                }
                continue;
            }
            Compare(label, Execute(original, row, inputs), actual, outcome);
        }
    }

    static bool PassesThrough(ParameterInfo parameter)
    {
        return parameter.CustomAttributes.Any(
            a => a.AttributeType.FullName == "DiffFixture.PassesThroughAttribute");
    }

    static RunResult NormalLog(RunResult result)
    {
        return new RunResult(
            result.Outcome,
            result.Log.Select(line => line.Replace("destroyed", "null")).ToList(),
            result.Objects);
    }

    static RunResult Normal(RunResult result)
    {
        var log = NormalLog(result);
        return log with { Outcome = result.Outcome.Replace("destroyed", "null") };
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
        // An async void method returns at its first await, and the rest of
        // its body runs on the thread pool, during later rows.
        if (run.ReturnType == typeof(void)
            && run.IsDefined(typeof(AsyncStateMachineAttribute), false))
        {
            throw new Stop("A differential case's Run is async void. Make it "
                + "return Task, so that the harness can wait for its body.");
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
            outcome = Threw(e.InnerException);
        }
        outcome += Statics(assembly);
        var entries = (System.Collections.IEnumerable)trace
            .GetField("Entries").GetValue(null);
        var names = run.GetParameters().Select(p => p.Name).ToArray();
        var objects = arguments
            .Select((argument, i) => ObjectState(assembly, names[i], argument))
            .ToList();
        var unseen = (string)trace.GetField("Undescribable").GetValue(null);
        if (unseen != null)
        {
            throw new Stop($"A differential case logged, returned or left "
                + $"behind a {unseen}, which the harness can't show, so a "
                + "difference in it would go unseen. Use values it can show: "
                + "numbers, bools, strings, enums, collections, tuples, the "
                + "fake UnityObject and structs and classes written in the "
                + "case.");
        }
        return new RunResult(outcome, entries.Cast<string>().ToList(), objects);
    }

    /// How many values an iterator may yield before the harness stops it. A
    /// coroutine can loop for ever.
    const int StepLimit = 1000;

    /// What a run returned. An iterator is run to its end, or for StepLimit
    /// values, and a task is waited for, because otherwise their bodies
    /// wouldn't run here (or would run on another thread during later rows).
    /// A delegate stops the command: its body would never run.
    static string Finish(Assembly assembly, MethodInfo run, object value)
    {
        if (value is Delegate)
        {
            throw new Stop("A differential case's Run returns a delegate, "
                + "whose body the harness would never run. Call it inside "
                + "Run and return what it returns.");
        }
        var task = AsTask(value);
        if (task != null)
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
            return $"yielded [{string.Join(", ", yielded)}], then "
                + Threw(e);
        }
        var text = $"yielded [{string.Join(", ", yielded)}]";
        if (yielded.Count == StepLimit)
        {
            text += $", stopped after {StepLimit} values";
        }
        return text;
    }

    /// A Task, or the task behind a ValueTask, which isn't a Task: a
    /// pending one would print as an empty string.
    static Task AsTask(object value)
    {
        if (value is Task task)
        {
            return task;
        }
        if (value == null || !IsValueTask(value.GetType()))
        {
            return null;
        }
        var asTask = value.GetType().GetMethod("AsTask", Type.EmptyTypes);
        return (Task)asTask.Invoke(value, null);
    }

    static bool IsValueTask(Type type)
    {
        if (type == typeof(ValueTask))
        {
            return true;
        }
        return type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(ValueTask<>);
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
            return Threw(e.InnerException);
        }
        // An async Task is a Task<VoidTaskResult> underneath, so the declared
        // type says whether there is a result.
        var type = run.ReturnType;
        bool result = type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(Task<>)
                || type.GetGenericTypeDefinition() == typeof(ValueTask<>));
        if (!result)
        {
            return "completed";
        }
        var typed = typeof(Task<>).MakeGenericType(type.GetGenericArguments());
        var value = typed.GetProperty("Result").GetValue(task);
        return "returned " + Describe(assembly, value);
    }

    /// An exception by its type and message, so that a rewrite that throws
    /// the same type with other words shows.
    static string Threw(Exception e)
    {
        return "threw " + e.GetType().Name + ": " + e.Message;
    }

    static string Describe(Assembly assembly, object value)
    {
        var describe = assembly.GetType("DiffFixture.Trace").GetMethod("Describe");
        return (string)describe.Invoke(null, new[] { value });
    }

    /// The static fields of the case and of the types written inside it
    /// after a run, so that a dropped store to one shows.
    static string Statics(Assembly assembly)
    {
        var text = new StringBuilder();
        AddStatics(assembly, assembly.GetType("DiffFixture.Case"), "", text);
        return text.ToString();
    }

    static void AddStatics(
        Assembly assembly, Type type, string prefix, StringBuilder text)
    {
        var fields = type
            .GetFields(AnyStatic | BindingFlags.DeclaredOnly)
            .Where(field => !field.IsLiteral)
            .OrderBy(field => field.Name, StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var value = Describe(assembly, field.GetValue(null));
            text.Append($"; {prefix}{field.Name}={value}");
        }
        // A generated type holds no state of the case's own: "<>c" caches
        // its lambdas. An open generic type has no statics to read.
        var nested = type
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => !t.Name.StartsWith('<') && !t.ContainsGenericParameters)
            .OrderBy(t => t.Name, StringComparer.Ordinal);
        foreach (var inner in nested)
        {
            AddStatics(assembly, inner, prefix + inner.Name + ".", text);
        }
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

    /// The inputs for the parameter at this place: bools, integers with
    /// their type's limits, the values of an enum and one it doesn't name,
    /// other numbers (with NaN and the infinities for double and float),
    /// nulls and the fake Unity object (UnityInputs).
    static List<Input> InputsFor(Type type, int place)
    {
        var inner = Nullable.GetUnderlyingType(type);
        if (inner != null)
        {
            var values = new List<Input> { Value("null", null) };
            values.AddRange(InputsFor(inner, place));
            return values;
        }
        if (type == typeof(bool))
        {
            return Values(false, true);
        }
        if (type.IsEnum)
        {
            return EnumValues(type);
        }
        var integers = Integers(type);
        if (integers != null)
        {
            return integers;
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
            return UnityInputs(place);
        }
        throw new Stop("The harness has no inputs for parameter type " + type);
    }

    /// Null, and an alive and a destroyed object, each alone and with an
    /// alive Next. Every object has a Count of its own, in every parameter,
    /// so a wrong object in the result, the log or a chain shows.
    static List<Input> UnityInputs(int place)
    {
        int count = 10 * (place + 1);
        return new List<Input>
        {
            Value("null", null),
            new Input("alive", a => NewObject(a, false, count, null), false),
            new Input(
                "alive, next", a => NewObject(a, false, count + 1, count + 2),
                false),
            new Input(
                "destroyed", a => NewObject(a, true, count + 3, null), true),
            new Input(
                "destroyed, next",
                a => NewObject(a, true, count + 4, count + 5),
                true),
        };
    }

    /// Small values and the type's limits, or null for a type that isn't an
    /// integer. The limits show a value of the wrong type in an intermediate
    /// result ("n + 1" and "n + 1L" differ only at int.MaxValue) and an
    /// overflow that checked arithmetic throws for.
    static List<Input> Integers(Type type)
    {
        if (type == typeof(int))
        {
            return Limits("int", int.MinValue, int.MaxValue, 0, 1, -1, 5);
        }
        if (type == typeof(long))
        {
            return Limits("long", long.MinValue, long.MaxValue, 0L, 1L, -1L, 5L);
        }
        if (type == typeof(short))
        {
            return Limits("short", short.MinValue, short.MaxValue,
                (short)0, (short)1, (short)-1);
        }
        if (type == typeof(sbyte))
        {
            return Limits("sbyte", sbyte.MinValue, sbyte.MaxValue,
                (sbyte)0, (sbyte)1, (sbyte)-1);
        }
        if (type == typeof(byte))
        {
            return Limits("byte", byte.MinValue, byte.MaxValue, (byte)1);
        }
        if (type == typeof(ushort))
        {
            return Limits("ushort", ushort.MinValue, ushort.MaxValue, (ushort)1);
        }
        if (type == typeof(uint))
        {
            return Limits("uint", uint.MinValue, uint.MaxValue, 1u);
        }
        if (type == typeof(ulong))
        {
            return Limits("ulong", ulong.MinValue, ulong.MaxValue, 1ul);
        }
        return null;
    }

    static List<Input> Limits(
        string name, object min, object max, params object[] small)
    {
        var values = Values(small);
        values.Add(Value(name + ".min", min));
        values.Add(Value(name + ".max", max));
        return values;
    }

    /// Every value the enum names, and the first it doesn't, which a switch
    /// sends to its default. Made in each assembly, because the original's
    /// enum is another type than the rewritten one's.
    static List<Input> EnumValues(Type type)
    {
        var underlying = Enum.GetUnderlyingType(type);
        var values = new List<Input>();
        foreach (var named in Enum.GetValues(type).Cast<object>().Distinct())
        {
            var raw = Convert.ChangeType(named, underlying);
            values.Add(EnumValue(type, type.Name + "." + named, raw));
        }
        for (long n = 0; ; n++)
        {
            var raw = Convert.ChangeType(n, underlying);
            if (!Enum.IsDefined(type, raw))
            {
                values.Add(EnumValue(type, type.Name + "(" + n + ")", raw));
                return values;
            }
        }
    }

    static Input EnumValue(Type type, string label, object raw)
    {
        return new Input(
            label, a => Enum.ToObject(a.GetType(type.FullName), raw), false);
    }

    static object NewObject(Assembly assembly, bool dead, int count, int? next)
    {
        var type = assembly.GetType("DiffFixture.UnityObject");
        var value = Activator.CreateInstance(type);
        type.GetField("Dead").SetValue(value, dead);
        type.GetField("Count").SetValue(value, count);
        if (next != null)
        {
            var alive = NewObject(assembly, false, next.Value, null);
            type.GetField("Next").SetValue(value, alive);
        }
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
