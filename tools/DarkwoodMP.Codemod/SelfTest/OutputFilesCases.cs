using System.Runtime.Versioning;
using DarkwoodMP.Codemod.Stages;

namespace DarkwoodMP.Codemod.SelfTest;

/// The ways writing a stage's files can fail, each in a temp folder of its own:
/// every source must be whole afterwards, the message must name the file and
/// say what happened to the tree, and no temp file may stay. The cases set
/// file modes, so they run where those exist.
[UnsupportedOSPlatform("windows")]
static class OutputFilesCases
{
    const string Restore = "git checkout -- src/DarkwoodMP";

    static readonly (string Name, Func<Sandbox, string> Run)[] Cases =
    {
        ("every file is replaced", AllReplaced),
        ("a read-only file stops the stage first", ReadOnlyFile),
        ("a failed temp file leaves the sources alone", ReadOnlyFolder),
        ("a failed first replace changes nothing", FirstReplaceFails),
        ("a failed later replace says how to restore (IOException)",
            s => LaterReplaceFails(s, new IOException("disk gone"))),
        ("a failed later replace says how to restore (access denied)",
            s => LaterReplaceFails(
                s, new UnauthorizedAccessException("denied"))),
        ("a failed later replace says how to restore (any other exception)",
            s => LaterReplaceFails(
                s, new ArgumentOutOfRangeException("value", "too large"))),
        ("a temp file that can't be deleted is named", TempCantBeDeleted),
    };

    public static int Count => Cases.Length;

    /// One line for each case that went wrong.
    public static List<string> Failures()
    {
        var problems = new List<string>();
        foreach (var (name, run) in Cases)
        {
            var sandbox = new Sandbox();
            try
            {
                var problem = run(sandbox);
                if (problem != null)
                {
                    problems.Add($"  output files, {name}: {problem}");
                }
            }
            catch (Exception e)
            {
                problems.Add($"  output files, {name}: threw {e}");
            }
            finally
            {
                sandbox.Dispose();
            }
        }
        return problems;
    }

    static string AllReplaced(Sandbox box)
    {
        File.SetUnixFileMode(box.Two, Sandbox.Executable);
        OutputFiles.WriteAll(box.Outputs);
        if (File.GetUnixFileMode(box.Two) != Sandbox.Executable)
        {
            return "the file's mode changed";
        }
        return box.Holds(Sandbox.New, Sandbox.New, Sandbox.New);
    }

    static string ReadOnlyFile(Sandbox box)
    {
        if (Environment.IsPrivilegedProcess)
        {
            return null;
        }
        File.SetUnixFileMode(box.Two, Sandbox.ReadOnly);
        var stop = Stopped(() => OutputFiles.WriteAll(box.Outputs));
        File.SetUnixFileMode(box.Two, Sandbox.Writable);
        return Unchanged(box, stop, box.Two);
    }

    static string ReadOnlyFolder(Sandbox box)
    {
        if (Environment.IsPrivilegedProcess)
        {
            return null;
        }
        File.SetUnixFileMode(box.Second, Sandbox.ReadOnlyFolder);
        var stop = Stopped(() => OutputFiles.WriteAll(box.Outputs));
        File.SetUnixFileMode(box.Second, Sandbox.WritableFolder);
        return Unchanged(box, stop, box.Three);
    }

    static string FirstReplaceFails(Sandbox box)
    {
        var stop = Stopped(() => OutputFiles.WriteAll(
            box.Outputs, (temp, file) => throw new IOException("disk gone")));
        return Unchanged(box, stop, box.One);
    }

    /// The first file is replaced for real, then the second one fails.
    static string LaterReplaceFails(Sandbox box, Exception failure)
    {
        int calls = 0;
        var stop = Stopped(() => OutputFiles.WriteAll(
            box.Outputs,
            (temp, file) =>
            {
                calls++;
                if (calls > 1)
                {
                    throw failure;
                }
                File.Move(temp, file, overwrite: true);
            }));
        if (stop == null)
        {
            return "no Stop was thrown";
        }
        if (!Names(stop, box.Two))
        {
            return "the message doesn't name the file: " + stop;
        }
        if (!stop.Contains(Restore) || !stop.Contains("1 of 3"))
        {
            return "the message doesn't say how to restore: " + stop;
        }
        return box.Holds(Sandbox.New, Sandbox.Old, Sandbox.Old);
    }

