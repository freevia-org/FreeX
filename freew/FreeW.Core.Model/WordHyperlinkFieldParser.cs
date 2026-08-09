namespace FreeW.Core.Model;

/// <summary>The navigable subset of a native Word <c>HYPERLINK</c> field instruction.</summary>
public readonly record struct HyperlinkFieldTarget(string? Url, string? Anchor, string? Tooltip);

/// <summary>Projects Word's native HYPERLINK instruction onto FreeW's existing link model.</summary>
public static class WordHyperlinkFieldParser
{
    /// <summary>
    /// Parses the destination, <c>\l</c> location, and <c>\o</c> ScreenTip while leaving the source
    /// instruction untouched. Unsupported switches remain available through <see cref="ComplexField"/>.
    /// </summary>
    public static bool TryParse(ComplexField? field, out HyperlinkFieldTarget target)
    {
        target = default;
        if (field is not { Keyword: "HYPERLINK" })
            return false;

        var address = EmptyToNull(ComplexFieldEngine.Argument(field.Instruction));
        var location = EmptyToNull(ComplexFieldEngine.SwitchValue(field.Instruction, 'l'));
        var tooltip = EmptyToNull(ComplexFieldEngine.SwitchValue(field.Instruction, 'o'));
        if (address is null && location is null)
            return false;

        if (address is not null)
        {
            target = new HyperlinkFieldTarget(
                location is null ? address : CombineAddressAndLocation(address, location),
                Anchor: null,
                tooltip);
        }
        else
        {
            target = new HyperlinkFieldTarget(Url: null, location, tooltip);
        }

        return true;
    }

    private static string CombineAddressAndLocation(string address, string location)
    {
        var fragment = address.IndexOf('#');
        var baseAddress = fragment >= 0 ? address[..fragment] : address;
        return $"{baseAddress}#{location}";
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
