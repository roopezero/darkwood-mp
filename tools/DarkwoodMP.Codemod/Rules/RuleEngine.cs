using System.Text;
using DarkwoodMP.Codemod.Census;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// Runs a rule over files (codemod.md section 7.5): plan on the original tree,
/// replace the planned spans, reparse, and compile with the tree replaced. A
/// site whose rewrite doesn't compile goes to the queue and the file is
/// rewritten without it. The layout stage's pass then formats the new code, so
/// it keeps IDE0011, IDE0055 and DW0002 at 0.
static class RuleEngine
{
    public static RuleResult Run(
        Rule rule,
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> targets,
        ReceiverFilter receivers,
        string sourceDir)
    {
        var result = new RuleResult(rule.Name);
        var finished = compilation;
        foreach (var tree in targets)
        {
            var file = Path.GetRelativePath(sourceDir, tree.FilePath)
                .Replace('\\', '/');
            var model = compilation.GetSemanticModel(tree);
            var plan = new RulePlan(rule.Name, model, receivers, file);
            rule.Plan(plan);
            var after = ApplyFile(rule, plan, compilation, result);
            finished = finished.ReplaceSyntaxTree(
                finished.SyntaxTrees.First(t => t.FilePath == tree.FilePath),
                Reparse(after, tree));
        }
        RequireClean(finished);
        result.CheckCensus();
        result.Manifest.Members.Sort(StringComparer.Ordinal);
        return result;
    }

    static SyntaxTree Reparse(string text, SyntaxTree tree)
    {
        var source = SourceText.From(text, Encoding.UTF8, SourceHashAlgorithm.Sha256);
        return CSharpSyntaxTree.ParseText(
            source, (CSharpParseOptions)tree.Options, tree.FilePath);
    }

    /// Applies the plan to one file and returns its final text. Once the
    /// file compiles, the rule rechecks the rewrites it made (Rule.Recheck),
    /// and those it gives up are queued as a compile error's are.
    static string ApplyFile(
        Rule rule,
        RulePlan plan,
        CSharpCompilation compilation,
        RuleResult result)
    {
        var tree = plan.Tree;
        var parse = (CSharpParseOptions)tree.Options;
        var text = tree.GetText().ToString();
        var final = text;
        var live = plan.Proposals.OrderBy(p => p.Span.Start).ToList();
        if (live.Count > 0 && Passes.Layout(text, parse) != text)
        {
            throw new Stop(
                plan.File + " isn't in the layout stage's form: run layout first.");
        }
        var banned = new List<(SyntaxNode Site, string Reason)>();
        var shown = new Dictionary<Proposal, string>();
        while (live.Count > 0)
        {
            var spliced = Splice(text, live, out var spans);
            var errors = Errors(
                compilation, tree, Reparse(spliced, tree), out var model);
            var given = new List<(Proposal Proposal, string Reason)>();
            if (errors.Count == 0)
            {
                given = rule.Recheck(plan, model, spans);
            }
            if (errors.Count == 0 && given.Count == 0)
            {
                final = Passes.Layout(spliced, parse);
                CheckFile(plan.File, text, final, parse);
                result.Rewritten[tree.FilePath] = final;
                shown = Shown(spliced, spans, final, parse);
                break;
            }
            if (errors.Count > 0)
            {
                given = Blame(errors, live, spans, plan);
            }
            foreach (var (proposal, reason) in given)
            {
                live.Remove(proposal);
                foreach (var site in proposal.Sites)
                {
                    banned.Add((site, reason));
                }
            }
        }
        Tally(plan, live, banned, final, shown, result);
        return final;
    }

