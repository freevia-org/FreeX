using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

/// <summary>
/// Portable, UI-free planning for the "Crop Picture" dialog shared by the desktop hosts and the cross-platform
/// shell: capturing a picture's four crop fractions as edit-box percentages, parsing the typed percents back to
/// fractions, and validating that the requested crop leaves a visible region (matching the Core
/// <c>SetPictureCropCommand</c> rule <c>Left + Right &lt; 1</c> and <c>Top + Bottom &lt; 1</c>). Building the
/// dialog and running the command stays with each shell. Named <c>PictureCropDialogPlanner</c> to stay distinct
/// from the drag-mode <c>PictureCropPlanner</c> in DrawingInteraction.
/// </summary>
public static class PictureCropDialogPlanner
{
    /// <summary>Largest accepted single-edge crop percentage (exclusive of 100%).</summary>
    public const double MaxEdgePercent = 100.0;

    public const string InvalidPercentMessage =
        "Enter crop percentages between 0 and 100 that leave a visible width and height.";

    public const string NotImageMessage = "Only inserted image pictures can be cropped.";

    /// <summary>The four crop edge fractions (0–1) seeded into / read back from the dialog.</summary>
    public sealed record CropValues(double Left, double Top, double Right, double Bottom, bool IsCroppable);

    /// <summary>The validated crop fractions handed to <c>SetPictureCropCommand</c>.</summary>
    public sealed record CropResult(double Left, double Top, double Right, double Bottom);

    /// <summary>Snapshots the picture's current crop fractions and whether it is an image (croppable).</summary>
    public static CropValues Capture(PictureModel picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return new CropValues(
            picture.CropLeft,
            picture.CropTop,
            picture.CropRight,
            picture.CropBottom,
            picture.Kind == PictureKind.Image);
    }

    /// <summary>Formats a crop fraction (0–1) as a percentage string for an edit box.</summary>
    public static string FormatPercent(double fraction) =>
        Math.Round(fraction * 100, 1).ToString("0.#", CultureInfo.CurrentCulture);

    /// <summary>Parses a single typed percentage (with optional trailing '%') into a 0–1 fraction.</summary>
    public static bool TryParsePercent(string? text, out double fraction)
    {
        fraction = 0;
        text = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
        if (!TryParseNumber(text, out var percent))
            return false;
        if (percent < 0 || percent >= MaxEdgePercent)
            return false;
        fraction = percent / 100.0;
        return true;
    }

    /// <summary>
    /// Validates the four typed crop percentages and produces a result. Each edge must parse to a 0–100%
    /// value and the opposing edges together must leave a visible region (left+right &lt; 1, top+bottom &lt; 1).
    /// </summary>
    public static bool TryCreateResult(
        string? leftText,
        string? topText,
        string? rightText,
        string? bottomText,
        out CropResult? result,
        out string? error)
    {
        result = null;
        error = null;

        if (!TryParsePercent(leftText, out var left) ||
            !TryParsePercent(topText, out var top) ||
            !TryParsePercent(rightText, out var right) ||
            !TryParsePercent(bottomText, out var bottom) ||
            left + right >= 1 ||
            top + bottom >= 1)
        {
            error = InvalidPercentMessage;
            return false;
        }

        result = new CropResult(left, top, right, bottom);
        return true;
    }

    /// <summary>Validates a comma- or semicolon-delimited left/top/right/bottom crop percentage list.</summary>
    public static bool TryCreateResult(string? input, out CropResult? result, out string? error)
    {
        result = null;
        error = null;
        var parts = (input ?? string.Empty).Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            ? TryCreateResult(parts[0], parts[1], parts[2], parts[3], out result, out error)
            : Invalid(out result, out error);
    }

    public static SetPictureCropCommand BuildCommand(
        SheetId sheetId,
        Guid pictureId,
        CropResult crop)
    {
        ArgumentNullException.ThrowIfNull(crop);
        return BuildCommand(sheetId, pictureId, crop.Left, crop.Top, crop.Right, crop.Bottom);
    }

    public static SetPictureCropCommand BuildCommand(
        SheetId sheetId,
        Guid pictureId,
        double left,
        double top,
        double right,
        double bottom) =>
        new(sheetId, pictureId, left, top, right, bottom);

    public static SetPictureCropCommand BuildResetCommand(SheetId sheetId, Guid pictureId) =>
        BuildCommand(sheetId, pictureId, 0, 0, 0, 0);

    private static bool Invalid(out CropResult? result, out string? error)
    {
        result = null;
        error = InvalidPercentMessage;
        return false;
    }

    private static bool TryParseNumber(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
            return true;
        value = 0;
        return false;
    }
}
