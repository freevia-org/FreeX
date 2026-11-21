using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>Builds and applies user-defined named cell styles (Excel's New Cell Style / Modify).</summary>
public static class CustomCellStylePlanner
{
    /// <summary>The style groups a style carries, as a diff that leaves every other attribute untouched.</summary>
    public static StyleDiff BuildDiff(CustomCellStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        var full = StyleDiff.FromStyle(style.Style);
        var parts = style.Parts;
        var diff = new StyleDiff();

        if (parts.HasFlag(CustomCellStyleParts.NumberFormat))
            diff = diff with { NumberFormat = full.NumberFormat };

        if (parts.HasFlag(CustomCellStyleParts.Alignment))
            diff = diff with
            {
                HAlign = full.HAlign,
                VAlign = full.VAlign,
                WrapText = full.WrapText,
                ShrinkToFit = full.ShrinkToFit,
                IndentLevel = full.IndentLevel,
                TextRotation = full.TextRotation,
                ReadingOrder = full.ReadingOrder,
            };

        if (parts.HasFlag(CustomCellStyleParts.Font))
            diff = diff with
            {
                Bold = full.Bold,
                Italic = full.Italic,
                Underline = full.Underline,
                DoubleUnderline = full.DoubleUnderline,
                Strikethrough = full.Strikethrough,
                Superscript = full.Superscript,
                Subscript = full.Subscript,
                FontName = full.FontName,
                FontSize = full.FontSize,
                FontColor = full.FontColor,
                FontThemeColor = full.FontThemeColor,
                FontScheme = full.FontScheme,
            };

        if (parts.HasFlag(CustomCellStyleParts.Border))
            diff = diff with
            {
                BorderTop = full.BorderTop,
                BorderRight = full.BorderRight,
                BorderBottom = full.BorderBottom,
                BorderLeft = full.BorderLeft,
                BorderDiagonalDown = full.BorderDiagonalDown,
                BorderDiagonalUp = full.BorderDiagonalUp,
            };

        if (parts.HasFlag(CustomCellStyleParts.Fill))
            diff = diff with
            {
                FillColor = full.FillColor,
                FillThemeColor = full.FillThemeColor,
                ClearFill = full.ClearFill,
                FillPatternStyle = full.FillPatternStyle,
                FillPatternColor = full.FillPatternColor,
                FillPatternThemeColor = full.FillPatternThemeColor,
                GradientFill = full.GradientFill,
            };

        if (parts.HasFlag(CustomCellStyleParts.Protection))
            diff = diff with { Locked = full.Locked, Hidden = full.Hidden };

        return diff;
    }

    /// <summary>
    /// Whether <paramref name="name"/> is usable for a new style: not blank, not too long, and not the
    /// name of one of the built-in styles (Excel never lets New Cell Style shadow a built-in).
    /// </summary>
    public static bool IsValidNewName(string? name, out string? error)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            error = "Enter a name for the style.";
            return false;
        }

        if (trimmed.Length > CustomCellStyle.MaxNameLength)
        {
            error = "The style name is too long.";
            return false;
        }

        foreach (var preset in Enum.GetValues<CellStylePreset>())
        {
            if (string.Equals(CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                error = "That name is already used by a built-in style.";
                return false;
            }
        }

        error = null;
        return true;
    }
}
