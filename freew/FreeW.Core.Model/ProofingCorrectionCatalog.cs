namespace FreeW.Core.Model;

/// <summary>
/// Deterministic corrections for the spelling diagnostics emitted by
/// <see cref="ProofingDiagnosticPlanner"/>. This is deliberately a small portable catalog, not an
/// operating-system dictionary or a claim of complete language coverage.
/// </summary>
public sealed record ProofingCorrection(string Misspelling, IReadOnlyList<string> Suggestions);

public static class ProofingCorrectionCatalog
{
    public static IReadOnlyList<ProofingCorrection> Entries { get; } =
    [
        Correction("acommodate", "accommodate"),
        Correction("adress", "address"),
        Correction("arguement", "argument"),
        Correction("beleive", "believe"),
        Correction("definately", "definitely"),
        Correction("enviroment", "environment"),
        Correction("occured", "occurred"),
        Correction("recieve", "receive"),
        Correction("seperate", "separate"),
        Correction("teh", "the"),
        Correction("wierd", "weird"),
    ];

    public static IReadOnlyList<string> SuggestionsFor(string? word)
    {
        var normalized = ProofingDiagnosticPlanner.NormalizeWord(word);
        if (normalized is null)
            return Array.Empty<string>();

        var entry = Entries.FirstOrDefault(item =>
            string.Equals(item.Misspelling, normalized, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return Array.Empty<string>();

        return entry.Suggestions
            .Select(suggestion => ApplyCasing(word!, suggestion))
            .ToArray();
    }

    private static ProofingCorrection Correction(string misspelling, params string[] suggestions) =>
        new(misspelling, suggestions);

    private static string ApplyCasing(string source, string suggestion)
    {
        if (source.Length > 0 && source.All(char.IsUpper))
            return suggestion.ToUpperInvariant();
        if (source.Length > 0 && char.IsUpper(source[0])
            && source.Skip(1).All(ch => !char.IsLetter(ch) || char.IsLower(ch)))
            return char.ToUpperInvariant(suggestion[0]) + suggestion[1..];
        return suggestion;
    }
}
