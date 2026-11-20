using System.Text.Json.Serialization;
using DarkwoodMP.Codemod.Il;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// --receivers: which null checks a rule works on in this run.
enum ReceiverFilter
{
    All,
    Unity,
    Other,
}

/// One rewrite rule. It looks at a file through a RulePlan and says, site by
/// site, what to do; the engine does the rest (see RuleEngine).
abstract class Rule
{
    public abstract string Name { get; }

    public abstract void Plan(RulePlan plan);

    /// The proposals of a file the rule gives up after all, each with the
    /// reason, once the file compiles with every live proposal in place.
    /// Rewritten is that file's model and spans says where each proposal's
    /// text landed in it. The engine queues their sites and tries the file
    /// again without them. Most rules have nothing to check here.
    public virtual List<(Proposal Proposal, string Reason)> Recheck(
        RulePlan plan,
        SemanticModel rewritten,
        IReadOnlyDictionary<Proposal, TextSpan> spans)
    {
        return new List<(Proposal Proposal, string Reason)>();
    }

    /// Where "codemod rule" writes the readable list of rewritten sites,
    /// relative to the repo's root, when it rewrites the plugin's own
    /// sources; null for no list. The list goes into the stage's commit.
    public virtual string SiteList => null;

    /// The lines that open the list, after its title: what the rewrite
    /// changes.
    public virtual string SiteListNote => "";
}

/// A rewrite the rule wants: this span of the file becomes this text.
sealed class Proposal
{
    public IReadOnlyList<SyntaxNode> Sites;
    public TextSpan Span;
    public string Text;
    public string Before;
    public string Member;
    public StringChange Strings;
}

/// A line of queue.jsonl: a site left for a person or an LLM.
sealed class QueueEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; }

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("rule")]
    public string Rule { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; }

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; }
}

/// A line of sites.jsonl: a site that was rewritten.
sealed class SiteEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; }

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("member")]
    public string Member { get; set; }

    [JsonPropertyName("before")]
    public string Before { get; set; }

    [JsonPropertyName("after")]
    public string After { get; set; }

    /// Where the rewrite put the comment it took in from behind the
    /// statement, and the comment; absent when it took in none.
    [JsonPropertyName("comment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Comment { get; set; }

    /// The old code from the start of its first line, for the readable
    /// site list (SiteListText); not in sites.jsonl.
    [JsonIgnore]
    public string OldCode { get; set; }

    /// The new code as the layout pass left it, from the start of its
    /// first line; not in sites.jsonl.
    [JsonIgnore]
    public string NewCode { get; set; }
}
