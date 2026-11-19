using System.Globalization;
using System.Text;
using Mono.Cecil;

namespace DarkwoodMP.Codemod.Il;

/// Renders everything about a member except its body as text: attributes and
/// their arguments, parameter names, default values, constants. Harmony binds
/// patch parameters by name, so none of this may change in a rewrite.
static class IlFacts
{
    public static string Of(MethodDefinition m, GeneratedNames names)
    {
        var text = new StringBuilder();
        text.Append("attributes ").Append(m.Attributes);
        text.Append(" impl ").Append(m.ImplAttributes);
        text.Append(" semantics ").Append(m.SemanticsAttributes).Append('\n');
        text.Append("returns ").Append(names.Of(m.ReturnType)).Append('\n');
        Attributes(text, "return", m.MethodReturnType, names);
        Attributes(text, "method", m, names);
        foreach (var p in m.Parameters)
        {
            text.Append("parameter ").Append(p.Index).Append(' ').Append(p.Name);
            text.Append(' ').Append(p.Attributes);
            text.Append(' ').Append(names.Of(p.ParameterType));
            if (p.HasConstant)
            {
                text.Append(" = ").Append(Constant(p.Constant));
            }
            text.Append('\n');
            Attributes(text, "parameter " + p.Index, p, names);
        }
        Generics(text, m, names);
        foreach (var o in m.Overrides)
        {
            text.Append("overrides ").Append(names.Of(o)).Append('\n');
        }
        return text.ToString();
    }

    public static string Of(FieldDefinition f, GeneratedNames names)
    {
        var text = new StringBuilder();
        text.Append("attributes ").Append(f.Attributes);
        text.Append(" type ").Append(names.Of(f.FieldType));
        if (f.HasConstant)
        {
            text.Append(" = ").Append(Constant(f.Constant));
        }
        if (f.HasLayoutInfo)
        {
            text.Append(" offset ").Append(f.Offset);
        }
        if (f.InitialValue != null && f.InitialValue.Length > 0)
        {
            text.Append(" data ").Append(Convert.ToHexString(f.InitialValue));
        }
        text.Append('\n');
        Attributes(text, "field", f, names);
        return text.ToString();
    }

    public static string Of(PropertyDefinition p, GeneratedNames names)
    {
        var text = new StringBuilder();
        text.Append("attributes ").Append(p.Attributes);
        text.Append(" type ").Append(names.Of(p.PropertyType));
        if (p.HasConstant)
        {
            text.Append(" = ").Append(Constant(p.Constant));
        }
        text.Append(" get ").Append(Name(p.GetMethod, names));
        text.Append(" set ").Append(Name(p.SetMethod, names)).Append('\n');
        Attributes(text, "property", p, names);
        return text.ToString();
    }

    public static string Of(EventDefinition e, GeneratedNames names)
    {
        var text = new StringBuilder();
        text.Append("attributes ").Append(e.Attributes);
        text.Append(" type ").Append(names.Of(e.EventType));
        text.Append(" add ").Append(Name(e.AddMethod, names));
        text.Append(" remove ").Append(Name(e.RemoveMethod, names));
        text.Append(" raise ").Append(Name(e.InvokeMethod, names)).Append('\n');
        Attributes(text, "event", e, names);
        return text.ToString();
    }

    public static string Of(TypeDefinition t, GeneratedNames names)
    {
        var text = new StringBuilder();
        text.Append("attributes ").Append(t.Attributes);
        text.Append(" base ");
        if (t.BaseType != null)
        {
            text.Append(names.Of(t.BaseType));
        }
        text.Append(" pack ").Append(t.PackingSize);
        text.Append(" size ").Append(t.ClassSize).Append('\n');
        var interfaces = t.Interfaces.Select(i => names.Of(i.InterfaceType));
        foreach (var name in interfaces.OrderBy(n => n, StringComparer.Ordinal))
        {
            text.Append("implements ").Append(name).Append('\n');
        }
        Attributes(text, "type", t, names);
        Generics(text, t, names);
        return text.ToString();
    }

    /// The assembly's own attributes, references and resources.
    public static string Of(ModuleDefinition module, GeneratedNames names)
    {
        var text = new StringBuilder();
        if (module.Assembly != null)
        {
            text.Append("assembly ").Append(module.Assembly.Name.FullName);
            text.Append('\n');
            Attributes(text, "assembly", module.Assembly, names);
        }
        Attributes(text, "module", module, names);
        var references = module.AssemblyReferences.Select(r => r.FullName);
        foreach (var name in references.OrderBy(n => n, StringComparer.Ordinal))
        {
            text.Append("references ").Append(name).Append('\n');
        }
        foreach (var resource in module.Resources)
        {
            text.Append("resource ").Append(resource.Name);
            text.Append(' ').Append(resource.Attributes).Append('\n');
        }
        return text.ToString();
    }

    static string Name(MethodReference method, GeneratedNames names)
    {
        if (method == null)
        {
            return "-";
        }
        return names.Of(method);
    }

    /// Generic parameters of a type or method, with their constraints.
    static void Generics(
        StringBuilder text, IGenericParameterProvider owner, GeneratedNames names)
    {
        foreach (var g in owner.GenericParameters)
        {
            text.Append("generic ").Append(g.Name);
            text.Append(' ').Append(g.Attributes);
            foreach (var c in g.Constraints)
            {
                text.Append(' ').Append(names.Of(c.ConstraintType));
            }
            text.Append('\n');
        }
    }

    /// One line per attribute: its constructor and its raw argument bytes, so
    /// any change to a constructor argument, a named argument or a typeof
    /// shows.
    static void Attributes(
        StringBuilder text,
        string where,
        ICustomAttributeProvider provider,
        GeneratedNames names)
    {
        if (!provider.HasCustomAttributes)
        {
            return;
        }
        var lines = provider.CustomAttributes
            .Select(a => names.Of(a.Constructor) + " " + Arguments(a, names))
            .OrderBy(line => line, StringComparer.Ordinal);
        foreach (var line in lines)
        {
            text.Append(where).Append(" attribute ").Append(line).Append('\n');
        }
    }

    /// The argument bytes of an attribute. [IteratorStateMachine(typeof(X))]
    /// and its async siblings name a generated type, whose name moves with the
    /// member ordinals, so they are compared by that type's canonical name.
    static string Arguments(CustomAttribute attribute, GeneratedNames names)
    {
        var type = attribute.AttributeType.Name;
        if (type == "IteratorStateMachineAttribute"
            || type == "AsyncStateMachineAttribute"
            || type == "AsyncIteratorStateMachineAttribute")
        {
            return names.Of((TypeReference)attribute.ConstructorArguments[0].Value);
        }
        return Convert.ToHexString(attribute.GetBlob());
    }

    static string Constant(object value)
    {
        if (value == null)
        {
            return "null";
        }
        return value.GetType().Name + " "
            + Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
