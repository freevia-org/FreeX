namespace Free.Shared.IO;

/// <summary>
/// Builds a portable output filename stem from a user- or document-supplied name.
/// Callers retain control of the replacement character and output-specific suffix.
/// </summary>
public static class OutputFileNameStemPolicy
{
    public static string Normalize(
        string? candidate,
        string fallback,
        char invalidCharacterReplacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        var invalidCharacters = Path.GetInvalidFileNameChars();
        if (Array.IndexOf(invalidCharacters, invalidCharacterReplacement) >= 0)
        {
            throw new ArgumentException(
                "The invalid-character replacement must be valid in a filename.",
                nameof(invalidCharacterReplacement));
        }

        var fallbackStem = NormalizeCore(fallback, invalidCharacters, invalidCharacterReplacement);
        if (string.IsNullOrWhiteSpace(fallbackStem))
            throw new ArgumentException("The fallback must produce a usable filename stem.", nameof(fallback));

        if (string.IsNullOrWhiteSpace(candidate))
            return fallbackStem;

        var stem = NormalizeCore(candidate, invalidCharacters, invalidCharacterReplacement);
        return string.IsNullOrWhiteSpace(stem) ? fallbackStem : stem;
    }

    private static string NormalizeCore(
        string value,
        char[] invalidCharacters,
        char invalidCharacterReplacement)
    {
        string stem;
        try
        {
            stem = Path.GetFileNameWithoutExtension(value.Trim()) ?? string.Empty;
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }

        var characters = stem.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (Array.IndexOf(invalidCharacters, characters[index]) >= 0)
                characters[index] = invalidCharacterReplacement;
        }

        return new string(characters).Trim();
    }
}
