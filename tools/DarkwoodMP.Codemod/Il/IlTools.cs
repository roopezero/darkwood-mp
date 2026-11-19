using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DarkwoodMP.Codemod.Il;

/// Reads a DLL with Mono.Cecil and renders method bodies in a form that survives metadata token changes.
static class IlTools
{
    public static string Render(MethodDefinition m)
    {
        return Render(m, GeneratedNames.Identity);
    }

    public static string Render(MethodDefinition m, GeneratedNames names)
    {
        if (!m.HasBody) { return "(no body)"; }
        var ins = m.Body.Instructions;
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < ins.Count; i++) { index[ins[i]] = i; }
        var sb = new System.Text.StringBuilder();
        foreach (var v in m.Body.Variables) { sb.Append("local ").Append(names.Of(v.VariableType)).Append('\n'); }
        foreach (var h in m.Body.ExceptionHandlers)
        {
            sb.Append($"handler {h.HandlerType} try {index[h.TryStart]}-{(h.TryEnd == null ? -1 : index[h.TryEnd])} handler {index[h.HandlerStart]}-{(h.HandlerEnd == null ? -1 : index[h.HandlerEnd])} filter {(h.FilterStart == null ? -1 : index[h.FilterStart])} {(h.CatchType == null ? "" : names.Of(h.CatchType))}\n");
        }
        for (int i = 0; i < ins.Count; i++)
        {
            var x = ins[i];
            sb.Append(i).Append(": ").Append(x.OpCode.Name);
            switch (x.Operand)
            {
                case Instruction t: sb.Append(" ->").Append(index[t]); break;
                case Instruction[] ts: sb.Append(" ->").Append(string.Join(",", ts.Select(t => index[t]))); break;
                case MethodReference r: sb.Append(' ').Append(names.Of(r)); break;
                case FieldReference r: sb.Append(' ').Append(names.Of(r)); break;
                case TypeReference r: sb.Append(' ').Append(names.Of(r)); break;
                case string s: sb.Append(" \"").Append(s).Append('"'); break;
                case VariableDefinition v: sb.Append(" v").Append(v.Index); break;
                case ParameterDefinition p: sb.Append(" p").Append(p.Index); break;
                case null: break;
                default: sb.Append(' ').Append(System.Convert.ToString(x.Operand, System.Globalization.CultureInfo.InvariantCulture)); break;
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition mod)
    {
        foreach (var t in mod.Types)
        {
            foreach (var n in Walk(t)) { yield return n; }
        }
    }

    static IEnumerable<TypeDefinition> Walk(TypeDefinition t)
    {
        yield return t;
        foreach (var n in t.NestedTypes)
        {
            foreach (var x in Walk(n)) { yield return x; }
        }
    }

    public static void Dump(string dll, string type, string method)
    {
        var res = new DefaultAssemblyResolver();
        res.AddSearchDirectory(Path.GetDirectoryName(dll));
        using var mod = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = res });
        foreach (var t in AllTypes(mod).Where(t => t.FullName == type))
        {
            foreach (var m in t.Methods.Where(m => m.Name == method))
            {
                Console.WriteLine("=== " + m.FullName);
                foreach (var a in m.CustomAttributes) { Console.WriteLine("  [" + a.AttributeType.Name + "]"); }
                Console.WriteLine(Render(m));
            }
        }
    }
}
