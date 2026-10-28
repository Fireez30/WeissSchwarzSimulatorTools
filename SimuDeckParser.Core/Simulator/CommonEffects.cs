// StreamingAssets/CommonEffects_USER_REFERENCE.txt (formerly CommonEffects(copy).txt): reusable
// effects referenced by cards as *EffectName(param1,param2). Each has a script (what the
// simulator runs) and a text (what players read).

using System.Text;

namespace SimuDeckParser.Simulator;

/// <summary>
/// Effect name (with its parameter list, e.g. Brainstorm($X)) -> effect text, and -> script.
/// Ordered, because prefix matching takes the first matching key.
/// </summary>
public sealed class CommonEffects
{
    public SortedDictionary<string, string> Text { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Script { get; } = new(StringComparer.Ordinal);

    public static CommonEffects Load(string path)
    {
        try
        {
            return Parse(TextHelpers.DecodeText(File.ReadAllBytes(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CommonEffects();
        }
    }

    public static CommonEffects Parse(string content)
    {
        var effects = new CommonEffects();
        var key = "";
        var value = new StringBuilder();
        // The script: the lines between Define: and the first Text line, one per line.
        var script = new StringBuilder();
        var readingValue = false;

        void Flush()
        {
            if (key.Length > 0 && script.Length > 0)
                effects.Script[key] = script.ToString();
            if (key.Length > 0 && value.Length > 0)
                effects.Text[key] = value.ToString();
            key = "";
            value.Clear();
            script.Clear();
        }

        foreach (var line in TextHelpers.Lines(content))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || trimmed == "Quick")
                continue;
            if (trimmed.StartsWith("Define:", StringComparison.Ordinal))
            {
                Flush();
                key = trimmed["Define:".Length..].Trim();
                readingValue = false;
            }
            else if (key.Length == 0)
            {
                continue;
            }
            else if (trimmed.StartsWith("Text ", StringComparison.Ordinal))
            {
                readingValue = true;
                value.Clear().Append(trimmed[5..].Trim());
            }
            else if (readingValue && !new[] { "Tag ", "Act:", "Auto:", "Cont:" }.Any(p => trimmed.StartsWith(p, StringComparison.Ordinal)))
            {
                value.Append(trimmed);
            }
            else if (!readingValue && !trimmed.StartsWith("Tag ", StringComparison.Ordinal))
            {
                script.Append(trimmed).Append('\n');
            }
        }
        // The original dropped the last definition of the file.
        Flush();
        return effects;
    }
}