    /// The folder of the last temp file turns read-only just before the second
    /// replace fails: the first failure must still be the one reported.
    static string TempCantBeDeleted(Sandbox box)
    {
        if (Environment.IsPrivilegedProcess)
        {
            return null;
        }
        int calls = 0;
        var stop = Stopped(() => OutputFiles.WriteAll(
            box.Outputs,
            (temp, file) =>
            {
                calls++;
                if (calls > 1)
                {
                    File.SetUnixFileMode(box.Second, Sandbox.ReadOnlyFolder);
                    throw new IOException("disk gone");
                }
                File.Move(temp, file, overwrite: true);
            }));
        File.SetUnixFileMode(box.Second, Sandbox.WritableFolder);
        if (stop == null || !stop.Contains("disk gone"))
        {
            return "the first failure wasn't reported: " + stop;
        }
        var stayed = box.Three + OutputFiles.TempSuffix;
        if (!stop.Contains("left behind: " + stayed))
        {
            return "the message doesn't name the temp file: " + stop;
        }
        File.Delete(stayed);
        return box.Holds(Sandbox.New, Sandbox.Old, Sandbox.Old);
    }

    /// The checks for a stage that stopped before it changed any source.
    static string Unchanged(Sandbox box, string stop, string file)
    {
        if (stop == null)
        {
            return "no Stop was thrown";
        }
        if (!Names(stop, file))
        {
            return "the message doesn't name the file: " + stop;
        }
        if (!stop.Contains("Nothing was changed") || stop.Contains(Restore))
        {
            return "the message is wrong about the sources: " + stop;
        }
        return box.Holds(Sandbox.Old, Sandbox.Old, Sandbox.Old);
    }

    /// The system's reason, in brackets, often names the path by itself, so
    /// only the text before it counts.
    static bool Names(string stop, string file) =>
        stop.Split('(')[0].Contains(file);

    /// The Stop's message, or null when the action didn't stop.
    static string Stopped(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Stop stop)
        {
            return stop.Message;
        }
    }

    /// Two folders with three source files. Dispose deletes each file by name
    /// and the folders only when they are empty, never a whole tree.
    sealed class Sandbox : IDisposable
    {
        public const string Old = "old text\n";

        /// Not ASCII, so a wrong encoding or a byte order mark shows.
        public const string New = "new text: ä\n";

        public const UnixFileMode Writable =
            UnixFileMode.UserRead | UnixFileMode.UserWrite;

        public const UnixFileMode ReadOnly = UnixFileMode.UserRead;

        public const UnixFileMode Executable =
            Writable | UnixFileMode.UserExecute;

        public const UnixFileMode WritableFolder =
            Writable | UnixFileMode.UserExecute;

        public const UnixFileMode ReadOnlyFolder =
            UnixFileMode.UserRead | UnixFileMode.UserExecute;

        readonly string _root;

        public readonly string First;
        public readonly string Second;
        public readonly string One;
        public readonly string Two;
        public readonly string Three;

        public Sandbox()
        {
            _root = Directory.CreateTempSubdirectory("dwmp-codemod-").FullName;
            First = Directory.CreateDirectory(_root + "/first").FullName;
            Second = Directory.CreateDirectory(_root + "/second").FullName;
            One = First + "/one.cs";
            Two = First + "/two.cs";
            Three = Second + "/three.cs";
            foreach (var file in new[] { One, Two, Three })
            {
                File.WriteAllText(file, Old);
                File.SetUnixFileMode(file, Writable);
            }
        }

        public List<(string File, string Text)> Outputs => new()
        {
            (One, New),
            (Two, New),
            (Three, New),
        };

        /// What's wrong with the files, or null when they hold these texts
        /// to the byte and no temp file is left.
        public string Holds(string one, string two, string three)
        {
            var expected = new[] { (One, one), (Two, two), (Three, three) };
            foreach (var (file, text) in expected)
            {
                var bytes = File.ReadAllBytes(file);
                if (!bytes.SequenceEqual(new System.Text.UTF8Encoding(false)
                    .GetBytes(text)))
                {
                    return file + " doesn't hold the expected bytes";
                }
                if (File.Exists(file + OutputFiles.TempSuffix))
                {
                    return "a temp file stayed beside " + file;
                }
            }
            return null;
        }

        public void Dispose()
        {
            File.SetUnixFileMode(Second, WritableFolder);
            foreach (var file in new[] { One, Two, Three })
            {
                File.Delete(file);
                File.Delete(file + OutputFiles.TempSuffix);
            }
            Directory.Delete(First);
            Directory.Delete(Second);
            Directory.Delete(_root);
        }
    }
}
