namespace FreeW.App.Presentation.Links;

/// <summary>
/// Normalized link target text shared by Insert/Edit Hyperlink surfaces.
/// </summary>
public readonly record struct HyperlinkTarget(string? Url, string? Anchor)
{
    public bool IsExternal => !string.IsNullOrEmpty(Url);

    public bool IsInternal => !string.IsNullOrEmpty(Anchor);

    public bool HasTarget => IsExternal || IsInternal;

    public string DisplayFallback => Anchor ?? Url ?? string.Empty;

    /// <summary>The normalized address shown by Insert/Edit Hyperlink surfaces.</summary>
    public string Address => IsInternal ? "#" + Anchor : Url ?? string.Empty;

    public static bool TryParse(string? text, out HyperlinkTarget target)
    {
        target = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.StartsWith('#'))
        {
            var anchor = trimmed[1..].Trim();
            if (anchor.Length == 0)
                return false;

            target = new HyperlinkTarget(null, anchor);
            return true;
        }

        var normalized = Normalize(trimmed);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
            return false;

        target = new HyperlinkTarget(normalized, null);
        return true;
    }

    /// <summary>
    /// Normalizes an external target into a well-formed absolute URI the way Word's AutoFormat does:
    /// a bare email address (contains '@', no scheme) gets a <c>mailto:</c> prefix; a scheme-less host
    /// (e.g. <c>www.example.com</c> or <c>example.com</c>) gets an <c>https://</c> prefix (matching the
    /// seed FreeW's own "new hyperlink" ribbon command already uses, see FreeWRibbonCommands). A value
    /// that already has a URI scheme (has a ':' before the first '/', '@', or whitespace) is left as-is.
    /// </summary>
    private static string Normalize(string trimmed)
    {
        if (HasScheme(trimmed))
            return trimmed;

        if (trimmed.Contains('@') && !trimmed.Contains(' '))
            return "mailto:" + trimmed;

        return "https://" + trimmed;
    }

    private static bool HasScheme(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0)
            return false;

        // Everything before the colon must look like a scheme (letters/digits/+/-/.) — this rules out
        // "C:\path" style false positives and stray colons inside a scheme-less token.
        for (var i = 0; i < colon; i++)
        {
            var c = value[i];
            if (!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
                return false;
        }
        // A single-letter "scheme" followed by a colon is almost always a Windows drive letter, not a URI.
        return colon > 1;
    }
}
