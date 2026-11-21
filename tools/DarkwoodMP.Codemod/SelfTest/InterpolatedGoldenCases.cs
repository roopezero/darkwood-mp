using DarkwoodMP.Codemod.Interpolated;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// Stage 11's golden cases in SelfTest/Interpolated/*.golden: an "===
/// input" section, what the stage must make of it in "=== split", and in
/// "=== report" each long line it leaves with a string whole, "line:
/// reason" (lines of the output). Every case also runs the stage's gates
/// (InterpolatedGates): it compiles, the joined parts are the original,
/// the calls bind the same, the layout pass keeps the output, a second
/// run changes nothing and the IL changes only in the manifest's members.
static class InterpolatedGoldenCases
{
    const string Folder = "tools/DarkwoodMP.Codemod/SelfTest/Interpolated";

    public static List<string> Files(string root)
    {
        return Directory.GetFiles(Path.Combine(root, Folder), "*.golden")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static string NameOf(string file)
    {
        return "interpolated/" + Path.GetFileNameWithoutExtension(file);
    }

    public static string Show(string file)
    {
        var input = Sections(file)["input"];
        var (output, report, _) = Split(input);
        return "=== input\n" + input + "=== split\n" + output
            + "=== report\n" + report;
    }

    public static List<string> Failures(string file)
    {
        var problems = new List<string>();
        var sections = Sections(file);
        string output;
        string report;
        try
        {
            string gates;
            (output, report, gates) = Split(sections["input"]);
            if (gates != null)
            {
                problems.Add("  a gate failed: " + gates);
            }
        }
        catch (Stop stop)
        {
            problems.Add("  stopped: " + stop.Message);
            return problems;
        }
        sections.TryGetValue("split", out var expected);
        if (expected != output)
        {
            problems.Add("  split: output differs from the golden file");
            problems.Add(LineDiff.Of(expected ?? "", output));
        }
        sections.TryGetValue("report", out var listed);
        if ((listed ?? "").Trim() != report.Trim())
        {
            problems.Add("  report differs from the golden file:\n" + report);
        }
        return problems;
    }

    /// The stage's output, its report and the first gate that failed (null
    /// when all passed).
    static (string Output, string Report, string Gates) Split(string input)
    {
        var tree = CSharpSyntaxTree.ParseText(
            input, SnippetCompiler.Parse, "golden.cs");
        var compilation = CSharpCompilation.Create(
            "golden",
            new[] { tree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithDeterministic(true));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new Stop("the input doesn't compile: " + errors[0]);
        }
        var result = InterpolatedCommand.Split(compilation, new[] { tree }, ".");
        var output = input;
        if (result.Rewritten.Count > 0)
        {
            output = result.Rewritten[0].After;
        }
        var report = string.Concat(
            result.Whole.Select(w => $"{w.Line}: {w.Reason}\n"));
        string gates = null;
        try
        {
            InterpolatedGates.Run(compilation, result);
        }
        catch (Stop stop)
        {
            gates = stop.Message;
        }
        return (output, report, gates);
    }

    static Dictionary<string, string> Sections(string file)
    {
        var sections = new Dictionary<string, List<string>>();
        List<string> current = null;
        foreach (var line in File.ReadAllLines(file))
        {
            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                current = new List<string>();
                sections[line.Substring(4).Trim()] = current;
            }
            else if (current != null)
            {
                current.Add(line);
            }
        }
        if (!sections.ContainsKey("input"))
        {
            throw new Stop("Golden case " + file + " has no input section.");
        }
        return sections.ToDictionary(
            s => s.Key, s => string.Join("\n", s.Value) + "\n");
    }
}
