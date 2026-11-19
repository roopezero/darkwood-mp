using DarkwoodMP.Codemod.Census;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// One file as a rule sees it. Rules plan on the original tree, so the
/// semantic model stays valid, and every site gets exactly one fate:
/// Propose, Queue or Later. A site a rule doesn't mention counts as left for
/// a later rule.
sealed class RulePlan
{
    public readonly string RuleName;
    public readonly SyntaxTree Tree;
    public readonly SyntaxNode Root;
    public readonly SemanticModel Model;
    public readonly ReceiverFilter Receivers;
    public readonly Purity Purity;
    public readonly string File;

    internal readonly List<Proposal> Proposals = new();
    internal readonly List<(SyntaxNode Site, string Reason)> Queued = new();
    internal readonly List<SyntaxNode> Left = new();

    readonly string _text;
    readonly HashSet<TextSpan> _reported = new();
    readonly Dictionary<SyntaxNode, TempNames> _temps = new();

    public RulePlan(
        string ruleName,
        SemanticModel model,
        ReceiverFilter receivers,
        string file)
    {
        RuleName = ruleName;
        Tree = model.SyntaxTree;
        Root = Tree.GetRoot();
        Model = model;
        Receivers = receivers;
        Purity = new Purity(model);
        File = file;
        _text = Tree.GetText().ToString();
    }

    /// Does this receiver belong to the run's --receivers?
    public bool ReceiverMatches(ExpressionSyntax receiver)
    {
        if (Receivers == ReceiverFilter.All)
        {
            return true;
        }
        var unity = Model.Compilation.GetTypeByMetadataName("UnityEngine.Object");
        var type = Model.GetTypeInfo(receiver).Type;
        var kind = OperandClass.TypeClass(type, unity);
        bool isUnity = kind == "UnityEngine.Object (derived)"
            || kind == "type parameter : UnityEngine.Object";
        return isUnity == (Receivers == ReceiverFilter.Unity);
    }

    /// The names already taken in the member around a site.
    public TempNames Temps(SyntaxNode site)
    {
        var member = MemberOf(site);
        if (!_temps.TryGetValue(member, out var names))
        {
            names = new TempNames(member);
            _temps[member] = names;
        }
        return names;
    }

    /// The outermost declaration that holds the site, or the file's root.
    static SyntaxNode MemberOf(SyntaxNode site)
    {
        foreach (var ancestor in site.Ancestors())
        {
            if (ancestor is BaseMethodDeclarationSyntax
                || ancestor is BasePropertyDeclarationSyntax
                || ancestor is BaseFieldDeclarationSyntax)
            {
                return ancestor;
            }
        }
        return site.SyntaxTree.GetRoot();
    }

    /// Replace a statement with these statements. The statement must sit in a
    /// block or a switch section, so that its replacement can be several.
    public void Propose(
        SyntaxNode site,
        StatementSyntax target,
        string text,
        StringChange strings = null)
    {
        ProposeAll(new[] { site }, target, text, strings);
    }

    /// The same, for a rewrite that removes several sites at once.
    public void ProposeAll(
        IReadOnlyList<SyntaxNode> sites,
        StatementSyntax target,
        string text,
        StringChange strings = null)
    {
        ProposeStatement(sites, target, target.Span, text, strings);
    }

    /// The same for a span that starts with the statement and can take in
    /// the comment behind it on its line, which the new text then places.
    public void ProposeStatement(
        IReadOnlyList<SyntaxNode> sites,
        StatementSyntax target,
        TextSpan span,
        string text,
        StringChange strings = null)
    {
        if (target.Parent is not BlockSyntax
            && target.Parent is not SwitchSectionSyntax)
        {
            QueueAll(sites, "the statement isn't in a block or switch section");
            return;
        }
        if (span.Start != target.SpanStart || span.End < target.Span.End)
        {
            throw new Stop(
                $"{File}: rule {RuleName} proposed a span that doesn't start "
                    + $"with its statement: {target}");
        }
        ProposeSpan(sites, span, text, strings);
    }