    /// The new code of each proposal as the layout pass left it, from the
    /// start of its first line, for a readable site list. Layout changes
    /// only whitespace, so the proposal's tokens keep their places in the
    /// file's token stream. Empty if that doesn't hold.
    static Dictionary<Proposal, string> Shown(
        string spliced,
        Dictionary<Proposal, TextSpan> spans,
        string final,
        CSharpParseOptions parse)
    {
        var shown = new Dictionary<Proposal, string>();
        var before = CSharpSyntaxTree.ParseText(spliced, parse).GetRoot()
            .DescendantTokens().ToList();
        var after = CSharpSyntaxTree.ParseText(final, parse).GetRoot()
            .DescendantTokens().ToList();
        if (before.Count != after.Count)
        {
            return shown;
        }
        foreach (var (proposal, span) in spans)
        {
            int first = before.FindIndex(t => t.SpanStart >= span.Start);
            int last = before.FindLastIndex(t => t.Span.End <= span.End);
            if (first < 0 || last < first
                || before[first].Text != after[first].Text
                || before[last].Text != after[last].Text)
            {
                continue;
            }
            shown[proposal] = FromLineStart(
                final, after[first].SpanStart, after[last].Span.End);
        }
        return shown;
    }

    static string FromLineStart(string text, int start, int end)
    {
        int line = 0;
        if (start > 0)
        {
            line = text.LastIndexOf('\n', start - 1) + 1;
        }
        return text.Substring(line, end - line);
    }

    /// The file with every live proposal's span replaced, and where each new
    /// text landed.
    static string Splice(
        string text,
        List<Proposal> live,
        out Dictionary<Proposal, TextSpan> spans)
    {
        spans = new Dictionary<Proposal, TextSpan>();
        var output = new StringBuilder(text.Length);
        int position = 0;
        foreach (var proposal in live)
        {
            output.Append(text, position, proposal.Span.Start - position);
            spans[proposal] = new TextSpan(output.Length, proposal.Text.Length);
            output.Append(proposal.Text);
            position = proposal.Span.End;
        }
        output.Append(text, position, text.Length - position);
        return output.ToString();
    }

    static List<Diagnostic> Errors(
        CSharpCompilation compilation,
        SyntaxTree tree,
        SyntaxTree trial,
        out SemanticModel model)
    {
        model = compilation.ReplaceSyntaxTree(tree, trial)
            .GetSemanticModel(trial);
        return model
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
    }

    /// Which rewrites to give up, for the errors of this round. An error on a
    /// rewritten line blames that rewrite; any other error blames the rewrites
    /// of its member, or of the whole file when its member has none.
    static List<(Proposal, string)> Blame(
        List<Diagnostic> errors,
        List<Proposal> live,
        Dictionary<Proposal, TextSpan> spans,
        RulePlan plan)
    {
        var blamed = new Dictionary<Proposal, string>();
        foreach (var error in errors)
        {
            var reason = "did not compile: " + error.Id + " " + error.GetMessage();
            int position = error.Location.SourceSpan.Start;
            var hit = live.FirstOrDefault(
                p => spans[p].Contains(position) || spans[p].End == position);
            if (hit != null)
            {
                blamed.TryAdd(hit, reason);
                continue;
            }
            var near = NearProposals(error, live, spans);
            foreach (var proposal in near)
            {
                blamed.TryAdd(proposal, reason);
            }
        }
        if (blamed.Count == 0)
        {
            throw new Stop(
                $"{plan.File}: errors that no rewrite explains: {errors[0]}");
        }
        return blamed.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// The rewrites in the member around an error, or all of them.
    static List<Proposal> NearProposals(
        Diagnostic error,
        List<Proposal> live,
        Dictionary<Proposal, TextSpan> spans)
    {
        var token = error.Location.SourceTree.GetRoot().FindToken(
            error.Location.SourceSpan.Start);
        var member = token.Parent.AncestorsAndSelf()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m is not BaseTypeDeclarationSyntax
                && m is not BaseNamespaceDeclarationSyntax);
        if (member != null)
        {
            var inside = live
                .Where(p => member.FullSpan.Contains(spans[p]))
                .ToList();
            if (inside.Count > 0)
            {
                return inside;
            }
        }
        return live;
    }

