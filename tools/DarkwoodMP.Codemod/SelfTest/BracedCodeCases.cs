using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// Rewrites the braces stage's proof must refuse. The golden cases show that it
/// accepts what the stage really writes; these show that it isn't always saying
/// yes.
static class BracedCodeCases
{
    const string Start = "class C { void F(bool a) { ";
    const string End = " } void G() { } void H() { } }\n";

    static readonly (string Name, string Before, string After)[] Refused =
    {
        ("the next statement pulled into the new block",
            "if (a) G(); G();",
            "if (a) { G(); G(); }"),
        ("an else if turned into an else with a block",
            "if (a) G(); else if (!a) H();",
            "if (a) G(); else { if (!a) H(); }"),
        ("a call changed inside the new block",
            "if (a) G();",
            "if (a) { H(); }"),
        ("a statement dropped",
            "if (a) G(); H();",
            "if (a) { G(); }"),
        ("a block added around a statement that had no owner",
            "G();",
            "{ G(); }"),
        ("a comment moved past a statement",
            "if (a) G(); // why\n H();",
            "if (a) { G(); } H(); // why\n"),
        ("a comment dropped",
            "if (a) G(); // why\n",
            "if (a) { G(); }\n"),
        ("a comment reworded",
            "if (a) G(); // why\n",
            "if (a) { G(); } // how\n"),
    };

    public static int Count => Refused.Length;

    /// One line for each wrong rewrite the proof let through.
    public static List<string> Failures()
    {
        var problems = new List<string>();
        foreach (var (name, before, after) in Refused)
        {
            var a = CSharpSyntaxTree.ParseText(
                Start + before + End, SnippetCompiler.Parse);
            var b = CSharpSyntaxTree.ParseText(
                Start + after + End, SnippetCompiler.Parse);
            if (BracedCode.Difference(a.GetRoot(), b.GetRoot()) == null)
            {
                problems.Add("  the braces check accepted: " + name);
            }
        }
        return problems;
    }
}
