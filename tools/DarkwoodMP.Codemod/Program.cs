using DarkwoodMP.Codemod.Census;
using DarkwoodMP.Codemod.Check;
using DarkwoodMP.Codemod.Format;
using DarkwoodMP.Codemod.SelfTest;
using DarkwoodMP.Codemod.Stages;

namespace DarkwoodMP.Codemod;

/// The formatting tool of the migration plan. See README.md.
static class Program
{
    const string Usage = """
        codemod census                 counts per construct and context;
                                       writes nothing
        codemod braces                 stage 1: every statement gets braces
        codemod layout                 stage 2: one-line blocks and bodies,
                                       then the Formatter over every file
        codemod check [--braces] <dirA> <dirB>
                                       same code, same comment words and every
                                       comment in its place, file by file?
                                       --braces allows the new blocks of stage 1
        codemod selftest [--show NAME] the golden cases of both stages
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
            case "check":
                return CheckCommand.Run(rest);
            case "selftest":
                return SelfTestCommand.Run(rest);
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }
}