    /// What must hold for a file before it may be written.
    static void CheckFile(
        string file, string before, string after, CSharpParseOptions parse)
    {
        if (Passes.Layout(after, parse) != after)
        {
            throw new Stop($"{file}: a second layout run changed the rewrite.");
        }
        var root = CSharpSyntaxTree.ParseText(after, parse).GetRoot();
        var census = new BraceCensus();
        census.Count(root, file);
        if (census.Braceless.Total + census.OneLineBlocks.Total
            + census.OneLineBodies.Total > 0)
        {
            throw new Stop($"{file}: the rewrite left a statement without "
                + "braces or a one-line block.");
        }
        var old = CommentWords.Of(before, parse);
        var fresh = CommentWords.Of(root);
        old.Sort(StringComparer.Ordinal);
        fresh.Sort(StringComparer.Ordinal);
        if (!old.SequenceEqual(fresh))
        {
            throw new Stop($"{file}: the rewrite changed the comment words.");
        }
    }

    /// The variables the proposal declares that its old text didn't and
    /// whose names end in a digit: the numbered temporaries.
    static void NoteTemps(
        string file, Proposal proposal, List<NumberedTemp> temps)
    {
        var old = Declared(proposal.Before);
        foreach (var name in Declared(proposal.Text))
        {
            if (!old.Contains(name)
                && char.IsDigit(name[name.Length - 1])
                && !temps.Any(t => t.File == file
                    && t.Member == proposal.Member && t.Name == name))
            {
                temps.Add(new NumberedTemp
                {
                    File = file,
                    Member = proposal.Member,
                    Name = name,
                });
            }
        }
    }

    static List<string> Declared(string statements)
    {
        return SyntaxFactory.ParseStatement("{\n" + statements + "\n}")
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(declarator => declarator.Identifier.ValueText)
            .ToList();
    }

    /// Records the fate of every site of the file.
    static void Tally(
        RulePlan plan,
        List<Proposal> live,
        List<(SyntaxNode Site, string Reason)> banned,
        string final,
        Dictionary<Proposal, string> shown,
        RuleResult result)
    {
        var text = plan.Tree.GetText().ToString();
        var parse = (CSharpParseOptions)plan.Tree.Options;
        Add(result.Before, OperatorCount.Of(plan.Root));
        var root = CSharpSyntaxTree.ParseText(final, parse).GetRoot();
        Add(result.After, OperatorCount.Of(root));
        foreach (var proposal in live)
        {
            foreach (var site in proposal.Sites)
            {
                Count(result.Removed, site);
                result.Sites.Add(new SiteEntry
                {
                    File = plan.File,
                    Line = LineOf(site),
                    Member = proposal.Member,
                    Before = proposal.Before,
                    After = proposal.Text,
                    Comment = plan.CommentOf(site),
                    OldCode = FromLineStart(
                        text, proposal.Span.Start, proposal.Span.End),
                    NewCode = shown.GetValueOrDefault(proposal, proposal.Text),
                });
            }
            if (!result.Manifest.Members.Contains(proposal.Member))
            {
                result.Manifest.Members.Add(proposal.Member);
            }
            if (proposal.Strings != null)
            {
                result.Manifest.StringChanges.Add(proposal.Strings);
            }
            NoteTemps(plan.File, proposal, result.Manifest.Temps);
        }
        foreach (var (site, reason) in plan.Queued.Concat(banned))
        {
            Count(result.Queued, site);
            result.Queue.Add(new QueueEntry
            {
                File = plan.File,
                Line = LineOf(site),
                Rule = plan.RuleName,
                Reason = reason,
                Snippet = site.ToString(),
            });
        }
    }

    static int LineOf(SyntaxNode site)
    {
        return site.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    static void Count(int[] counts, SyntaxNode site)
    {
        var kind = OperatorCount.KindOf(site);
        if (kind != null)
        {
            counts[(int)kind.Value]++;
        }
    }

    static void Add(int[] totals, int[] counts)
    {
        for (int i = 0; i < totals.Length; i++)
        {
            totals[i] += counts[i];
        }
    }

    /// The whole compilation must still be free of errors.
    static void RequireClean(CSharpCompilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (errors.Count > 0)
        {
            throw new Stop(
                $"The rewritten sources have {errors.Count} errors. Nothing "
                    + "was written. First errors:\n"
                    + string.Join("\n", errors.Take(20).Select(e => "  " + e)));
        }
    }
}
