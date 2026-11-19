using System.Text;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.SelfTest;

/// The golden cases of the operator rules, one folder per rule under
/// SelfTest/Rules. A case has an "=== input" section, the file the rule must
/// make of it in "=== output", and the sites it must leave in the queue in
/// "=== queue", one "line: reason" each (no section: none). Lines before the
/// first section describe the case. For every case the rule's second run
/// must change nothing and the IL diff of input and output must pass with
/// the rule's own manifest.
static class RuleGoldenCases
{
    const string Folder = "tools/DarkwoodMP.Codemod/SelfTest/Rules";

    public static List<string> Files(string root)
    {
        var folder = Path.Combine(root, Folder);
        if (!Directory.Exists(folder))
        {
            return new List<string>();
        }
        return Directory
            .GetFiles(folder, "*.golden", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// "rule/case", as --show takes it.
    public static string NameOf(string file)
    {
        var rule = Path.GetFileName(Path.GetDirectoryName(file));
        return rule + "/" + Path.GetFileNameWithoutExtension(file);
    }

    /// Everything wrong with one case; empty when it passes.
    public static List<string> Failures(string file)
    {
        var problems = new List<string>();
        var sections = Sections(file);
        var rule = RuleOf(file);
        if (!sections.TryGetValue("input", out var input))
        {
            problems.Add("  no input section");
            return problems;
        }
        if (Passes.Layout(input, SnippetCompiler.Parse) != input)
        {
            problems.Add("  the input isn't in the layout stage's form");
            return problems;
        }
        var result = Apply(rule, input, out var compilation, out var tree);
        var output = Output(result, tree, input);
        Compare("output", Expected(sections, "output"), output, problems);
        Compare("queue", Expected(sections, "queue"), Queue(result), problems);
        var again = Apply(rule, output, out _, out var second);
        if (again.Rewritten.ContainsKey(second.FilePath))
        {
            problems.Add("  a second run changed the output:\n"
                + LineDiff.Of(output, again.Rewritten[second.FilePath]));
        }
        if (result.Rewritten.Count > 0)
        {
            CheckIl(compilation, tree, output, result.Manifest, problems);
        }
        return problems;
    }

    /// The case's file with the actual output and queue, for reading.
    public static string Show(string file)
    {
        var sections = Sections(file);
        var input = sections["input"];
        var result = Apply(RuleOf(file), input, out _, out var tree);
        return "=== input\n" + input + "=== output\n"
            + Output(result, tree, input) + "=== queue\n" + Queue(result);
    }

    static Rule RuleOf(string file)
    {
        var name = Path.GetFileName(Path.GetDirectoryName(file));
        var rule = RuleRegistry.Find(name);
        if (rule == null)
        {
            throw new Stop("No rule named " + name + " for " + file);
        }
        return rule;
    }

    static RuleResult Apply(
        Rule rule,
        string source,
        out CSharpCompilation compilation,
        out SyntaxTree tree)
    {
        var text = SourceText.From(
            source, Encoding.UTF8, SourceHashAlgorithm.Sha256);
        tree = CSharpSyntaxTree.ParseText(
            text, SnippetCompiler.Parse, "/g/case.cs");
        compilation = CSharpCompilation.Create(
            "golden",
            new[] { tree },
            SnippetCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(true)
                .WithDeterministic(true));
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new Stop(
                "A rule's golden input doesn't compile: " + errors[0]);
        }
        return RuleEngine.Run(
            rule, compilation, new[] { tree }, ReceiverFilter.All, "/g");
    }

    static string Output(RuleResult result, SyntaxTree tree, string input)
    {
        if (result.Rewritten.TryGetValue(tree.FilePath, out var after))
        {
            return after;
        }
        return input;
    }

    static string Queue(RuleResult result)
    {
        var lines = result.Queue
            .OrderBy(q => q.Line)
            .ThenBy(q => q.Snippet.Length)
            .Select(q => q.Line + ": " + q.Reason);
        return string.Concat(lines.Select(line => line + "\n"));
    }

    /// The members that changed must be the manifest's, with no new frame
    /// and no string constant changed without a record.
    static void CheckIl(
        CSharpCompilation compilation,
        SyntaxTree tree,
        string output,
        Manifest manifest,
        List<string> problems)
    {
        var text = SourceText.From(
            output, Encoding.UTF8, SourceHashAlgorithm.Sha256);
        var rewritten = compilation.ReplaceSyntaxTree(
            tree,
            CSharpSyntaxTree.ParseText(
                text, SnippetCompiler.Parse, tree.FilePath));
        RuleCommand.CheckMembers(compilation, manifest);
        var report = IlDiff.Compare(
            Emit(compilation), Emit(rewritten), manifest);
        foreach (var problem in report.Problems)
        {
            problems.Add("  ildiff: " + problem);
        }
    }

    static byte[] Emit(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new Stop("A rule's golden case doesn't emit: "
                + result.Diagnostics.First());
        }
        return stream.ToArray();
    }

    static void Compare(
        string section, string expected, string actual, List<string> problems)
    {
        if (expected != actual)
        {
            problems.Add($"  {section} differs from the golden file");
            problems.Add(LineDiff.Of(expected, actual));
        }
    }

    static string Expected(Dictionary<string, string> sections, string name)
    {
        if (sections.TryGetValue(name, out var text))
        {
            return text;
        }
        return "";
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
        var texts = new Dictionary<string, string>();
        foreach (var (name, lines) in sections)
        {
            while (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }
            var joined = string.Concat(lines.Select(l => l + "\n"));
            texts[name] = joined;
        }
        return texts;
    }
}
