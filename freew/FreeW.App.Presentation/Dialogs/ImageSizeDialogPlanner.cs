using System.Globalization;

namespace FreeW.App.Presentation.Dialogs;

public enum ImageSizeDialogField
{
    Width,
    Height,
    LockAspectRatio
}

public sealed record ImageSizeDialogInitialState(
    string WidthText,
    string HeightText,
    double AspectRatio,
    bool LockAspectRatio);

public sealed record ImageSizeDialogInput(
    string? WidthText,
    string? HeightText);

public sealed record ImageSizeValidation(
    ImageSizeDialogField Field,
    string Message);

public sealed record ImageSizeDialogResult(
    double Width,
    double Height);

public static class ImageSizeDialogPlanner
{
    public const string DefaultTitle = "Image Size";
    public const string PositiveSizeValidationMessage =
        "Enter positive values for both width and height (in points).";

    public static DialogSurfaceSpec<ImageSizeDialogField> Surface { get; } = new(
        Title: DefaultTitle,
        AutomationId: "ImageSizeDialog",
        AutomationName: "Image Size",
        Fields:
        [
            new(ImageSizeDialogField.Width, "Width (pt):", "ImageSizeWidthTextBox", "Image width"),
            new(ImageSizeDialogField.Height, "Height (pt):", "ImageSizeHeightTextBox", "Image height"),
            new(ImageSizeDialogField.LockAspectRatio, "Lock aspect ratio", "ImageSizeLockAspectRatioCheckBox", "Lock aspect ratio"),
        ],
        ValidationAutomationId: "ImageSizeValidationText");

    public static ImageSizeDialogInitialState BuildInitialState(
        double currentWidthPt,
        double currentHeightPt,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new ImageSizeDialogInitialState(
            WidthText: FormatPoints(currentWidthPt, culture),
            HeightText: FormatPoints(currentHeightPt, culture),
            AspectRatio: CalculateAspectRatio(currentWidthPt, currentHeightPt),
            LockAspectRatio: true);
    }

    public static bool TryBuildResult(
        ImageSizeDialogInput input,
        CultureInfo culture,
        out ImageSizeDialogResult? result,
        out ImageSizeValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParsePositive(input.WidthText, culture, out var width))
        {
            validation = new ImageSizeValidation(ImageSizeDialogField.Width, PositiveSizeValidationMessage);
            return false;
        }

        if (!TryParsePositive(input.HeightText, culture, out var height))
        {
            validation = new ImageSizeValidation(ImageSizeDialogField.Height, PositiveSizeValidationMessage);
            return false;
        }

        result = new ImageSizeDialogResult(width, height);
        return true;
    }

    public static bool TryBuildLockedHeightText(
        string? widthText,
        double aspectRatio,
        bool lockAspectRatio,
        CultureInfo culture,
        out string? heightText)
    {
        ArgumentNullException.ThrowIfNull(culture);

        heightText = null;
        if (!lockAspectRatio || !TryParsePositive(widthText, culture, out var width))
            return false;

        heightText = FormatPoints(width * aspectRatio, culture);
        return true;
    }

    public static bool TryBuildLockedWidthText(
        string? heightText,
        double aspectRatio,
        bool lockAspectRatio,
        CultureInfo culture,
        out string? widthText)
    {
        ArgumentNullException.ThrowIfNull(culture);

        widthText = null;
        if (!lockAspectRatio
            || aspectRatio <= 0
            || !TryParsePositive(heightText, culture, out var height))
        {
            return false;
        }

        widthText = FormatPoints(height / aspectRatio, culture);
        return true;
    }

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static double CalculateAspectRatio(double width, double height) =>
        width > 0 ? height / width : 1.0;

    private static bool TryParsePositive(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value) && value > 0;
    }
}
