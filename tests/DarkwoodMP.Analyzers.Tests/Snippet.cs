using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DarkwoodMP.Analyzers.Tests
{
    /// Runs FormattingAnalyzer over a piece of code as the compiler does in
    /// a build, with every rule turned on, and says where a rule reported.
    static class Snippet
    {
        static readonly CSharpParseOptions Parse =
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.CSharp12);

        /// The lines, counted from 1, where rule id reported in code.
        public static int[] Lines(string id, string code)
        {
            return Diagnose(code)
                .Where(found => found.Id == id)
                .Select(found => LineOf(found.Location))
                .OrderBy(line => line)
                .ToArray();
        }

        /// The lines where rule id reported in statements, which go into a
        /// method of a class. The lines count from the first statement's.
        public static int[] LinesInMethod(string id, string statements)
        {
            var code = "class C\n{\n    void M()\n    {\n"
                + statements
                + "\n    }\n}\n";
            return Lines(id, code).Select(line => line - 4).ToArray();
        }

        /// The code parsed as a file. Code that doesn't parse fails the
        /// test: a rule would see what Roslyn made of the error, not the
        /// case the test names.
        public static SyntaxTree Tree(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code, Parse, "Snippet.cs");
            var errors = tree.GetDiagnostics()
                .Where(item => item.Severity == DiagnosticSeverity.Error);
            if (errors.Any())
            {
                throw new InvalidOperationException(
                    string.Join("\n", errors));
            }

            return tree;
        }

        public static int LineOf(Location place) =>
            place.GetLineSpan().StartLinePosition.Line + 1;

        /// Everything the analyzer reported. Anything that isn't one of its
        /// rules, such as AD0001 for an exception in it, fails the test.
        static ImmutableArray<Diagnostic> Diagnose(string code)
        {
            var analyzer = new FormattingAnalyzer();
            var rules = analyzer.SupportedDiagnostics.ToDictionary(
                rule => rule.Id,
                rule => ReportDiagnostic.Warn);
            var kind = OutputKind.DynamicallyLinkedLibrary;
            var options = new CSharpCompilationOptions(kind)
                .WithSpecificDiagnosticOptions(rules);
            var compilation = CSharpCompilation.Create(
                "Snippet",
                new[] { Tree(code) },
                Array.Empty<MetadataReference>(),
                options);
            var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
            var found = compilation
                .WithAnalyzers(analyzers)
                .GetAnalyzerDiagnosticsAsync()
                .Result;

            var others = found.Where(item => !rules.ContainsKey(item.Id));
            if (others.Any())
            {
                throw new InvalidOperationException(
                    string.Join("\n", others));
            }

            return found;
        }
    }
}
