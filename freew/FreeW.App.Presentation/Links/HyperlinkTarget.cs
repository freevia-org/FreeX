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

        target = new HyperlinkTarget(trimmed, null);
        return true;
    }
}
