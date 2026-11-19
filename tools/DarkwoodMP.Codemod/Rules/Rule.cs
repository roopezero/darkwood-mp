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
}
