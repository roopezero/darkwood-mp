using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DarkwoodMP.Codemod.Rules;

/// Wire flags (guide section 4.6): "(byte)((a ? 1 : 0) | (b ? 2 : 0))"
/// becomes "int flags = 0; if (a) { flags |= 1; } if (b) { flags |= 2; }"
/// and "(byte)flags". The conditions run in the same order as before and
/// nothing else runs between them, so the value and every byte on the wire
/// stay the same; the packet tests and the Python peers confirm the bytes.
sealed partial class HoistPlanner
{
    /// Plans the flags the site is the first of, and true; false when the
    /// site isn't part of wire flags, and it is decided on its own.
    bool TryFlags(ConditionalExpressionSyntax site)
    {
        var top = FlagsTop(site);
        if (top == null || HostOf(site) != null)
        {
            return false;
        }
        var leaves = new List<ExpressionSyntax>();
        var bars = new List<BinaryExpressionSyntax>();
        FlagParts(top, leaves, bars);
        var bits = leaves.OfType<ConditionalExpressionSyntax>().ToList();
        if (bits.Count == 0 || bits[0] != site || !AreFlags(top, leaves, bars))
        {
            return false;
        }
        foreach (var bit in bits)
        {
            _taken.Add(bit);
        }
        var path = HoistPath.Walk(top, null, _model);
        var refused = path.Refusal;
        var hoist = new Hoist
        {
            Replaced = TernaryChain.Outer(top),
            Moved = top,
            Sites = bits.Cast<SyntaxNode>().ToList(),
            Statement = path.Statement,
        };
        if (refused == null)
        {
            refused = FlagsRefusal(hoist, bits, path);
        }
        if (refused != null)
        {
            Queue(bits, refused);
            return true;
        }
        hoist.Name = _plan.Temps(site).Unique("flags");
        hoist.Make = text => Flags(text, hoist, leaves);
        Accept(hoist);
        return true;
    }

    /// The outermost | above the site, through parentheses and other |, or
    /// null when the site isn't an operand of one.
    static ExpressionSyntax FlagsTop(ConditionalExpressionSyntax site)
    {
        ExpressionSyntax top = null;
        var node = TernaryChain.Outer(site);
        while (node.Parent is BinaryExpressionSyntax bar
            && bar.IsKind(SyntaxKind.BitwiseOrExpression))
        {
            top = bar;
            node = TernaryChain.Outer(bar);
        }
        return top;
    }

    /// The operands of the | chain in the order they run, and the |.
    static void FlagParts(
        ExpressionSyntax node,
        List<ExpressionSyntax> leaves,
        List<BinaryExpressionSyntax> bars)
    {
        var bare = TernaryChain.Strip(node);
        if (bare is BinaryExpressionSyntax bar
            && bar.IsKind(SyntaxKind.BitwiseOrExpression))
        {
            bars.Add(bar);
            FlagParts(bar.Left, leaves, bars);
            FlagParts(bar.Right, leaves, bars);
            return;
        }
        leaves.Add(bare);
    }

    /// Built-in | of whole numbers, whose ?: are "c ? k : 0" with constant
    /// numbers, and no other ?: anywhere in the chain.
    bool AreFlags(
        ExpressionSyntax top,
        List<ExpressionSyntax> leaves,
        List<BinaryExpressionSyntax> bars)
    {
        if (!IsWholeNumber(_model.GetTypeInfo(top).Type))
        {
            return false;
        }
        foreach (var bar in bars)
        {
            if (_model.GetOperation(bar) is not IBinaryOperation
                {
                    OperatorMethod: null,
                })
            {
                return false;
            }
        }
        foreach (var leaf in leaves)
        {
            if (leaf is not ConditionalExpressionSyntax bit)
            {
                if (leaf.DescendantNodes()
                    .OfType<ConditionalExpressionSyntax>()
                    .Any())
                {
                    return false;
                }
                continue;
            }
            if (!IsNumber(bit.WhenTrue) || !IsZero(bit.WhenFalse)
                || bit.Condition.DescendantNodesAndSelf()
                    .OfType<ConditionalExpressionSyntax>().Any())
            {
                return false;
            }
        }
        return true;
    }

    /// What the flags' statement and their ?: must allow.
    string FlagsRefusal(
        Hoist hoist,
        List<ConditionalExpressionSyntax> bits,
        HoistPath path)
    {
        foreach (var bit in bits)
        {
            var refused = new TernarySite(_plan, bit)
                .HoistRefusal(hoist.Statement.Span);
            if (refused != null)
            {
                return refused;
            }
        }
        var statementRefused = StatementRefusal(hoist, hoist.Moved);
        if (statementRefused != null)
        {
            return statementRefused;
        }
        var hazard = _hazards.WhyNotBefore(hoist.Moved, path, Moved(hoist));
        if (hazard != null)
        {
            return hazard;
        }
        var type = _model.GetTypeInfo(hoist.Moved).Type;
        hoist.Type = new TernarySite(_plan, bits[0])
            .SpellType(type, hoist.Statement.SpanStart, out var spelled);
        return spelled;
    }

    /// "int flags = 0;" (or the first operand, when it isn't a ?:), then an
    /// if per flag and a "flags |= x;" per other operand, in their order.
    static string Flags(
        EditText text, Hoist hoist, List<ExpressionSyntax> leaves)
    {
        var output = new StringBuilder();
        var name = hoist.Name;
        int start = 0;
        output.Append(hoist.Type).Append(' ').Append(name).Append(" = ");
        if (leaves[0] is ConditionalExpressionSyntax)
        {
            output.Append("0;\n");
        }
        else
        {
            output.Append(text.Of(leaves[0])).Append(";\n");
            start = 1;
        }
        for (int i = start; i < leaves.Count; i++)
        {
            if (leaves[i] is ConditionalExpressionSyntax bit)
            {
                output.Append("if (")
                    .Append(text.Of(TernaryChain.Strip(bit.Condition)))
                    .Append(")\n{\n")
                    .Append(name).Append(" |= ")
                    .Append(text.Of(TernaryChain.Strip(bit.WhenTrue)))
                    .Append(";\n}\n");
            }
            else
            {
                output.Append(name).Append(" |= ")
                    .Append(text.Of(leaves[i])).Append(";\n");
            }
        }
        return output.ToString();
    }

    bool IsNumber(ExpressionSyntax value)
    {
        var constant = _model.GetConstantValue(value);
        return constant.HasValue
            && IsWholeNumber(_model.GetTypeInfo(value).Type);
    }

    bool IsZero(ExpressionSyntax value)
    {
        var constant = _model.GetConstantValue(value);
        return constant.HasValue
            && IsWholeNumber(_model.GetTypeInfo(value).Type)
            && Convert.ToDecimal(constant.Value) == 0;
    }

    static bool IsWholeNumber(ITypeSymbol type)
    {
        if (type == null)
        {
            return false;
        }
        switch (type.SpecialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return true;
            default:
                return false;
        }
    }
}
