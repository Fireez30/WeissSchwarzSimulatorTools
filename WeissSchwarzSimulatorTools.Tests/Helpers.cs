namespace WeissSchwarzSimulatorTools.Tests;

/// <summary>A temporary folder, deleted with everything in it when disposed.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("sdp-test-").FullName;

    public string Join(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Writes <paramref name="content"/> at <paramref name="relative"/>, creating its folders.</summary>
    public string Write(string relative, string content) => Write(relative, System.Text.Encoding.UTF8.GetBytes(content));

    public string Write(string relative, byte[] content)
    {
        var path = Join(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Cards
{
    public static Card Make(string key, CardType type = CardType.Character, CardColor color = CardColor.Red,
        uint level = 0, uint cost = 0, int power = 0, string? name = null, string text = "",
        IReadOnlyList<string>? traits = null, IReadOnlyList<Trigger>? triggers = null, uint soul = 1, string? image = null) =>
        new()
        {
            Key = key,
            Type = type,
            Name = name ?? $"name {key}",
            Color = color,
            Level = level,
            Cost = cost,
            Power = power,
            Text = text,
            Traits = traits ?? [],
            Triggers = triggers ?? [],
            SoulCount = soul,
            Image = image,
        };

    public static List<string> Keys(IEnumerable<Card> cards) => cards.Select(c => c.Key).ToList();
}

/// <summary>A test that loads a real simulator install (read-only): skipped unless SIMULATOR_ROOT is set.</summary>
public sealed class RealInstallFactAttribute : FactAttribute
{
    public RealInstallFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIMULATOR_ROOT")))
            Skip = "set SIMULATOR_ROOT to a simulator install to run it";
    }
}

/// <summary>A test that uses the network or the system keyring: skipped unless WSST_LIVE_TESTS=1.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WSST_LIVE_TESTS") != "1")
            Skip = "set WSST_LIVE_TESTS=1 to run it (network, system keyring)";
    }
}
