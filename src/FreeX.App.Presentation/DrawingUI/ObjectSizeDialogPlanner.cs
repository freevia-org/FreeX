using System.Globalization;

namespace FreeX.App.Presentation.DrawingUI;

public enum ObjectSizeDialogField
{
    Width,
    Height
}

public readonly record struct ObjectSizeDialogSize(double Width, double Height);

public readonly record struct ObjectSizeDialogSubmission(
    string? WidthText,
    string? HeightText,
    ObjectSizeDialogField FirstInvalidField);

public readonly record struct ObjectSizeDialogState(
    string WidthText,
    string HeightText,
    ObjectSizeDialogSize OriginalSize,
    ObjectSizeDialogField InitialFocusField,
    ObjectSizeDialogField FirstInvalidField,
    bool LockAspectRatio);

public static class ObjectSizeDialogPlanner
{
    public const double MinimumSize = FormatPicturePlanner.MinimumSize;

    public static ObjectSizeDialogState CreateState(
        double width,
        double height,
        ObjectSizeDialogField initialFocusField,
        ObjectSizeDialogField firstInvalidField,
        CultureInfo? culture = null)
    {
        var originalSize = NormalizeSize(width, height);
        return new ObjectSizeDialogState(
            FormatSize(originalSize.Width, culture),
            FormatSize(originalSize.Height, culture),
            originalSize,
            initialFocusField,
            firstInvalidField,
            LockAspectRatio: true);
    }

    public static ObjectSizeDialogSize NormalizeSize(double width, double height) =>
        new(NormalizeSizeComponent(width), NormalizeSizeComponent(height));

    public static double NormalizeSizeComponent(double value) =>
        double.IsFinite(value) && value >= MinimumSize ? value : MinimumSize;

    public static string FormatSize(double value, CultureInfo? culture = null) =>
        Math.Round(NormalizeSizeComponent(value), 2).ToString("0.##", culture ?? CultureInfo.CurrentCulture);

    public static bool TryCreateSize(
        ObjectSizeDialogSubmission submission,
        out ObjectSizeDialogSize result,
        out ObjectSizeDialogField invalidField) =>
        TryCreateSize(
            submission.WidthText,
            submission.HeightText,
            submission.FirstInvalidField,
            out result,
            out invalidField);

    public static bool TryCreateSize(
        string? widthText,
        string? heightText,
        ObjectSizeDialogField firstInvalidField,
        out ObjectSizeDialogSize result,
        out ObjectSizeDialogField invalidField)
    {
        if (TryParsePositiveSize(widthText, out var width) &&
            TryParsePositiveSize(heightText, out var height))
        {
            result = new ObjectSizeDialogSize(width, height);
            invalidField = firstInvalidField;
            return true;
        }

        result = default;
        invalidField = ResolveInvalidSizeField(widthText, heightText, firstInvalidField);
        return false;
    }

    public static bool TryCreateDelimitedSize(
        string? sizeText,
        out ObjectSizeDialogSize result,
        out ObjectSizeDialogField invalidField)
    {
        var parts = (sizeText ?? string.Empty).Split(
            'x',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
        {
            result = default;
            invalidField = ObjectSizeDialogField.Width;
            return false;
        }

        return TryCreateSize(parts[0], parts[1], ObjectSizeDialogField.Width, out result, out invalidField);
    }

    public static ObjectSizeDialogField ResolveInvalidSizeField(
        string? widthText,
        string? heightText,
        ObjectSizeDialogField firstInvalidField)
    {
        if (firstInvalidField == ObjectSizeDialogField.Height)
        {
            return !IsValidSizeComponent(heightText)
                ? ObjectSizeDialogField.Height
                : ObjectSizeDialogField.Width;
        }

        return !IsValidSizeComponent(widthText)
            ? ObjectSizeDialogField.Width
            : ObjectSizeDialogField.Height;
    }

    public static bool TryParsePositiveSize(string? text, out double value)
    {
        if (FormatPicturePlanner.TryParseNumber(text, out value) && value >= MinimumSize)
            return true;

        value = 0;
        return false;
    }

    public static double? SyncHeightFromWidth(string? widthText, ObjectSizeDialogSize originalSize) =>
        FormatPicturePlanner.SyncHeightFromWidth(widthText, AspectRatio(originalSize));

    public static double? SyncWidthFromHeight(string? heightText, ObjectSizeDialogSize originalSize) =>
        FormatPicturePlanner.SyncWidthFromHeight(heightText, AspectRatio(originalSize));

    public static double? SyncHeightFromWidth(double width, double originalWidth, double originalHeight) =>
        FormatPicturePlanner.SyncHeightFromWidth(width, AspectRatio(originalWidth, originalHeight));

    public static double? SyncWidthFromHeight(double height, double originalWidth, double originalHeight) =>
        FormatPicturePlanner.SyncWidthFromHeight(height, AspectRatio(originalWidth, originalHeight));

    public static double CalculateLockedAspectHeight(double width, double originalWidth, double originalHeight) =>
        SyncHeightFromWidth(width, originalWidth, originalHeight) ?? width;

    public static double CalculateLockedAspectWidth(double height, double originalWidth, double originalHeight) =>
        SyncWidthFromHeight(height, originalWidth, originalHeight) ?? height;

    public static double AspectRatio(ObjectSizeDialogSize originalSize) =>
        AspectRatio(originalSize.Width, originalSize.Height);

    public static double AspectRatio(double originalWidth, double originalHeight) =>
        originalWidth > 0 && originalHeight > 0
            ? FormatPicturePlanner.AspectRatio(originalWidth, originalHeight)
            : 0;

    private static bool IsValidSizeComponent(string? text) =>
        TryParsePositiveSize(text, out _);
}
