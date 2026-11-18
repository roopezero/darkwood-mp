using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DarkwoodMP.Codemod.Il;

/// Does the built DLL call anything that takes caller information (line, file, member, argument text)?
static class Callers
{
    public static void Run(string dll, string[] searchDirs)
    {
        var res = new DefaultAssemblyResolver();
        foreach (var d in searchDirs) { res.AddSearchDirectory(d); }
        using var mod = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = res });
        var hits = new Dictionary<string, int>();
        int calls = 0, unresolved = 0;
        foreach (var t in IlTools.AllTypes(mod))
        {
            foreach (var m in t.Methods.Where(m => m.HasBody))
            {
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.Operand is not MethodReference r) { continue; }
                    calls++;
                    MethodDefinition def;
                    try { def = r.Resolve(); } catch { def = null; }
                    if (def == null) { unresolved++; continue; }
                    foreach (var p in def.Parameters)
                    {
                        foreach (var a in p.CustomAttributes)
                        {
                            if (a.AttributeType.Name.StartsWith("Caller"))
                            {
                                var k = a.AttributeType.Name + " on " + def.FullName;
                                hits.TryGetValue(k, out var c); hits[k] = c + 1;
                            }
                        }
                    }
                }
            }
        }
        Console.WriteLine($"{calls} member references inspected, {unresolved} unresolved, {hits.Count} distinct caller-info targets");
        foreach (var kv in hits.OrderByDescending(k => k.Value)) { Console.WriteLine($"  {kv.Value,5}  {kv.Key}"); }
    }
}
