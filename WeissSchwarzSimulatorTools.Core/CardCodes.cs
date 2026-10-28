// The many spellings of a card code.
//
// Encore Decks, the simulator's card data and the artwork file names don't always agree on the
// exact card code: English cards gain an E after the dash (-E073), trial decks use -TE, promos
// -PE, foils add a rarity suffix (073SP) and some files are lowercase. These helpers generate
// the candidate spellings, in the same order as the original tool.

namespace WeissSchwarzSimulatorTools;

public static class CardCodes
{
    /// <summary>
    /// A candidate spelling of a card code. <c>Lowercase</c> means the lookup is done on the
    /// lowercased candidate, while the candidate itself (original case) is what gets returned.
    /// </summary>
    internal readonly record struct Variant(string Candidate, bool Lowercase)
    {
        public string LookupKey => Lowercase ? Candidate.ToLowerInvariant() : Candidate;
    }

    /// <summary>A rewrite of the dash of a card code, and its reverse: KS/W49-073 &lt;-&gt; KS/W49-E073.</summary>
    private readonly record struct Rewrite(string AFrom, string ATo, string BFrom, string BTo);

    /// <summary>English cards: -001 &lt;-&gt; -E001.</summary>
    private static readonly Rewrite English = new("-", "-E", "-E", "-");
    /// <summary>English trial decks: -T01 &lt;-&gt; -TE01.</summary>
    private static readonly Rewrite Trial = new("-T", "-TE", "-TE", "-T");
    /// <summary>English promos: -P01 &lt;-&gt; -PE01.</summary>
    private static readonly Rewrite Promo = new("-P", "-PE", "-PE", "-P");

    /// <summary>For each rewrite: both directions exactly, then both case-insensitively.</summary>
    private static IEnumerable<Variant> Rewritten(string code, Rewrite rewrite)
    {
        var a = code.Replace(rewrite.AFrom, rewrite.ATo, StringComparison.Ordinal);
        var b = code.Replace(rewrite.BFrom, rewrite.BTo, StringComparison.Ordinal);
        return [new(a, false), new(b, false), new(a, true), new(b, true)];
    }

    /// <summary>
    /// Candidate spellings used to match an imported code against the simulator's card keys:
    /// the code as is, its rewrites, then the same without the rarity suffix (-001SP -> -001).
    /// Each spelling is tried exactly, then case-insensitively.
    /// </summary>
    internal static List<Variant> KeyVariants(string key)
    {
        var output = new List<Variant> { new(key, false), new(key, true) };
        foreach (var rewrite in new[] { English, Trial, Promo })
            output.AddRange(Rewritten(key, rewrite));

        var stripped = TextHelpers.RemoveTrailingAlphas(key);
        output.Add(new(stripped, false));
        output.Add(new(stripped, true));
        // The original tool tried the promo rewrite first here.
        foreach (var rewrite in new[] { Promo, English, Trial })
            output.AddRange(Rewritten(stripped, rewrite));
        return output;
    }

    /// <summary>Maps each imported code to the spelling that exists in the simulator, or keeps it unchanged.</summary>
    public static List<string> TransformToExistingKeys(IReadOnlySet<string> existing, IEnumerable<string> keys) =>
        keys.Select(key =>
                KeyVariants(key).FirstOrDefault(v => existing.Contains(v.LookupKey)) is { Candidate: not null } v
                    ? v.Candidate
                    : key)
            .ToList();

    /// <summary>
    /// Spellings of a simulator code to try against Encore Decks, which matches codes exactly:
    /// the code as-is, then the -E / -TE / -PE rewrites, then without the rarity suffix, then the
    /// same in uppercase. No duplicates.
    ///
    /// Encore Decks only knows cards with several arts by their first art (SDS/SX03-022a, never
    /// SDS/SX03-022), so a spelling ending in a digit is tried with an 'a' first.
    /// </summary>
    public static List<string> EncoreSpellings(string key)
    {
        var output = new List<string>();
        foreach (var code in new[] { key.Trim(), key.Trim().ToUpperInvariant() })
        {
            foreach (var v in KeyVariants(code))
            {
                if (v.Lowercase) continue;
                var spellings = v.Candidate.Length > 0 && char.IsAsciiDigit(v.Candidate[^1])
                    ? new[] { v.Candidate + "a", v.Candidate }
                    : new[] { v.Candidate };
                foreach (var spelling in spellings)
                    if (!output.Contains(spelling))
                        output.Add(spelling);
            }
        }
        return output;
    }
}
