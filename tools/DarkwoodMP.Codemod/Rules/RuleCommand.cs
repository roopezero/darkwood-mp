using System.Text.Json;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Loading;
using DarkwoodMP.Codemod.Stages;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;

namespace DarkwoodMP.Codemod.Rules;

/// "codemod rule NAME [--receivers unity|other] --out DIR [--src DIR]
/// [--dry-run]": runs one rule over the plugin's sources (or another copy of
/// them), writes manifest.json, queue.jsonl, sites.jsonl and census.txt to DIR,
/// and rewrites the sources unless --dry-run is given.
static class RuleCommand
{
    const string Usage =
        "codemod rule <name> [--receivers unity|other] --out DIR [--src DIR] "
        + "[--dry-run]";

    public static int Run(string[] args)
    {
        var options = Options.Parse(args);
        var rule = RuleRegistry.Find(options.Name);
        if (rule == null)
        {
            throw new Stop(
                $"No rule named {options.Name}. Rules: {RuleRegistry.Names}");
        }
        var paths = RepoPaths.Find();
        var project = PluginLoader.Load(paths, options.Source);
        var result = RuleEngine.Run(
            rule,
            project.Compilation,
            project.Sources,
            options.Receivers,
            project.SourceDir);
        CheckMembers(project.Compilation, result.Manifest);
        Directory.CreateDirectory(options.Out);
        WriteOutputs(result, options.Out);
        result.PrintCensus(Console.Out);
        Console.WriteLine(
            $"{rule.Name}: {result.Sites.Count} sites rewritten in "
                + $"{result.Rewritten.Count} files, {result.Queue.Count} "
                + $"queued, {result.Manifest.Members.Count} members in the "
                + "manifest.");
        if (options.DryRun)
        {
            Console.WriteLine("--dry-run: no source was changed.");
            return 0;
        }
        OutputFiles.WriteAll(
            result.Rewritten.Select(kv => (kv.Key, kv.Value)).ToList());
        return 0;
    }

    static void WriteOutputs(RuleResult result, string dir)
    {
        result.Manifest.Write(Path.Combine(dir, "manifest.json"));
        WriteLines(Path.Combine(dir, "queue.jsonl"), result.Queue);
        WriteLines(Path.Combine(dir, "sites.jsonl"), result.Sites);
        using var census = new StreamWriter(Path.Combine(dir, "census.txt"));
        result.PrintCensus(census);
    }

    static void WriteLines<T>(string path, List<T> entries)
    {
        File.WriteAllLines(path, entries.Select(e => JsonSerializer.Serialize(e)));
    }

    /// A manifest entry that doesn't name a method of the built DLL would make
    /// the IL diff fail later, or worse, cover nothing.
    public static void CheckMembers(
        CSharpCompilation compilation, Manifest manifest)
    {
        using var stream = new MemoryStream(InMemoryDll.Bytes(compilation));
        using var module = ModuleDefinition.ReadModule(stream);
        var exact = new HashSet<string>();
        var names = new HashSet<string>();
        foreach (var type in IlTools.AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                exact.Add(IlDiff.MemberKey(method));
                names.Add(type.FullName + "::" + method.Name);
            }
        }
        foreach (var member in manifest.Members)
        {
            if (!exact.Contains(member) && !names.Contains(member))
            {
                throw new Stop(
                    "A manifest member isn't a method of the built DLL: "
                        + member);
            }
        }
    }

    sealed class Options
    {
        public string Name;
        public string Out;
        public string Source;
        public bool DryRun;
        public ReceiverFilter Receivers = ReceiverFilter.All;

        public static Options Parse(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--receivers":
                        options.Receivers = ParseReceivers(Value(args, ref i));
                        break;
                    case "--out":
                        options.Out = Value(args, ref i);
                        break;
                    case "--src":
                        options.Source = Value(args, ref i);
                        break;
                    case "--dry-run":
                        options.DryRun = true;
                        break;
                    default:
                        if (options.Name != null || args[i].StartsWith("--"))
                        {
                            throw new Stop(Usage);
                        }
                        options.Name = args[i];
                        break;
                }
            }
            if (options.Name == null || options.Out == null)
            {
                throw new Stop(Usage);
            }
            return options;
        }

        static string Value(string[] args, ref int i)
        {
            i++;
            if (i >= args.Length)
            {
                throw new Stop(Usage);
            }
            return args[i];
        }

        static ReceiverFilter ParseReceivers(string text)
        {
            switch (text)
            {
                case "unity":
                    return ReceiverFilter.Unity;
                case "other":
                    return ReceiverFilter.Other;
                default:
                    throw new Stop(Usage);
            }
        }
    }
}
