using System.Text;

namespace DarkwoodMP.Codemod.Rules;

/// The readable list of the sites a rule rewrote (Rule.SiteList): for each
/// rewrite the file and line of every site it removed, the member, the old
/// code and the new code. Lines are those of the tree the rule read, before
/// its rewrite, as in sites.jsonl.
static class SiteListText
{
    public static string Of(Rule rule, RuleResult result)
    {
        var text = new StringBuilder();
        text.Append("# The sites the rule ").Append(rule.Name)
            .Append(" rewrote: ").Append(Rewrites(result).Count)
            .Append(" rewrites of ").Append(result.Sites.Count)
            .Append(" sites.\n");
        foreach (var line in rule.SiteListNote.Split('\n'))
        {
            if (line.Length > 0)
            {
                text.Append("# ").Append(line).Append('\n');
            }
        }
        foreach (var group in Rewrites(result))
        {
            var first = group[0];
            var lines = group.Select(site => site.Line).Distinct();
            text.Append('\n').Append(first.File).Append(':')
                .Append(string.Join(", ", lines)).Append('\n');
            text.Append("member: ").Append(first.Member).Append('\n');
            text.Append("old:\n").Append(Indented(first.OldCode));
            text.Append("new:\n").Append(Indented(first.NewCode));
        }
        return text.ToString();
    }

    /// The site entries of each rewrite, in order: the entries of one
    /// proposal follow each other and share its texts.
    static List<List<SiteEntry>> Rewrites(RuleResult result)
    {
        var groups = new List<List<SiteEntry>>();
        foreach (var site in result.Sites)
        {
            var last = groups.LastOrDefault();
            if (last != null && Same(last[0], site))
            {
                last.Add(site);
            }
            else
            {
                groups.Add(new List<SiteEntry> { site });
            }
        }
        return groups;
    }

    static bool Same(SiteEntry a, SiteEntry b)
    {
        return a.File == b.File && a.Member == b.Member
            && a.Before == b.Before && a.After == b.After;
    }

    /// The code with its common indentation taken off, four spaces in.
    static string Indented(string code)
    {
        if (code == null)
        {
            return "";
        }
        var lines = code.Replace("\r\n", "\n").Split('\n');
        int common = lines
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart(' ').Length)
            .DefaultIfEmpty(0)
            .Min();
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                text.Append('\n');
                continue;
            }
            text.Append("    ").Append(line.Substring(common).TrimEnd())
                .Append('\n');
        }
        return text.ToString();
    }
}
