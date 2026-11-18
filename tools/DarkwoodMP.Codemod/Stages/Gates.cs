using DarkwoodMP.Codemod.Census;
using DarkwoodMP.Codemod.Loading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Stages;

/// What must hold for a stage's output before anything is written. Any failure
/// stops the stage with the files untouched.
sealed class Gates
{
    readonly PluginProject _project;
    readonly string _stage;
    readonly StageCommand.Pass _pass;

    public Gates(PluginProject project, string stage, StageCommand.Pass pass)
    {
        _project = project;
        _stage = stage;
        _pass = pass;
    }

    public void Run(List<(SyntaxTree Tree, string After)> rewritten)
    {
        var parse = _project.ParseOptions;
        foreach (var (tree, after) in rewritten)
        {
            CheckFile(tree, after, parse);
        }
        var compilation = Recompile(rewritten);
        CheckDll(compilation);
        Console.WriteLine($"{_stage}: all gates passed.");
    }

    void CheckFile(SyntaxTree tree, string after, CSharpParseOptions parse)
    {
        var file = Path.GetRelativePath(_project.SourceDir, tree.FilePath);
        if (after.Contains('\r'))
        {
            throw new Stop($"{file}: the output has a carriage return.");
        }
        var rootAfter = CSharpSyntaxTree.ParseText(after, parse).GetRoot();
        var words = CommentWords.FirstDifference(
            CommentWords.Of(tree.GetRoot()), CommentWords.Of(rootAfter));
        if (words != null)
        {
            throw new Stop($"{file}: comment words changed at {words}");
        }
        if (_stage == "layout" && !SameCode.Of(tree.GetRoot(), rootAfter))
        {
            throw new Stop(
                $"{file}: layout changed more than whitespace: "
                    + TokenDiff.First(tree.GetRoot(), rootAfter));
        }
        if (_stage == "braces")
        {
            var added = BracedCode.Difference(tree.GetRoot(), rootAfter);
            if (added != null)
            {
                throw new Stop($"{file}: braces changed more than braces: {added}");
            }
        }
        if (_pass(after, parse) != after)
        {
            throw new Stop($"{file}: a second {_stage} run changed the output.");
        }
        CheckEndState(file, rootAfter);
    }

    /// Nothing the stage is responsible for may be left in the file.
    void CheckEndState(string file, SyntaxNode root)
    {
        var census = new BraceCensus();
        census.Count(root, file);
        if (census.Braceless.Total > 0)
        {
            throw new Stop($"{file}: a braceless statement is left after {_stage}.");
        }
        if (_stage == "layout"
            && census.OneLineBlocks.Total + census.OneLineBodies.Total > 0)
        {
            throw new Stop($"{file}: a one-line block or body is left after layout.");
        }
    }

    CSharpCompilation Recompile(List<(SyntaxTree Tree, string After)> rewritten)
    {
        var parse = _project.ParseOptions;
        var changed = rewritten.ToDictionary(
            r => r.Tree.FilePath, r => r.After);
        var trees = new List<SyntaxTree>();
        foreach (var tree in _project.Compilation.SyntaxTrees)
        {
            if (changed.TryGetValue(tree.FilePath, out var after))
            {
                var text = SourceText.From(
                    after, System.Text.Encoding.UTF8, SourceHashAlgorithm.Sha256);
                trees.Add(CSharpSyntaxTree.ParseText(text, parse, tree.FilePath));
            }
            else
            {
                trees.Add(tree);
            }
        }
        var compilation = _project.Compilation
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(trees);
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new Stop(
                $"The rewrite no longer compiles: {errors.Count} errors. "
                    + "Nothing was written. First errors:\n"
                    + string.Join("\n", errors.Take(20).Select(e => "  " + e)));
        }
        return compilation;
    }

    void CheckDll(CSharpCompilation after)
    {
        var before = InMemoryDll.Bytes(_project.Compilation);
        var now = InMemoryDll.Bytes(after);
        if (!before.AsSpan().SequenceEqual(now))
        {
            throw new Stop(
                $"{_stage} changed the compiled DLL (hash "
                    + $"{InMemoryDll.Hash(before)} became {InMemoryDll.Hash(now)}). "
                    + "Nothing was written.");
        }
        Console.WriteLine(
            $"{_stage}: compiled DLL unchanged in memory ({InMemoryDll.Hash(now)}).");
    }
}
