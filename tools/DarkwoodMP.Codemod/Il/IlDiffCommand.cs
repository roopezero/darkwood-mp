namespace DarkwoodMP.Codemod.Il;

/// "codemod ildiff a.dll b.dll [--manifest touched.json]": exit code 0 when
/// the second DLL differs from the first only inside the manifest.
static class IlDiffCommand
{
    const string Usage = "codemod ildiff <a.dll> <b.dll> [--manifest touched.json]";

    public static int Run(string[] args)
    {
        var manifest = new Manifest();
        var dlls = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--manifest" && i + 1 < args.Length)
            {
                i++;
                manifest = Manifest.Read(args[i]);
            }
            else
            {
                dlls.Add(args[i]);
            }
        }
        if (dlls.Count != 2)
        {
            throw new Stop(Usage);
        }
        foreach (var dll in dlls)
        {
            if (!File.Exists(dll))
            {
                throw new Stop("No such file: " + dll);
            }
        }
        var report = IlDiff.Compare(dlls[0], dlls[1], manifest);
        report.Print(Console.Out);
        if (report.Passed)
        {
            return 0;
        }
        return 1;
    }
}
