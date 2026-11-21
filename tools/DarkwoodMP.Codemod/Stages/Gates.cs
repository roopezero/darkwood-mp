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
        var wordsBefore = CommentWords.Of(tree.GetRoot());
        var wordsAfter = CommentWords.Of(rootAfter);
        if (_stage == "wrap")
        {
            wordsBefore = CommentWords.ForWrap(wordsBefore);
            wordsAfter = CommentWords.ForWrap(wordsAfter);
        }
        var words = CommentWords.FirstDifference(wordsBefore, wordsAfter);
        if (words != null)
        {
            throw new Stop($"{file}: comment words changed at {words}");
        }
        if (_stage == "layout")
        {
            CheckLayout(file, tree.GetRoot(), rootAfter);
        }
        if (_stage == "braces")
        {
            var added = BracedCode.Difference(tree.GetRoot(), rootAfter);
            if (added != null)
            {
                throw new Stop($"{file}: braces changed more than braces: {added}");
            }
        }
        if (_stage == "wrap")
        {
            CheckWrap(file, tree, after, parse);
        }
        if (_stage == "blank")
        {
            CheckBlank(file, tree, after, parse);
        }
        if (_pass(after, parse) != after)
        {
            throw new Stop($"{file}: a second {_stage} run changed the output.");
        }
        CheckEndState(file, rootAfter);
    }

    /// The layout stage changes whitespace, and moves comments only in the
    /// ways it is meant to (see CommentPlaces).
    void CheckLayout(string file, SyntaxNode before, SyntaxNode after)
    {
        if (!SameCode.Of(before, after))
        {
            throw new Stop(
                $"{file}: layout changed more than whitespace: "
                    + TokenDiff.First(before, after));
        }
        var moved = CommentPlaces.Difference(before, after);
        if (moved != null)
        {
            throw new Stop($"{file}: layout moved a comment: {moved}");
        }
    }

    /// The wrap stage changes whitespace and splits plain strings, which the
    /// compiler folds back, and moves comments only as WrapPlaces allows. The
    /// Formatter must keep what it wrote, and the
    /// line the Python tests read the protocol version from must still match.
    void CheckWrap(
        string file, SyntaxTree tree, string after, CSharpParseOptions parse)
    {
        var before = tree.GetRoot();
        var rootAfter = CSharpSyntaxTree.ParseText(after, parse).GetRoot();
        bool split = JoinedStrings.Count(rootAfter)
            != JoinedStrings.Count(before);
        bool same = SameCode.Of(before, rootAfter);
        if (split)
        {
            same = JoinedStrings.SameCode(before, rootAfter);
        }
        if (!same)
        {
            throw new Stop(
                $"{file}: wrap changed more than whitespace and string "
                    + "splits: " + TokenDiff.First(before, rootAfter));
        }
        var moved = WrapPlaces.Of(before, rootAfter, split);
        if (moved != null)
        {
            throw new Stop($"{file}: wrap moved a comment: {moved}");
        }
        if (Format.Passes.Layout(after, parse) != after)
        {
            throw new Stop($"{file}: the layout pass changes wrap's output.");
        }
        var version = Format.Wrap.VersionLine.Match(tree.GetText().ToString());
        if (version.Success && !after.Contains(version.Value))
        {
            throw new Stop(
                $"{file}: the line '{version.Value}' that "
                    + "tools/tests/worldtest.py reads is gone.");
        }
    }

    /// The blank stage only puts empty lines between the lines that were
    /// there, so the code and every comment stay where they were. The
    /// Formatter must keep them: a file it left alone before, it leaves alone
    /// after. Every place rule DW0007 still reports is one the stage says it
    /// left alone, and those are listed.
    void CheckBlank(
        string file, SyntaxTree tree, string after, CSharpParseOptions parse)
    {
        var before = tree.GetRoot();
        var rootAfter = CSharpSyntaxTree.ParseText(after, parse).GetRoot();
        if (!SameCode.Of(before, rootAfter))
        {
            throw new Stop(
                $"{file}: blank changed more than whitespace: "
                    + TokenDiff.First(before, rootAfter));
        }
        var moved = CommentPlaces.Difference(before, rootAfter);
        if (moved != null)
        {
            throw new Stop($"{file}: blank moved a comment: {moved}");
        }
        var extra = OnlyEmptyLinesAdded(tree.GetText().ToString(), after);
        if (extra != null)
        {
            throw new Stop($"{file}: blank changed a line: {extra}");
        }
        var source = tree.GetText().ToString();
        if (Format.Passes.Layout(source, parse) == source
            && Format.Passes.Layout(after, parse) != after)
        {
            throw new Stop($"{file}: the layout pass changes blank's output.");
        }
        var found = DarkwoodMP.Analyzers.BlankLines.Find(
            CSharpSyntaxTree.ParseText(after, parse));
        var left = Format.Blank.LeftAlone(after, parse);
        if (found.Length != left.Count)
        {
            throw new Stop(
                $"{file}: {found.Length} places still lack a blank line "
                    + $"after blank, and it left {left.Count} alone.");
        }
        foreach (var place in left)
        {
            Console.WriteLine(
                $"{_stage}: left {file}:{place.Line}: {place.Reason}");
        }
    }

    /// Null when after is before with only empty lines put between its lines;
    /// otherwise the first line of after that isn't.
    public static string OnlyEmptyLinesAdded(string before, string after)
    {
        var was = before.Split('\n');
        var now = after.Split('\n');
        int i = 0;
        for (int j = 0; j < now.Length; j++)
        {
            if (i < was.Length && now[j] == was[i])
            {
                i++;
            }
            else if (now[j].Length != 0)
            {
                return $"line {j + 1}: '{now[j]}'";
            }
        }
        if (i != was.Length)
        {
            return $"line {i + 1} of the input is gone";
        }
        return null;
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
