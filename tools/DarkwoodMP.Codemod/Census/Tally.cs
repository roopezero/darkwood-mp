namespace DarkwoodMP.Codemod.Census;

/// Counts per key, split into production code and test code. Map test steps
/// and test helpers (MapScenario* and *Test* files) count as test code.
sealed class Tally
{
    readonly string _name;
    readonly Dictionary<string, int[]> _counts = new();

    public Tally(string name)
    {
        _name = name;
    }

    public static bool IsTestFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("MapScenario", StringComparison.Ordinal)
            || name.Contains("Test", StringComparison.Ordinal);
    }

    public void Add(string key, string file)
    {
        if (!_counts.TryGetValue(key, out var pair))
        {
            pair = new int[2];
            _counts[key] = pair;
        }
        if (IsTestFile(file))
        {
            pair[1]++;
        }
        else
        {
            pair[0]++;
        }
    }

    public int Total => _counts.Values.Sum(p => p[0] + p[1]);

    public int Production => _counts.Values.Sum(p => p[0]);

    public int Test => _counts.Values.Sum(p => p[1]);

    public int Of(string key)
    {
        if (_counts.TryGetValue(key, out var pair))
        {
            return pair[0] + pair[1];
        }
        return 0;
    }

    public void Print()
    {
        Console.WriteLine($"\n[{_name}] total {Total} (prod {Production} / test {Test})");
        var ordered = _counts
            .OrderByDescending(k => k.Value[0] + k.Value[1])
            .ThenBy(k => k.Key, StringComparer.Ordinal);
        foreach (var (key, pair) in ordered)
        {
            int all = pair[0] + pair[1];
            Console.WriteLine($"  {all,6}  ({pair[0],5} /{pair[1],5})  {key}");
        }
    }
}
