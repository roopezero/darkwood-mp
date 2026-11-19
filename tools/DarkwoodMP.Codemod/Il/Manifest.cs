using System.Text.Json;
using System.Text.Json.Serialization;

namespace DarkwoodMP.Codemod.Il;

/// A change to the string constants of one member that a rule made on purpose.
/// Without one of these, a changed string in a rewritten member fails the diff.
sealed class StringChange
{
    [JsonPropertyName("member")]
    public string Member { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("removed")]
    public List<string> Removed { get; set; } = new();

    [JsonPropertyName("added")]
    public List<string> Added { get; set; } = new();
}

/// What a rule says it touched. A member is "Ns.Type::Name(Param,Types)", in
/// Cecil's spelling, or "Ns.Type::Name" for every overload of that name. An
/// allowed entry names one thing that may be added or removed: "type T",
/// "field T::x", "property T::x", "event T::x" or "method <full name>".
sealed class Manifest
{
    static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        WriteIndented = true,
    };

    [JsonPropertyName("rule")]
    public string Rule { get; set; } = "";

    [JsonPropertyName("members")]
    public List<string> Members { get; set; } = new();

    [JsonPropertyName("allowed")]
    public List<string> Allowed { get; set; } = new();

    [JsonPropertyName("stringChanges")]
    public List<StringChange> StringChanges { get; set; } = new();

    public static Manifest Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new Stop("No such manifest: " + path);
        }
        try
        {
            return JsonSerializer.Deserialize<Manifest>(
                File.ReadAllText(path), Json);
        }
        catch (JsonException e)
        {
            throw new Stop("The manifest " + path + " isn't valid: " + e.Message);
        }
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json) + "\n");
    }

    /// "Ns.Type::Name(int)" becomes "Ns.Type::Name".
    public static string NameLevel(string member)
    {
        int open = member.IndexOf('(');
        if (open < 0)
        {
            return member;
        }
        return member.Substring(0, open);
    }
}
