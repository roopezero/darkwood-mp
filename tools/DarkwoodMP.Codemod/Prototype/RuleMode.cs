using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Loading;

namespace DarkwoodMP.Codemod.Prototype;

/// Applies the one IL-changing rule to a tree and proves how far the change reaches.
static class RuleMode
{
    public static void Run(string srcRoot, string outDir)
    {
        var game = RepoPaths.GameDir;
        var comp = PluginLoader.Load(RepoPaths.Find(), srcRoot).Compilation;
        Console.WriteLine($"input: {comp.SyntaxTrees.Count()} trees, {comp.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error)} errors");
        Directory.CreateDirectory(outDir);
        Emit(comp, outDir + "/before.dll");

        var ws = new AdhocWorkspace();
        var opt = ws.Options
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, "\n")
            .WithChangedOption(CSharpFormattingOptions.WrappingKeepStatementsOnSingleLine, false);

        int applied = 0;
        var skipped = new Dictionary<string, int>();
        var touched = new HashSet<string>();
        var newComp = comp;
        foreach (var tree in comp.SyntaxTrees)
        {
            var model = comp.GetSemanticModel(tree);
            var banned = new HashSet<int>();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var rule = new TernaryLocalRule();
                rule.Plan(tree.GetRoot(), model, banned);
                if (rule.Sites.Count == 0)
                {
                    break;
                }
                var root = rule.Visit(tree.GetRoot());
                var text = Formatter.Format(root, Formatter.Annotation, ws, opt).ToFullString();
                var nt = CSharpSyntaxTree.ParseText(SourceText.From(text, System.Text.Encoding.UTF8), (CSharpParseOptions)tree.Options, tree.FilePath);
                var trial = newComp.ReplaceSyntaxTree(newComp.SyntaxTrees.First(t => t.FilePath == tree.FilePath), nt);
                var errs = trial.GetSemanticModel(nt).GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (errs.Count == 0)
                {
                    newComp = trial;
                    applied += rule.Sites.Count;
                    foreach (var s in rule.Sites.Keys) { touched.Add(TernaryLocalRule.MemberOf(s, model)); }
                    foreach (var why in rule.Skipped) { skipped.TryGetValue(why, out var c); skipped[why] = c + 1; }
                    break;
                }
                // A site whose rewrite doesn't compile is left alone: find it through the line of the first error.
                foreach (var e in errs)
                {
                    int line = e.Location.GetLineSpan().StartLinePosition.Line;
                    var ntText = nt.GetText();
                    var token = nt.GetRoot().FindToken(ntText.Lines[line].Start);
                    var stmt = token.Parent?.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault(s => s.GetAnnotations("site").Any());
                    if (stmt != null) { banned.Add(int.Parse(stmt.GetAnnotations("site").First().Data)); }
                    else { Console.WriteLine("  error outside a rewrite: " + e); }
                }
                Console.WriteLine($"  {Path.GetFileName(tree.FilePath)}: retry, {errs.Count} errors, first: {errs[0].GetMessage()}");
            }
        }
        var finalErrors = newComp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Console.WriteLine($"rule applied at {applied} sites; {finalErrors.Count} errors");
        foreach (var e in finalErrors.Take(10)) { Console.WriteLine("  " + e); }
        foreach (var kv in skipped) { Console.WriteLine($"  left alone: {kv.Value,4} {kv.Key}"); }
        Emit(newComp, outDir + "/after.dll");
        foreach (var t in newComp.SyntaxTrees)
        {
            var dest = Path.Combine(outDir, "src", Path.GetRelativePath(srcRoot, t.FilePath));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.WriteAllText(dest, t.GetText().ToString());
        }

        var diff = IlDiff.Compare(outDir + "/before.dll", outDir + "/after.dll", game + "/Darkwood_Data/Managed");
        Console.WriteLine($"IL: {diff.Same} methods identical, {diff.Changed.Count} changed, {diff.OnlyA.Count} gone, {diff.OnlyB.Count} new, {diff.Meta.Count} metadata differences");
        foreach (var m in diff.Meta.Take(15)) { Console.WriteLine("   meta " + m); }
        var owners = diff.Changed.Concat(diff.OnlyA).Concat(diff.OnlyB).Select(IlDiff.Owner).ToHashSet();
        var outside = owners.Where(o => !touched.Contains(o)).OrderBy(o => o).ToList();
        Console.WriteLine($"owners of changed methods: {owners.Count}; rewritten members: {touched.Count}; changed outside the rewritten members: {outside.Count}");
        foreach (var o in outside.Take(25)) { Console.WriteLine("   OUTSIDE " + o); }
    }

    static void Emit(CSharpCompilation c, string dll)
    {
        using var pe = new MemoryStream();
        var r = c.Emit(pe);
        if (!r.Success)
        {
            foreach (var d in r.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(10)) { Console.WriteLine("  " + d); }
            throw new Exception("emit failed");
        }
        File.WriteAllBytes(dll, pe.ToArray());
        Console.WriteLine($"{Path.GetFileName(dll)}: {Convert.ToHexString(SHA256.HashData(pe.ToArray()))[..16]}");
    }
}
