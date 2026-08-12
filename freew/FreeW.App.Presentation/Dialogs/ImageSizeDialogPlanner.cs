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
            WidthText: DialogNumericTextPolicy.FormatPoints(currentWidthPt, culture),
            HeightText: DialogNumericTextPolicy.FormatPoints(currentHeightPt, culture),
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

        if (!DialogNumericTextPolicy.TryParsePositiveDouble(input.WidthText, culture, out var width))
        {
            validation = new ImageSizeValidation(ImageSizeDialogField.Width, PositiveSizeValidationMessage);
            return false;
        }

        if (!DialogNumericTextPolicy.TryParsePositiveDouble(input.HeightText, culture, out var height))
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
        if (!lockAspectRatio || !DialogNumericTextPolicy.TryParsePositiveDouble(widthText, culture, out var width))
            return false;

        heightText = DialogNumericTextPolicy.FormatPoints(width * aspectRatio, culture);
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
            || !DialogNumericTextPolicy.TryParsePositiveDouble(heightText, culture, out var height))
        {
            return false;
        }

        widthText = DialogNumericTextPolicy.FormatPoints(height / aspectRatio, culture);
        return true;
    }

    private static double CalculateAspectRatio(double width, double height) =>
        width > 0 ? height / width : 1.0;

}
