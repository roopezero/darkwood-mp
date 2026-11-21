using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Rules;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Interpolated;

/// What must hold before stage 11 writes anything. Each failure stops the
/// command with exit code 2 and names the file or member:
///
/// - the rewritten sources compile with no errors;
/// - the code is the same once every string is spelled out and the parts
///   of each + chain joined (JoinedParts), so the parts of each split
///   string, joined, are the original text and holes in order;
/// - the comment words are the same and every comment is where the wrap
///   stage may put it;
/// - every call binds to the same method and every argument has the same
///   type and converted type, in order;
/// - the layout pass keeps the output, and a second run changes nothing;
/// - every manifest member is a method of the built DLL;
/// - the IL differs only in the manifest's members, and in each of them the
///   string constants lost and gained hold the same literal characters
///   (format items aside). The records of those strings go in the manifest.
static class InterpolatedGates
{
    public static void Run(CSharpCompilation compilation, SplitResult result)
    {
        if (result.Rewritten.Count == 0)
        {
            return;
        }
        var after = compilation;
        var trees = new List<(SyntaxTree Before, SyntaxTree After)>();
        foreach (var (tree, text) in result.Rewritten)
        {
            var parse = (CSharpParseOptions)tree.Options;
            var next = CSharpSyntaxTree.ParseText(
                text, parse, tree.FilePath, tree.Encoding);
            after = after.ReplaceSyntaxTree(tree, next);
            trees.Add((tree, next));
        }
        var errors = after.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .ToList();
        if (errors.Count > 0)
        {
            throw new Stop("interpolated: the rewritten sources don't "
                + "compile:\n  " + string.Join("\n  ", errors));
        }
        foreach (var (before, now) in trees)
        {
            Text(before, now);
            Calls(compilation, before, after, now);
            Again(after, now);
        }
        RuleCommand.CheckMembers(after, result.Manifest);
        Il(compilation, after, result.Manifest);
    }

    static void Text(SyntaxTree before, SyntaxTree after)
    {
        var file = before.FilePath;
        var a = before.GetRoot();
        var b = after.GetRoot();
        if (!JoinedParts.SameCode(a, b))
        {
            throw new Stop($"interpolated: the joined parts differ from the "
                + $"original in {file}: {JoinedParts.FirstDifference(a, b)}");
        }
        var words = CommentWords.FirstDifference(
            CommentWords.ForWrap(CommentWords.Of(a)),
            CommentWords.ForWrap(CommentWords.Of(b)));
        if (words != null)
        {
            throw new Stop($"interpolated: comment words changed in {file} "
                + "at " + words);
        }
        var moved = WrapPlaces.Difference(
            JoinedParts.Joined(a), JoinedParts.Joined(b));
        if (moved != null)
        {
            throw new Stop($"interpolated: a comment moved in {file}: {moved}");
        }
        var text = after.GetText().ToString();
        var parse = (CSharpParseOptions)after.Options;
        if (Passes.Layout(text, parse) != text)
        {
            throw new Stop("interpolated: the layout pass changes " + file);
        }
        bool hadVersion = Wrap.VersionLine.IsMatch(before.GetText().ToString());
        if (hadVersion && !Wrap.VersionLine.IsMatch(text))
        {
            throw new Stop("interpolated: the protocol version line changed "
                + "in " + file);
        }
    }

    /// Every call, creation and argument, in order, with what it binds to.
    static void Calls(
        CSharpCompilation compilation,
        SyntaxTree before,
        CSharpCompilation after,
        SyntaxTree now)
    {
        var was = Bindings(compilation.GetSemanticModel(before), before);
        var now_ = Bindings(after.GetSemanticModel(now), now);
        for (int i = 0; i < Math.Max(was.Count, now_.Count); i++)
        {
            var a = "(nothing)";
            var b = "(nothing)";
            if (i < was.Count)
            {
                a = was[i];
            }
            if (i < now_.Count)
            {
                b = now_[i];
            }
            if (a != b)
            {
                throw new Stop($"interpolated: a call or argument binds "
                    + $"differently in {before.FilePath}: {a} became {b}");
            }
        }
    }

    static List<string> Bindings(SemanticModel model, SyntaxTree tree)
    {
        var list = new List<string>();
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            switch (node)
            {
                case InvocationExpressionSyntax:
                case BaseObjectCreationExpressionSyntax:
                    var symbol = model.GetSymbolInfo(node).Symbol;
                    list.Add("call " + symbol?.ToDisplayString());
                    break;
                case ArgumentSyntax argument:
                    var info = model.GetTypeInfo(argument.Expression);
                    list.Add($"argument {info.Type?.ToDisplayString()} as "
                        + info.ConvertedType?.ToDisplayString());
                    break;
            }
        }
        return list;
    }

    static void Again(CSharpCompilation after, SyntaxTree now)
    {
        var (again, _) = InterpolatedStage.Run(after, now, now.FilePath);
        if (again != now.GetText().ToString())
        {
            throw new Stop("interpolated: a second run changes " + now.FilePath);
        }
    }

    static void Il(
        CSharpCompilation before, CSharpCompilation after, Manifest manifest)
    {
        var a = InMemoryDll.Bytes(before);
        var b = InMemoryDll.Bytes(after);
        manifest.StringChanges = IlDiff.StringChanges(
            a, b, manifest, InterpolatedCommand.Reason);
        foreach (var change in manifest.StringChanges)
        {
            var lost = LiteralChars(change.Removed);
            var gained = LiteralChars(change.Added);
            if (lost != gained)
            {
                throw new Stop($"interpolated: the string constants of "
                    + $"{change.Member} hold other literal text: removed "
                    + string.Join(" | ", change.Removed) + "; added "
                    + string.Join(" | ", change.Added));
            }
        }
        var report = IlDiff.Compare(a, b, manifest);
        if (!report.Passed)
        {
            report.Print(Console.Out);
            throw new Stop("interpolated: the IL changed outside the "
                + "manifest (above)");
        }
    }

    /// The characters of the strings' literal text, sorted: a format item
    /// such as {0,-5:0.0} counts for nothing and {{ and }} for one brace.
    /// Cutting a string, or folding a part into a neighbouring constant,
    /// moves these characters between constants but never adds or drops one.
    static string LiteralChars(List<string> strings)
    {
        var chars = new List<char>();
        foreach (var s in strings)
        {
            for (int i = 0; i < s.Length; i++)
            {
                bool doubled = i + 1 < s.Length && s[i + 1] == s[i];
                if ((s[i] == '{' || s[i] == '}') && doubled)
                {
                    chars.Add(s[i]);
                    i++;
                    continue;
                }
                if (s[i] == '{' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                {
                    int close = s.IndexOf('}', i);
                    if (close > 0)
                    {
                        i = close;
                        continue;
                    }
                }
                chars.Add(s[i]);
            }
        }
        chars.Sort();
        return new string(chars.ToArray());
    }
}
