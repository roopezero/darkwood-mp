using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DarkwoodMP.Codemod.Rules;

/// The contexts of the pattern table in codemod.md section 7.3 where the
/// chain is the whole value: a statement, a declaration, an assignment, a
/// return and an expression body. Each returns true when it took the site,
/// rewritten or queued.
sealed partial class AccessPlanner
{
    /// "x?.F(a);" becomes "if (x != null) { x.F(a); }".
    bool Statement(AccessChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not ExpressionStatementSyntax statement)
        {
            return false;
        }
        if (!Allowed(chain, statement))
        {
            return true;
        }
        var names = Names(chain, statement.SpanStart);
        if (names != null)
        {
            Direct(chain, statement, names, text => text.ReceiverLine()
                + text.Ifs(value => text.Line(value + ";")));
        }
        return true;
    }

    /// "T n = x?.F;" becomes "T n = null; if (x != null) { n = x.F; }",
    /// when the value goes into n by an identity or a reference conversion.
    /// "var" is spelled out with the natural type, a Nullable one for a
    /// value type.
    bool Declaration(AccessChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Parent: LocalDeclarationStatementSyntax statement,
                    } declaration,
                } declarator,
            }
            || statement.Modifiers.Count > 0
            || statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
            || declaration.Variables.Count > 1
            || declaration.Type is RefTypeSyntax)
        {
            return false;
        }
        var natural = _types.TypeOf(chain.Top);
        if (!declaration.Type.IsVar
            && !_types.Keeps(natural, _types.TypeOf(declaration.Type)))
        {
            return false;
        }
        if (!Allowed(chain, statement))
        {
            return true;
        }
        var names = ReceiverNames(chain, statement.SpanStart);
        if (names == null)
        {
            return true;
        }
        string type = null;
        if (declaration.Type.IsVar)
        {
            type = Spell(chain, natural, statement.SpanStart);
            if (type == null)
            {
                return true;
            }
        }
        if (!StepNames(chain, names, statement.SpanStart))
        {
            return true;
        }
        var name = declarator.Identifier.Text;
        Direct(chain, statement, names, text =>
        {
            var written = type;
            if (written == null)
            {
                written = text.Of(declaration.Type);
            }
            return text.ReceiverLine()
                + written + " " + name + " = null;\n"
                + text.Ifs(value => text.Line(name + " = " + value + ";"));
        });
        return true;
    }

    /// "t = x?.F;" becomes "if (x != null) { t = x.F; } else { t = null;
    /// }", so t is written once, with the final value, as before. Only for
    /// one ?., a target without an element or a constant (it is written
    /// twice), whose parts may run after the receiver (hazard 2), and a
    /// value that goes into it by an identity or a reference conversion.
    bool Assignment(AccessChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not AssignmentExpressionSyntax assignment
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || assignment.Right != outer
            || assignment.Parent is not ExpressionStatementSyntax statement
            || chain.IsChain
            || assignment.Left.DescendantNodesAndSelf().Any(
                node => node is ElementAccessExpressionSyntax
                    || node is LiteralExpressionSyntax
                    || node is InvocationExpressionSyntax)
            || !_types.Keeps(
                _types.TypeOf(chain.Top), _types.TypeOf(assignment.Left))
            || _plan.Purity.WhyNotBefore(
                chain.Receiver, Purity.TargetParts(assignment)) != null)
        {
            return false;
        }
        if (!Allowed(chain, statement))
        {
            return true;
        }
        var names = Names(chain, statement.SpanStart);
        if (names != null)
        {
            Direct(chain, statement, names, text =>
            {
                var target = text.Of(assignment.Left);
                return text.ReceiverLine()
                    + text.Ifs(value => text.Line(target + " = " + value + ";"))
                    + "else\n{\n" + target + " = null;\n}\n";
            });
        }
        return true;
    }

    /// "return x?.F;" becomes "if (x != null) { return x.F; } return
    /// null;", when the value is returned by an identity or a reference
    /// conversion.
    bool Return(AccessChain chain, ExpressionSyntax outer)
    {
        if (outer.Parent is not ReturnStatementSyntax statement
            || !_types.Keeps(
                _types.TypeOf(chain.Top),
                _model.GetTypeInfo(outer).ConvertedType))
        {
            return false;
        }
        if (!Allowed(chain, statement))
        {
            return true;
        }
        var names = Names(chain, statement.SpanStart);
        if (names != null)
        {
            Direct(chain, statement, names, text => Returns(text));
        }
        return true;
    }

    static string Returns(AccessText text)
    {
        return text.ReceiverLine()
            + text.Ifs(value => text.Line("return " + value + ";"))
            + "return null;\n";
    }

    /// Statements may replace the statement or go in front of it.
    bool Allowed(AccessChain chain, StatementSyntax statement)
    {
        var refused = WhyNotStatements(chain, statement);
        if (refused != null)
        {
            Queue(chain, refused);
            return false;
        }
        return true;
    }

    /// Anywhere else the chain moves into a temporary of its natural type
    /// in front of its statement: "T t = null; if (x != null) { t = x.F; }",
    /// and t takes its place.
    void Hoist(AccessChain chain)
    {
        var site = chain.Top;
        var natural = _types.TypeOf(site);
        if (natural.SpecialType == SpecialType.System_Void)
        {
            Queue(chain, "a ?. of no value outside a statement");
            return;
        }
        var refused = WhyNotHoist(site, out var place);
        if (refused != null)
        {
            Queue(chain, refused);
            return;
        }
        int position = place.SpanStart;
        var names = ReceiverNames(chain, position);
        if (names == null)
        {
            return;
        }
        var type = Spell(chain, natural, position);
        if (type == null)
        {
            return;
        }
        var replaced = HoistPlanner.Replaced(site, null);
        var name = Temps(chain).Pick(Hints(replaced, site), Related(replaced));
        if (!StepNames(chain, names, position))
        {
            return;
        }
        var hoist = new AccessHoist
        {
            Place = place,
            Moved = site,
            Sites = chain.Sites(),
        };
        _hoists.Add(hoist);
        int column = PlaceColumn(place);
        _edits.Add(new PlannedEdit
        {
            Span = replaced.Span,
            Sites = chain.Sites(),
            Build = text =>
            {
                var access = new AccessText(text, chain, names, _types, column);
                hoist.Text = access.ReceiverLine()
                    + type + " " + name + " = null;\n"
                    + access.Ifs(
                        value => access.Line(name + " = " + value + ";"));
                return name;
            },
        });
    }

    /// Why the expression can't move in front of its statement, or into
    /// the block its expression body becomes, or null, after which place
    /// is where it goes: the statement or the body.
    string WhyNotHoist(ExpressionSyntax moved, out SyntaxNode place)
    {
        var body = BodyOf(moved);
        var path = HoistPath.Walk(moved, body, _model);
        place = path.Statement;
        if (body != null)
        {
            place = body;
        }
        if (path.Refusal != null)
        {
            return path.Refusal;
        }
        if (place.Ancestors().Any(node => node is InterpolationSyntax))
        {
            return "the statement is inside an interpolation hole, where no "
                + "statement can go";
        }
        if (place.DescendantNodesAndSelf(
                node => node is not AnonymousFunctionExpressionSyntax
                    && node is not LocalFunctionStatementSyntax)
            .OfType<AwaitExpressionSyntax>().Any())
        {
            return Awaits;
        }
        if (body != null)
        {
            var refused = WhyNotBlock(body);
            if (refused != null)
            {
                return refused;
            }
        }
        return _hazards.WhyNotBefore(moved, path, Moved(place));
    }

    /// What the hoists already planned for the place move: they run there
    /// in the same order as before.
    HashSet<SyntaxNode> Moved(SyntaxNode place)
    {
        var moved = new HashSet<SyntaxNode>();
        foreach (var hoist in _hoists)
        {
            if (hoist.Place == place)
            {
                moved.Add(hoist.Moved);
            }
        }
        return moved;
    }

    /// The names for the temporary of the chain's value, best first: the
    /// parameter it is passed to, when that is a word, the target an
    /// assignment stores it to, what the last step reads, and "text" in an
    /// interpolation hole.
    List<string> Hints(ExpressionSyntax replaced, ExpressionSyntax site)
    {
        var hints = new List<string>();
        if (replaced.Parent is ArgumentSyntax)
        {
            var parameter = TempNames.Hint(replaced, _model);
            if (TempNames.IsWord(parameter))
            {
                hints.Add(parameter);
            }
        }
        if (StoredTo(replaced) is ExpressionSyntax target)
        {
            hints.Add(TempNames.Hint(target, _model));
        }
        hints.Add(TempNames.Named(site, _model));
        if (replaced.Parent is InterpolationSyntax)
        {
            hints.Add("text");
        }
        return hints;
    }

    /// The names the temporary may take with a number: the target an
    /// assignment stores the value to, and what the last step reads.
    List<string> Related(ExpressionSyntax replaced)
    {
        var related = new List<string>();
        if (StoredTo(replaced) is ExpressionSyntax target)
        {
            related.Add(TempNames.Hint(target, _model));
        }
        related.Add(TempNames.Named(replaced, _model));
        return related;
    }

    /// The target an assignment stores the value to, when it names a
    /// variable or a member: an element's collection would make the
    /// temporary read as a copy of the collection.
    static ExpressionSyntax StoredTo(ExpressionSyntax replaced)
    {
        if (replaced.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == replaced
            && assignment.Left is not ElementAccessExpressionSyntax)
        {
            return assignment.Left;
        }
        return null;
    }
}
