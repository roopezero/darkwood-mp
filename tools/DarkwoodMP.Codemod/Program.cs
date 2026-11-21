using DarkwoodMP.Codemod.Census;
using DarkwoodMP.Codemod.Check;
using DarkwoodMP.Codemod.DiffTest;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.Il;
using DarkwoodMP.Codemod.Interpolated;
using DarkwoodMP.Codemod.Rules;
using DarkwoodMP.Codemod.SelfTest;
using DarkwoodMP.Codemod.Stages;

namespace DarkwoodMP.Codemod;

/// The formatting tool of the migration plan. See README.md.
static class Program
{
    const string Usage = """
        codemod census [--samples | --operators]
                                       counts per construct and context;
                                       writes nothing. --operators prints the
                                       sites left of each operator
        codemod braces                 stage 1: every statement gets braces
        codemod layout                 stage 2: one-line blocks and bodies,
                                       then the Formatter over every file
        codemod wrap [--report FILE | --explain FILE LINE]
                                       stage 10: every line at most 80
                                       columns; lists the lines left over.
                                       --explain shows one line's plan
        codemod check [--braces] <dirA> <dirB>
                                       same code, same comment words and every
                                       comment in its place, file by file?
                                       --braces allows the new blocks of stage 1
        codemod rule <name> [--receivers unity|other] --out DIR [--src DIR] [--dry-run]
                                       one operator rule: writes manifest.json,
                                       queue.jsonl and sites.jsonl to DIR
        codemod ildiff <a.dll> <b.dll> [--manifest touched.json] [--list]
                                       did only the manifest's members change?
        codemod interpolated --out DIR [--src DIR] [--dry-run]
                                       stage 11: long interpolated strings
                                       split into parts joined with +;
                                       writes manifest.json, sites.jsonl and
                                       queue.jsonl to DIR
        codemod difftest <rule>        runs a rule's differential cases
        codemod selftest [--show NAME] the golden cases and the tool's checks
        """;

    static int Main(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (Stop stop)
        {
            Console.Error.WriteLine(stop.Message);
            return 2;
        }
    }

    static int Dispatch(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        var rest = args.Skip(1).ToArray();
        switch (args[0])
        {
            case "census":
                return CensusCommand.Run(rest);
            case "braces":
                return StageCommand.Run("braces", Passes.Braces);
            case "layout":
                return StageCommand.Run("layout", Passes.Layout);
            case "wrap":
                return WrapCommand.Run(rest);
            case "check":
                return CheckCommand.Run(rest);
            case "rule":
                return RuleCommand.Run(rest);
            case "ildiff":
                return IlDiffCommand.Run(rest);
            case "interpolated":
                return InterpolatedCommand.Run(rest);
            case "difftest":
                return DiffTestCommand.Run(rest);
            case "selftest":
                return SelfTestCommand.Run(rest);
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }
}
