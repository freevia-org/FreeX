using System.Globalization;
using FreeW.App.Localization;

namespace FreeW.App.Presentation.Dialogs;

public enum ImageCropDialogField
{
    Left,
    Right,
    Top,
    Bottom,
    Totals
}

public sealed record ImageCropDialogInitialState(
    string LeftText,
    string RightText,
    string TopText,
    string BottomText);

public sealed record ImageCropDialogInput(
    string? LeftText,
    string? RightText,
    string? TopText,
    string? BottomText);

public sealed record ImageCropValidation(
    ImageCropDialogField Field,
    string Message);

public sealed record ImageCropDialogResult(
    double Left,
    double Right,
    double Top,
    double Bottom);

public static class ImageCropDialogPlanner
{
    public const string PercentageValidationMessage =
        "Each crop value must be a percentage between 0 and 99.";
    public const string TotalsValidationMessage =
        "Left + Right and Top + Bottom must each total less than 100%.";

    public static string Instruction => Loc.Get("ImageCrop_Instruction");

    public static DialogSurfaceSpec<ImageCropDialogField> Surface { get; } = new(
        Title: "Crop Picture",
        AutomationId: "ImageCropDialog",
        AutomationName: "Crop Picture",
        Fields:
        [
            new(ImageCropDialogField.Left, "Left (%):", "ImageCropLeftTextBox", "Left crop percentage"),
            new(ImageCropDialogField.Right, "Right (%):", "ImageCropRightTextBox", "Right crop percentage"),
            new(ImageCropDialogField.Top, "Top (%):", "ImageCropTopTextBox", "Top crop percentage"),
            new(ImageCropDialogField.Bottom, "Bottom (%):", "ImageCropBottomTextBox", "Bottom crop percentage"),
        ],
        ValidationAutomationId: "ImageCropValidationText");

    public static ImageCropDialogInitialState BuildInitialState(
        double left,
        double right,
        double top,
        double bottom,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new ImageCropDialogInitialState(
            FormatFractionAsPercent(left, culture),
            FormatFractionAsPercent(right, culture),
            FormatFractionAsPercent(top, culture),
            FormatFractionAsPercent(bottom, culture));
    }

    public static bool TryBuildResult(
        ImageCropDialogInput input,
        CultureInfo culture,
        out ImageCropDialogResult? result,
        out ImageCropValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParsePercentFraction(input.LeftText, culture, out var left))
        {
            validation = new ImageCropValidation(ImageCropDialogField.Left, PercentageValidationMessage);
            return false;
        }

        if (!TryParsePercentFraction(input.RightText, culture, out var right))
        {
            validation = new ImageCropValidation(ImageCropDialogField.Right, PercentageValidationMessage);
            return false;
        }

        if (!TryParsePercentFraction(input.TopText, culture, out var top))
        {
            validation = new ImageCropValidation(ImageCropDialogField.Top, PercentageValidationMessage);
            return false;
        }

        if (!TryParsePercentFraction(input.BottomText, culture, out var bottom))
        {
            validation = new ImageCropValidation(ImageCropDialogField.Bottom, PercentageValidationMessage);
            return false;
        }

        if (left + right >= 1.0 || top + bottom >= 1.0)
        {
            validation = new ImageCropValidation(ImageCropDialogField.Totals, TotalsValidationMessage);
            return false;
        }

        result = new ImageCropDialogResult(left, right, top, bottom);
        return true;
    }

    public static string FormatFractionAsPercent(double fraction, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return (fraction * 100).ToString("0.#", culture);
    }

    private static bool TryParsePercentFraction(string? text, CultureInfo culture, out double fraction)
    {
        fraction = 0;
        var trimmed = (text ?? string.Empty).Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, culture, out var percent))
            return false;

        if (percent < 0 || percent >= 100)
            return false;

        fraction = percent / 100.0;
        return true;
    }
}
