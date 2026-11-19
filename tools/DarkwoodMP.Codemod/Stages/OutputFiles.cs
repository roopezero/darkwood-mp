using System.Text;

namespace DarkwoodMP.Codemod.Stages;

/// Puts a stage's output on disk. A half-rewritten tree or a cut-off source is
/// the one thing a stage must not leave behind, so every new text goes to a
/// temp file beside its source first, and the sources are replaced only when
/// all the temp files are complete. Any failure stops the command with a Stop
/// that names the file, and the temp files are deleted.
static class OutputFiles
{
    /// Doesn't end in .cs: the build can't pick up a file a killed run left.
    public const string TempSuffix = ".codemod-tmp";

    public delegate void Replace(string temp, string file);

    public static void WriteAll(
        IReadOnlyList<(string File, string Text)> outputs)
    {
        WriteAll(
            outputs,
            (temp, file) => File.Move(temp, file, overwrite: true));
    }

    /// The self test passes its own replace, to make one of them fail. Every
    /// exception counts: .NET reports a file over the size limit as an
    /// ArgumentOutOfRangeException, and this code does nothing but file work.
    public static void WriteAll(
        IReadOnlyList<(string File, string Text)> outputs, Replace replace)
    {
        var file = "";
        int replaced = 0;
        try
        {
            foreach (var output in outputs)
            {
                file = output.File;
                RequireWritable(file);
            }
            foreach (var output in outputs)
            {
                file = output.File;
                WriteTemp(file + TempSuffix, output.Text, file);
            }
            foreach (var output in outputs)
            {
                file = output.File;
                replace(file + TempSuffix, file);
                replaced++;
            }
        }
        catch (Exception e)
        {
            var left = outputs.Skip(replaced).Select(o => o.File + TempSuffix);
            throw new Stop(
                $"{file} can't be written ({e.Message.TrimEnd('.')}). "
                    + Damage(replaced, outputs.Count) + Delete(left));
        }
    }

    /// A rename needs no write permission on the file it replaces, so without
    /// this a read-only source would be overwritten without a word. Opening
    /// the file for writing changes nothing in it.
    static void RequireWritable(string file)
    {
        using var stream = new FileStream(
            file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
    }

    static void WriteTemp(string temp, string text, string file)
    {
        using var stream = new FileStream(
            temp, FileMode.Create, FileAccess.Write);
        stream.Write(new UTF8Encoding(false).GetBytes(text));
        // Some file systems report a full disk only when the data is flushed.
        stream.Flush(flushToDisk: true);
        KeepMode(temp, file);
    }

    /// git records a file's executable bit, and the new file replaces the old
    /// one's mode along with its text.
    static void KeepMode(string temp, string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, File.GetUnixFileMode(file));
        }
    }

    /// What a failure leaves behind, and how to undo it.
    static string Damage(int replaced, int total)
    {
        if (replaced == 0)
        {
            return "Nothing was changed.";
        }
        return $"{replaced} of {total} files were already replaced: restore "
            + "them with 'git checkout -- src/DarkwoodMP' in the repo's root.";
    }

    /// Deletes the temp files and returns a note on any it couldn't, so that
    /// a failed delete doesn't hide the error that stopped the stage.
    static string Delete(IEnumerable<string> temps)
    {
        var stayed = new List<string>();
        foreach (var temp in temps)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                stayed.Add(temp);
            }
        }
        if (stayed.Count == 0)
        {
            return "";
        }
        return " Temp files left behind: " + string.Join(", ", stayed);
    }
}