    /// Replace any span, such as an expression body with a block. The old and
    /// the new text are checked as statements.
    public void ProposeSpan(
        IReadOnlyList<SyntaxNode> sites,
        TextSpan span,
        string text,
        StringChange strings = null)
    {
        Offer(sites, span, text, strings, StatementFragment);
    }

    /// Replace a whole member, such as a property whose expression body
    /// becomes an accessor with a block, and maybe the comment behind it on
    /// its line. An accessor list doesn't parse as statements, so the old and
    /// the new text are checked as members of a type.
    public void ProposeMember(
        IReadOnlyList<SyntaxNode> sites,
        MemberDeclarationSyntax member,
        TextSpan span,
        string text,
        StringChange strings = null)
    {
        if (span.Start != member.SpanStart || span.End < member.Span.End)
        {
            throw new Stop(
                $"{File}: rule {RuleName} proposed a span that doesn't start "
                    + $"with its member: {member}");
        }
        Offer(sites, span, text, strings, MemberFragment);
    }

    void Offer(
        IReadOnlyList<SyntaxNode> sites,
        TextSpan span,
        string text,
        StringChange strings,
        Func<string, CSharpParseOptions, SyntaxNode> fragment)
    {
        foreach (var site in sites)
        {
            Report(site);
        }
        var before = _text.Substring(span.Start, span.Length);
        var refused = Refusal(before, text, fragment);
        if (refused != null)
        {
            Enqueue(sites, refused);
            return;
        }
        if (Proposals.Any(p => p.Span.OverlapsWith(span)))
        {
            Left.AddRange(sites);
            return;
        }
        Proposals.Add(new Proposal
        {
            Sites = sites,
            Span = span,
            Text = text,
            Before = before,
            Member = MemberKeys.Of(sites[0], Model),
            Strings = strings,
        });
    }

    /// Leave a site to a person or an LLM.
    public void Queue(SyntaxNode site, string reason)
    {
        Report(site);
        Queued.Add((site, reason));
    }

    /// Leave a site for a later rule.
    public void Later(SyntaxNode site, string reason)
    {
        Report(site);
        Left.Add(site);
    }

    void QueueAll(IReadOnlyList<SyntaxNode> sites, string reason)
    {
        foreach (var site in sites)
        {
            Queue(site, reason);
        }
    }

    void Enqueue(IReadOnlyList<SyntaxNode> sites, string reason)
    {
        foreach (var site in sites)
        {
            Queued.Add((site, reason));
        }
    }

    /// A rule that reports the same site twice has a bug.
    void Report(SyntaxNode site)
    {
        if (!_reported.Add(site.Span))
        {
            throw new Stop(
                $"{File}: rule {RuleName} reported a site twice: {site}");
        }
    }

    /// Why a replacement is refused without compiling it: it doesn't parse,
    /// loses a comment, or adds a lambda or local function, which would add a
    /// stack frame around code the game's stack readers look at.
    string Refusal(
        string before,
        string text,
        Func<string, CSharpParseOptions, SyntaxNode> fragment)
    {
        var parse = (CSharpParseOptions)Tree.Options;
        var old = fragment(before, parse);
        var fresh = fragment(text, parse);
        if (fresh.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return "the rewrite doesn't parse";
        }
        var kept = CommentWords.Of(fresh);
        var had = CommentWords.Of(old);
        kept.Sort(StringComparer.Ordinal);
        had.Sort(StringComparer.Ordinal);
        if (!kept.SequenceEqual(had))
        {
            return "a comment inside the statement would be lost";
        }
        if (Frames(fresh) > Frames(old))
        {
            return "the rewrite would add a lambda or local function";
        }
        return null;
    }

    static SyntaxNode StatementFragment(string text, CSharpParseOptions parse)
    {
        var block = "{\n" + text + "\n}";
        return CSharpSyntaxTree.ParseText(block, parse).GetRoot();
    }

    static SyntaxNode MemberFragment(string text, CSharpParseOptions parse)
    {
        var type = "class Fragment\n{\n" + text + "\n}";
        return CSharpSyntaxTree.ParseText(type, parse).GetRoot();
    }

    static int Frames(SyntaxNode node)
    {
        return node.DescendantNodes().Count(
            n => n is AnonymousFunctionExpressionSyntax
                || n is LocalFunctionStatementSyntax);
    }
}
