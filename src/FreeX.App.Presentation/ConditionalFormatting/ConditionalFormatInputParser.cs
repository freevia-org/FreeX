using System.Globalization;

using FreeX.Core.Model;

namespace FreeX.App.Presentation.ConditionalFormatting;

/// <summary>
/// Pure parse/validate/format helpers for the conditional-format rule editors, shared by both the
/// Windows host dialog (<c>ConditionalFormatDialog.Parsing</c>) and the cross-platform rule builder
/// (<c>ConditionalFormatRuleBuilder</c>). These mirror the text-box round-trips both editors perform
/// when committing a rule: trimming optional fields, validating bar-length percents and top/bottom
/// ranks, and parsing <c>"r,g,b"</c> colour text. Kept free of any UI framework so both hosts share
/// one definition instead of maintaining divergent copies.
/// </summary>
public static class ConditionalFormatInputParser
{
    /// <summary>Trims a free-text field, returning <see langword="null"/> for blank/whitespace input.</summary>
    public static string? BlankToNull(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Validates an optional data-bar length percent. Blank is allowed (yields <see langword="null"/>);
    /// a present value must be an integer in [0, 100]. Returns <see langword="false"/> for out-of-range
    /// or non-integer input.
    /// </summary>
    public static bool TryParseOptionalPercent(string? text, out int? percent)
    {
        percent = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return true;

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value is < 0 or > 100)
            return false;

        percent = value;
        return true;
    }

    /// <summary>
    /// Validates a Top/Bottom rank or percent entry. The value must be an integer in [1, 1000];
    /// returns <see langword="false"/> otherwise.
    /// </summary>
    public static bool TryParseTopBottomRank(string? text, out int rank)
    {
        rank = 0;
        return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out rank)
            && rank is >= 1 and <= 1000;
    }

    /// <summary>Formats an <see cref="RgbColor"/> as the editor's <c>"r,g,b"</c> text.</summary>
    public static string FormatRgb(RgbColor color) =>
        $"{color.R},{color.G},{color.B}";

    /// <summary>
    /// Parses the editor's <c>"r,g,b"</c> colour text (each component a byte 0-255). Returns
    /// <see langword="false"/> for malformed or out-of-range input.
    /// </summary>
    public static bool TryParseRgbColor(string? text, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            return false;

        color = new RgbColor(r, g, b);
        return true;
    }

    /// <summary>
    /// Parses an optional <c>"r,g,b"</c> colour field, returning <see langword="null"/> for blank input
    /// or for text that does not parse as a colour.
    /// </summary>
    public static RgbColor? ParseOptionalRgbColor(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : TryParseRgbColor(text, out var color) ? color : null;
}
