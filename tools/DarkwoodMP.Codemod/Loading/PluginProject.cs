using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Loading;

/// The plugin's sources, compiled the way the real build compiles them.
sealed class PluginProject
{
    /// The folder the source trees were read from.
    public readonly string SourceDir;

    /// Every tree the compiler sees, ModVersion.g.cs included.
    public readonly CSharpCompilation Compilation;

    /// The files on disk, the only trees a stage may rewrite or count.
    public readonly IReadOnlyList<SyntaxTree> Sources;

    /// The compiler's warnings when the project was loaded.
    public readonly int Warnings;

    public PluginProject(
        string sourceDir,
        CSharpCompilation compilation,
        IReadOnlyList<SyntaxTree> sources,
        int warnings)
    {
        SourceDir = sourceDir;
        Compilation = compilation;
        Sources = sources;
        Warnings = warnings;
    }

    public CSharpParseOptions ParseOptions =>
        (CSharpParseOptions)Sources[0].Options;
}
