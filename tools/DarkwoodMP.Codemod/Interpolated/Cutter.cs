using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Interpolated;

/// Stage 11's rule inside the wrap stage, for one pass's tree with its
/// semantic model: an interpolated string that crosses the limit is cut in
/// two at the last space that fits (InterpolatedParts), the tail on a new
/// line one level in behind a leading "+". Later passes cut the tail again
/// while it is too long. A string it must not touch is left whole, with the
/// reason.
sealed class Cutter : IInterpolatedSplit
{
    public const string Whole = "interpolated string left whole: ";

    readonly SemanticModel _model;
    readonly Action<SyntaxNode, string> _cut;

    /// cut is told of every planned cut: the string and its member's key.
    public Cutter(SemanticModel model, Action<SyntaxNode, string> cut)
    {
        _model = model;
        _cut = cut;
    }

    public LinePlan Plan(WrapLine line, InterpolatedStringExpressionSyntax text)
    {
        var why = Eligibility.WhyWhole(text, _model);
        if (why != null)
        {
            return line.Leave(Whole + why);
        }
        bool parens = NeedsParentheses(text);
        int room = Wrap.Max - line.Col(text.SpanStart);
        if (parens)
        {
            room--;
        }
        var order = new HoleOrder(text, _model);
        var parts = InterpolatedParts.Cut(text, room, order.Safe);
        if (parts == null)
        {
            var any = InterpolatedParts.Cut(text, room, _ => true);
            if (any != null)
            {
                return line.Leave(Whole + "a hole in front of every cut "
                    + "that fits is made into text by code a later hole "
                    + "could see or change");
            }
            return line.Leave(Whole + "no space to split at in time");
        }
        var split = parts.Head + line.BreakForString(text.StringEndToken)
            + "+ " + parts.Tail;
        if (parens)
        {
            split = "(" + split + ")";
        }
        _cut(text, MemberKeys.Of(text, _model));
        return line.Break(
            "interpolated string split", new TextChange(text.Span, split));
    }

    /// A lambda's body takes in the whole "a" + "b", like the places the
    /// plain string rule lists.
    static bool NeedsParentheses(ExpressionSyntax text)
    {
        if (text.Parent is LambdaExpressionSyntax lambda
            && lambda.ExpressionBody == text)
        {
            return false;
        }
        return WrapStrings.NeedsParentheses(text);
    }
}
