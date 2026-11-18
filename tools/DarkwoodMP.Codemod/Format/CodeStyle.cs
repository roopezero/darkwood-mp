using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;

namespace DarkwoodMP.Codemod.Format;

/// The Formatter settings that match the repo's .editorconfig, so what the
/// layout stage writes is what IDE0055 accepts.
static class CodeStyle
{
    public static readonly AdhocWorkspace Workspace = new AdhocWorkspace();

    public static readonly OptionSet Options = Workspace.Options
        .WithChangedOption(
            FormattingOptions.NewLine, LanguageNames.CSharp, "\n")
        .WithChangedOption(
            FormattingOptions.UseTabs, LanguageNames.CSharp, false)
        .WithChangedOption(
            FormattingOptions.IndentationSize, LanguageNames.CSharp, 4)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInTypes, true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInMethods, true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInProperties, true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInAccessors, true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInAnonymousMethods, true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInControlBlocks, true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInAnonymousTypes, true)
        .WithChangedOption(
            CSharpFormattingOptions
                .NewLinesForBracesInObjectCollectionArrayInitializers,
            true)
        .WithChangedOption(
            CSharpFormattingOptions.NewLinesForBracesInLambdaExpressionBody,
            true)
        .WithChangedOption(CSharpFormattingOptions.NewLineForElse, true)
        .WithChangedOption(CSharpFormattingOptions.NewLineForCatch, true)
        .WithChangedOption(CSharpFormattingOptions.NewLineForFinally, true)
        // Puts two statements on one line onto two lines.
        .WithChangedOption(
            CSharpFormattingOptions.WrappingKeepStatementsOnSingleLine, false)
        // true keeps { get; set; } on one line. One-line code blocks are
        // opened by a rewriter first, since this option can't tell them apart.
        .WithChangedOption(
            CSharpFormattingOptions.WrappingPreserveSingleLine, true);
}
