using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DarkwoodMP.Codemod.Rules;

/// The names and types a rewrite writes, chosen when the site is planned.
sealed class CoalesceNames
{
    /// The type Name is declared with, or null when Name is the first value
    /// itself, as x in "x = x ?? b".
    public string Type;

    public string Name;

    /// Name is a variable that exists already, the target of "x = a ?? b",
    /// which takes the first value with "x = a;" instead of a declaration.
    public bool Assigned;

    /// The temporary the receiver of an opened ?. goes into, or null when
    /// the receiver is a local, a parameter or this.
    public string ReceiverType;
    public string Receiver;

    /// The temporary the first value goes into before HasValue and Value
    /// read it, or null when it is a local or a parameter.
    public string HeldType;
    public string Held;
}

/// The statements a ?? chain or a ??= becomes (codemod.md section 7.2).
/// The right side of ?? runs only when everything before it was null, so
/// it always stays inside the if that tests for that. Values come from the
/// EditText, so edits inside them are kept. A statement whose value is
/// written over several lines starts where the original statement did, so
/// the layout pass moves the lines that continue it as far as it moves the
/// first.
sealed class CoalesceText
{
    readonly EditText _text;
    readonly string _margin;

    public CoalesceText(EditText text, int column)
    {
        _text = text;
        _margin = new string(' ', column);
    }

    /// The statements that give names.Name the value of the chain.
    public string Chain(CoalesceChain chain, CoalesceNames names)
    {
        var output = new StringBuilder();
        string test = null;
        string value = null;
        if (chain.Access != null)
        {
            var receiver = _text.Of(chain.Access.Expression);
            if (names.Receiver != null)
            {
                output.Append(Line(
                    names.ReceiverType + " " + names.Receiver + " = "
                        + receiver + ";"));
                receiver = names.Receiver;
            }
            test = receiver + " != null";
            value = receiver + _text.Of(chain.Access.WhenNotNull);
        }
        else if (chain.Form == CoalesceForm.Unwrap)
        {
            var held = _text.Of(chain.First);
            if (names.Held != null)
            {
                output.Append(Line(
                    names.HeldType + " " + names.Held + " = " + held + ";"));
                held = names.Held;
            }
            test = held + ".HasValue";
            value = held + ".Value";
        }
        if (chain.Form == CoalesceForm.Unwrap)
        {
            output.Append(Unwrap(chain, names, test, value));
            return output.ToString();
        }
        if (chain.Access != null)
        {
            output.Append(Target(names) + " = null;\n");
            output.Append(If(test, Line(names.Name + " = " + value + ";")));
        }
        else if (names.Type != null)
        {
            output.Append(Line(
                names.Type + " " + names.Name + " = " + _text.Of(chain.First)
                    + ";"));
        }
        else if (names.Assigned)
        {
            output.Append(Line(
                names.Name + " = " + _text.Of(chain.First) + ";"));
        }
        var empty = Empty(names.Name, CoalesceTypes.IsNullable(chain.Natural));
        for (int i = 1; i < chain.Values.Count; i++)
        {
            output.Append(If(empty, Store(names.Name, chain.Values[i])));
        }
        return output.ToString();
    }

    /// "U t; if (a.HasValue) { t = a.Value; } else { t = b; }", or "U t =
    /// k; if (a.HasValue) { t = a.Value; }" when the last value is a literal,
    /// which has no effects to run too early.
    string Unwrap(
        CoalesceChain chain, CoalesceNames names, string test, string value)
    {
        var store = Line(names.Name + " = " + value + ";");
        if (TernaryChain.IsLiteral(chain.Last))
        {
            return Line(Target(names) + " = " + _text.Of(chain.Last) + ";")
                + If(test, store);
        }
        var declared = "";
        if (names.Type != null)
        {
            declared = names.Type + " " + names.Name + ";\n";
        }
        return declared + If(test, store) + "else\n{\n"
            + Store(names.Name, chain.Last) + "}\n";
    }

    /// "T t" for a new temporary, or "x" for a variable that exists.
    static string Target(CoalesceNames names)
    {
        if (names.Type == null)
        {
            return names.Name;
        }
        return names.Type + " " + names.Name;
    }

    /// "if (x == null) { x = v; }" for "x ??= v", or with a temporary,
    /// "T t = x; if (t == null) { t = v; x = t; }", whose t then stands for
    /// the value of the ??=.
    public string Assign(
        AssignmentExpressionSyntax site, CoalesceNames names, bool nullable)
    {
        var target = _text.Of(site.Left);
        if (names.Name == null)
        {
            return If(Empty(target, nullable), Store(target, site.Right));
        }
        return Line(names.Type + " " + names.Name + " = " + target + ";")
            + If(
                Empty(names.Name, nullable),
                Store(names.Name, site.Right)
                    + Line(target + " = " + names.Name + ";"));
    }

    /// "name = value;", or the throw statement a throw value becomes.
    string Store(string name, ExpressionSyntax value)
    {
        if (value is ThrowExpressionSyntax thrown)
        {
            return Line("throw " + _text.Of(thrown.Expression) + ";");
        }
        return Line(name + " = " + _text.Of(value) + ";");
    }

    static string Empty(string name, bool nullable)
    {
        if (nullable)
        {
            return "!" + name + ".HasValue";
        }
        return name + " == null";
    }

    static string If(string test, string body)
    {
        return "if (" + test + ")\n{\n" + body + "}\n";
    }

    string Line(string line)
    {
        if (line.Contains('\n'))
        {
            return _margin + line + "\n";
        }
        return line + "\n";
    }
}
