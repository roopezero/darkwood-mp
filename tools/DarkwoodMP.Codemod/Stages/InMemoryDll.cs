using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DarkwoodMP.Codemod.Stages;

/// Emits a compilation to bytes, the way verify-identical.sh builds the real
/// DLL: Release, deterministic and without debug information. Whitespace
/// stages must leave these bytes exactly as they were.
static class InMemoryDll
{
    public static byte[] Bytes(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(20)
                .Select(d => "  " + d);
            throw new Stop(
                "The rewritten sources don't compile:\n"
                    + string.Join("\n", errors));
        }
        return stream.ToArray();
    }

    public static string Hash(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).Substring(0, 16);
    }
}
