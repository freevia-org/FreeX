using System.Globalization;

namespace FreeW.App.Presentation.Dialogs;

public enum ImageAdjustDialogField
{
    Brightness,
    Contrast,
    Saturation,
    Transparency
}

public sealed record ImageAdjustDialogInitialState(
    string BrightnessText,
    string ContrastText,
    string SaturationText,
    string TransparencyText);

public sealed record ImageAdjustDialogInput(
    string? BrightnessText,
    string? ContrastText,
    string? SaturationText,
    string? TransparencyText);

public sealed record ImageAdjustValidation(
    ImageAdjustDialogField Field,
    string Message);

public sealed record ImageAdjustDialogResult(
    double Brightness,
    double Contrast,
    double Saturation,
    double Transparency);

public static class ImageAdjustDialogPlanner
{
    public const string BrightnessValidationMessage = "Brightness must be a number between -100 and 100.";
    public const string ContrastValidationMessage = "Contrast must be a number between -100 and 100.";
    public const string SaturationValidationMessage = "Saturation must be a number between 0 and 400.";
    public const string TransparencyValidationMessage = "Transparency must be a number between 0 and 100.";

    public static DialogSurfaceSpec<ImageAdjustDialogField> DetailedSurface { get; } = new(
        Title: "Picture Corrections and Color",
        AutomationId: "ImageAdjustDialog",
        AutomationName: "Picture Corrections and Color",
        Fields:
        [
            new(ImageAdjustDialogField.Brightness, "Brightness (-100 to +100):", "ImageAdjustBrightnessTextBox", "Picture brightness"),
            new(ImageAdjustDialogField.Contrast, "Contrast (-100 to +100):", "ImageAdjustContrastTextBox", "Picture contrast"),
            new(ImageAdjustDialogField.Saturation, "Saturation (0\u2013400, 100=normal):", "ImageAdjustSaturationTextBox", "Picture saturation"),
            new(ImageAdjustDialogField.Transparency, "Transparency (0\u2013100):", "ImageAdjustTransparencyTextBox", "Picture transparency"),
        ],
        ValidationAutomationId: "ImageAdjustValidationText");

    public static DialogSurfaceSpec<ImageAdjustDialogField> CompactSurface { get; } = new(
        Title: "Picture Corrections",
        AutomationId: DetailedSurface.AutomationId,
        AutomationName: "Picture Corrections",
        Fields:
        [
            new(ImageAdjustDialogField.Brightness, "Brightness (-100 to 100):", "ImageAdjustBrightnessTextBox", "Picture brightness"),
            new(ImageAdjustDialogField.Contrast, "Contrast (-100 to 100):", "ImageAdjustContrastTextBox", "Picture contrast"),
            new(ImageAdjustDialogField.Saturation, "Saturation (0 to 400):", "ImageAdjustSaturationTextBox", "Picture saturation"),
            new(ImageAdjustDialogField.Transparency, "Transparency (0 to 100):", "ImageAdjustTransparencyTextBox", "Picture transparency"),
        ],
        ValidationAutomationId: DetailedSurface.ValidationAutomationId);

    public static ImageAdjustDialogInitialState BuildInitialState(
        double brightnessPct,
        double contrastPct,
        double saturationPct,
        double transparencyPct,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new ImageAdjustDialogInitialState(
            FormatPercent(brightnessPct, culture),
            FormatPercent(contrastPct, culture),
            FormatPercent(saturationPct, culture),
            FormatPercent(transparencyPct, culture));
    }

    public static bool TryBuildResult(
        ImageAdjustDialogInput input,
        CultureInfo culture,
        out ImageAdjustDialogResult? result,
        out ImageAdjustValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParseRange(input.BrightnessText, -100, 100, culture, out var brightness))
        {
            validation = new ImageAdjustValidation(
                ImageAdjustDialogField.Brightness,
                BrightnessValidationMessage);
            return false;
        }

        if (!TryParseRange(input.ContrastText, -100, 100, culture, out var contrast))
        {
            validation = new ImageAdjustValidation(
                ImageAdjustDialogField.Contrast,
                ContrastValidationMessage);
            return false;
        }

        if (!TryParseRange(input.SaturationText, 0, 400, culture, out var saturation))
        {
            validation = new ImageAdjustValidation(
                ImageAdjustDialogField.Saturation,
                SaturationValidationMessage);
            return false;
        }

        if (!TryParseRange(input.TransparencyText, 0, 100, culture, out var transparency))
        {
            validation = new ImageAdjustValidation(
                ImageAdjustDialogField.Transparency,
                TransparencyValidationMessage);
            return false;
        }

        result = new ImageAdjustDialogResult(brightness, contrast, saturation, transparency);
        return true;
    }

    public static string FormatPercent(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static bool TryParseRange(
        string? text,
        double min,
        double max,
        CultureInfo culture,
        out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value)
            && value >= min
            && value <= max;
    }
}
