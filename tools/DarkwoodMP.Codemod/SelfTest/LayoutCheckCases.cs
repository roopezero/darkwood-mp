using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// Rewrites the layout stage's gates must refuse, and the two moves of a
/// comment they must let through. The golden cases show the gates accept what
/// the stage really writes; these show they aren't always saying yes.
static class LayoutCheckCases
{
    const string Start = "class C { void F(bool a) { ";
    const string End = " } void G() { } void H() { } }\n";

    static readonly (string Name, string Before, string After)[] WrongWords =
    {
        ("a comment reworded", "G(); // why\n", "G(); // how\n"),
        ("a comment dropped", "G(); // why\nH();", "G();\nH();"),
        ("two comments swapped",
            "G(); // one\nH(); // two\n",
            "G(); // two\nH(); // one\n"),
    };

    static readonly (string Name, string Before, string After)[] WrongCode =
    {
        ("a changed token", "G();", "H();"),
        ("an added empty statement", "G();", "G();;"),
    };

    static readonly (string Name, string Before, string After)[] WrongPlaces =
    {
        ("a comment added", "G();", "G(); // new\n"),
        ("a comment moved past a closing brace",
            "try { G(); } catch { /* none */ } H();",
            "try { G(); } catch { } /* none */ H();"),
        ("a comment moved out of a block",
            "if (a) { G(); /* in */ } H();",
            "if (a) { G(); } /* in */ H();"),
        ("a comment pulled into a block it wasn't behind",
            "if (a) { G(); }\n// next\nH();",
            "if (a) { G(); // next\n}\nH();"),
        ("a comment pulled into a switch from behind its closing brace",
            "switch (a) { case true: break; } // why\n",
            "switch (a) { case true: break; // why\n}\n"),
        ("a comment moved past a statement",
            "G(); // one\nH();",
            "G();\nH(); // one\n"),
        ("a comment moved from behind a brace to the block's start",
            "if (a) { G(); } // why\n",
            "if (a) { // why\nG(); }\n"),
        ("a comment moved two statements back from a break",
            "switch (a) { case true: G(); H(); break; // why\n }",
            "switch (a) { case true: G(); // why\nH(); break; }"),
        ("a comment moved off a break in a section over several lines",
            "switch (a) { case true:\nG();\nbreak; // why\n }",
            "switch (a) { case true:\nG(); // why\nbreak; }"),
    };

    static readonly (string Name, string Before, string After)[] RightPlaces =
    {
        ("a comment from behind a brace to the last statement",
            "if (a) { G(); } // why\n",
            "if (a) { G(); // why\n}\n"),
        ("a comment from behind an empty block's brace into it",
            "try { G(); } catch { } // why\n",
            "try { G(); } catch { // why\n}\n"),
        ("a comment from a one-line section's break to the statement before",
            "switch (a) { case true: G(); break; // why\n }",
            "switch (a) { case true: G(); // why\nbreak; }"),
    };

    public static int Count =>
        WrongWords.Length + WrongCode.Length + WrongPlaces.Length
            + RightPlaces.Length;

    /// One line for each case the checks answered wrongly.
    public static List<string> Failures()
    {
        var problems = new List<string>();
        foreach (var (name, before, after) in WrongWords)
        {
            if (WordsSame(Root(before), Root(after)))
            {
                problems.Add("  the comment word check accepted: " + name);
            }
        }
        foreach (var (name, before, after) in WrongCode)
        {
            if (SameCode.Of(Root(before), Root(after)))
            {
                problems.Add("  the same-code check accepted: " + name);
            }
        }
        foreach (var (name, before, after) in WrongPlaces)
        {
            if (CommentPlaces.Difference(Root(before), Root(after)) == null)
            {
                problems.Add("  the comment place check accepted: " + name);
            }
        }
        foreach (var (name, before, after) in RightPlaces)
        {
            if (CommentPlaces.Difference(Root(before), Root(after)) != null)
            {
                problems.Add("  the comment place check refused: " + name);
            }
        }
        return problems;
    }

    static bool WordsSame(SyntaxNode before, SyntaxNode after)
    {
        var difference = CommentWords.FirstDifference(
            CommentWords.Of(before), CommentWords.Of(after));
        return difference == null;
    }

    static SyntaxNode Root(string code)
    {
        return CSharpSyntaxTree.ParseText(
            Start + code + End, SnippetCompiler.Parse).GetRoot();
    }
}
