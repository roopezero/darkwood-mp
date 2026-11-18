using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.SelfTest;

/// Compiles a golden case's source against this runtime's own assemblies, the
/// way the plugin is compiled: Release, deterministic and without debug
/// information. Whitespace stages must not change a single byte of the result.
static class SnippetCompiler
{
    public static readonly CSharpParseOptions Parse = new CSharpParseOptions(
        LanguageVersion.CSharp12, DocumentationMode.Parse);

    static readonly List<MetadataReference> References = ReadReferences();

    /// The DLL's bytes, or null and the errors in <paramref name="errors"/>.
    public static byte[] Emit(string source, out string errors)
    {
        var tree = CSharpSyntaxTree.ParseText(source, Parse, "golden.cs");
        var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Release)
            .WithAllowUnsafe(true)
            .WithDeterministic(true);
        var compilation = CSharpCompilation.Create(
            "golden", new[] { tree }, References, options);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var found = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            errors = string.Join("\n", found);
            return null;
        }
        errors = null;
        return stream.ToArray();
    }

    static List<MetadataReference> ReadReferences()
    {
        var list = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = new List<MetadataReference>();
        foreach (var file in list.Split(Path.PathSeparator))
        {
            references.Add(MetadataReference.CreateFromFile(file));
        }
        return references;
    }
}
