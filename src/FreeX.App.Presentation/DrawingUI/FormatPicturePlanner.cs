using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

/// <summary>
/// Portable, UI-free planning for the "Format Picture" / "Format Shape" dialog shared by the desktop hosts and
/// the cross-platform shell. Covers the size (width/height with optional lock-aspect-ratio syncing), rotation,
/// and alt-text fields that both pictures and drawing shapes expose. Input parsing, validation, and the
/// aspect-ratio math live here so the behavior is single-sourced; building the dialog and running the Core
/// commands stays with each shell's command glue.
/// </summary>
public static class FormatPicturePlanner
{
    /// <summary>Smallest accepted width/height, in DIP, so an object never collapses to nothing.</summary>
    public const double MinimumSize = 1.0;

    public const string InvalidSizeMessage = "Enter positive numbers for width and height.";
    public const string InvalidRotationMessage = "Enter a valid rotation in degrees.";

    public enum FormatObjectDialogField
    {
        Width,
        Height,
        Rotation
    }

    public enum FormatPictureDialogValidationError
    {
        Size,
        Rotation,
        Crop
    }

    /// <summary>The fields seeded into / read back from the dialog.</summary>
    public sealed record FormatObjectValues(
        double Width,
        double Height,
        double RotationDegrees,
        bool LockAspectRatio,
        bool LockAspectRatioSupported,
        string AltText);

    /// <summary>Portable initial edit-box state for the combined size / rotation / alt-text dialog.</summary>
    public sealed record FormatObjectDialogState(
        string WidthText,
        string HeightText,
        string RotationText,
        double AspectRatio,
        bool LockAspectRatio,
        bool LockAspectRatioSupported,
        string AltText);

    /// <summary>Portable submission payload for the combined size / rotation / alt-text dialog.</summary>
    public sealed record FormatObjectSubmission(
        string? WidthText,
        string? HeightText,
        string? RotationText,
        bool LockAspectRatio,
        string? AltText);

    /// <summary>The validated result the shell hands to the resize / rotate / alt-text commands.</summary>
    public sealed record FormatObjectResult(
        double Width,
        double Height,
        double RotationDegrees,
        bool LockAspectRatio,
        string? AltText);

    /// <summary>The validated picture-format result, including the crop tab fields used only by pictures.</summary>
    public sealed record PictureFormatResult(
        FormatObjectResult Format,
        PictureCropDialogPlanner.CropResult Crop);

    /// <summary>The validated size fields used by the standalone object-size dialog.</summary>
    public sealed record SizeResult(double Width, double Height);

    /// <summary>The validated, normalized rotation used by standalone and combined format dialogs.</summary>
    public sealed record RotationResult(double Degrees);

    /// <summary>Snapshots a picture's editable fields. Pictures support locking the aspect ratio.</summary>
    public static FormatObjectValues Capture(PictureModel picture)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return new FormatObjectValues(
            picture.Width,
            picture.Height,
            picture.RotationDegrees,
            picture.LockAspectRatio,
            LockAspectRatioSupported: true,
            picture.AltText ?? string.Empty);
    }

    /// <summary>Snapshots a drawing shape's editable fields. Shapes have no lock-aspect-ratio flag.</summary>
    public static FormatObjectValues Capture(DrawingShapeModel shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return new FormatObjectValues(
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            LockAspectRatio: false,
            LockAspectRatioSupported: false,
            shape.AltText ?? string.Empty);
    }

    /// <summary>Snapshots a text box's editable object-format fields. Text boxes have no lock-aspect-ratio flag.</summary>
    public static FormatObjectValues Capture(TextBoxModel textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        return new FormatObjectValues(
            textBox.Width,
            textBox.Height,
            textBox.RotationDegrees,
            LockAspectRatio: false,
            LockAspectRatioSupported: false,
            textBox.AltText ?? string.Empty);
    }

    /// <summary>Creates the initial UI-free state for a host-owned format-object dialog.</summary>
    public static FormatObjectDialogState CreateDialogState(FormatObjectValues values, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new FormatObjectDialogState(
            FormatSize(values.Width, culture),
            FormatSize(values.Height, culture),
            FormatRotation(values.RotationDegrees, culture),
            AspectRatio(values.Width, values.Height),
            values.LockAspectRatio,
            values.LockAspectRatioSupported,
            values.AltText);
    }

    /// <summary>Creates the initial state for the WPF Format Picture dialog, including its crop tab values.</summary>
    public static (FormatObjectDialogState Format, PictureCropDialogPlanner.CropValues Crop) CreatePictureDialogState(
        PictureModel picture,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(picture);
        return (CreateDialogState(Capture(picture), culture), PictureCropDialogPlanner.Capture(picture));
    }

    /// <summary>
    /// The aspect ratio (width / height) used to keep the two size boxes in sync while locked; returns a
    /// non-positive value when it cannot be computed (height is zero), which the syncing helpers treat as
    /// "do not sync".
    /// </summary>
    public static double AspectRatio(double width, double height) =>
        height > 0 ? width / height : 0;

    /// <summary>Formats a size component for an input box, using the current culture.</summary>
    public static string FormatSize(double value, CultureInfo? culture = null) =>
        value.ToString("0.###", culture ?? CultureInfo.CurrentCulture);

    /// <summary>Formats a rotation for an input box, using the current culture.</summary>
    public static string FormatRotation(double value, CultureInfo? culture = null) =>
        value.ToString("0.##", culture ?? CultureInfo.CurrentCulture);

    /// <summary>Parses a single size/rotation component (current culture, with invariant fallback).</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        text = text?.Trim() ?? string.Empty;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value))
            return true;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
            return true;
        value = 0;
        return false;
    }

    /// <summary>Validates width and height text from separate input boxes.</summary>
    public static bool TryCreateSizeResult(string? widthText, string? heightText, out SizeResult? result)
    {
        result = null;
        if (!TryParseNumber(widthText, out var width) ||
            !TryParseNumber(heightText, out var height) ||
            width < MinimumSize ||
            height < MinimumSize)
        {
            return false;
        }

        result = new SizeResult(width, height);
        return true;
    }

    /// <summary>Validates width-by-height text such as "320 x 180".</summary>
    public static bool TryCreateSizeResult(string? sizeText, out SizeResult? result)
    {
        result = null;
        var parts = (sizeText ?? string.Empty).Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && TryCreateSizeResult(parts[0], parts[1], out result);
    }

    /// <summary>Validates and normalizes rotation to the 0..360 degree range used by drawing commands.</summary>
    public static bool TryCreateRotationResult(string? rotationText, out RotationResult? result)
    {
        result = null;
        if (!TryParseNumber(rotationText, out var rotation))
            return false;

        result = new RotationResult(NormalizeRotationDegrees(rotation));
        return true;
    }

    public static FormatObjectDialogField ResolveInvalidField(
        string? widthText,
        string? heightText,
        string? rotationText,
        FormatObjectDialogField firstInvalidSizeField = FormatObjectDialogField.Height)
    {
        if (!TryCreateSizeResult(widthText, heightText, out _))
        {
            if (firstInvalidSizeField == FormatObjectDialogField.Height)
            {
                return !TryCreateSizeResult(heightText, heightText, out _)
                    ? FormatObjectDialogField.Height
                    : FormatObjectDialogField.Width;
            }

            return !TryCreateSizeResult(widthText, widthText, out _)
                ? FormatObjectDialogField.Width
                : FormatObjectDialogField.Height;
        }

        return !TryCreateRotationResult(rotationText, out _)
            ? FormatObjectDialogField.Rotation
            : firstInvalidSizeField;
    }

    /// <summary>Normalizes arbitrary finite degrees to the Excel-style 0..360 range.</summary>
    public static double NormalizeRotationDegrees(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    /// <summary>
    /// Given a new width while the aspect ratio is locked, returns the height that preserves
    /// <paramref name="aspectRatio"/>, or null when no sync should happen.
    /// </summary>
    public static double? SyncHeightFromWidth(string? widthText, double aspectRatio)
    {
        if (aspectRatio <= 0)
            return null;
        if (!TryParseNumber(widthText, out var width) || width <= 0)
            return null;
        return SyncHeightFromWidth(width, aspectRatio);
    }

    /// <summary>Numeric equivalent of <see cref="SyncHeightFromWidth(string?, double)"/>.</summary>
    public static double? SyncHeightFromWidth(double width, double aspectRatio) =>
        aspectRatio > 0 && width > 0 && double.IsFinite(width)
            ? width / aspectRatio
            : null;

    /// <summary>
    /// Given a new height while the aspect ratio is locked, returns the width that preserves
    /// <paramref name="aspectRatio"/>, or null when no sync should happen.
    /// </summary>
    public static double? SyncWidthFromHeight(string? heightText, double aspectRatio)
    {
        if (aspectRatio <= 0)
            return null;
        if (!TryParseNumber(heightText, out var height) || height <= 0)
            return null;
        return SyncWidthFromHeight(height, aspectRatio);
    }

    /// <summary>Numeric equivalent of <see cref="SyncWidthFromHeight(string?, double)"/>.</summary>
    public static double? SyncWidthFromHeight(double height, double aspectRatio) =>
        aspectRatio > 0 && height > 0 && double.IsFinite(height)
            ? height * aspectRatio
            : null;

    /// <summary>
    /// Validates the typed size / rotation / alt-text and produces the result the shell applies. Width and
    /// height must be finite and at least <see cref="MinimumSize"/>; rotation must be a finite number.
    /// </summary>
    public static bool TryCreateResult(
        FormatObjectSubmission submission,
        out FormatObjectResult? result,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return TryCreateResult(
            submission.WidthText,
            submission.HeightText,
            submission.RotationText,
            submission.LockAspectRatio,
            submission.AltText,
            out result,
            out error);
    }

    /// <summary>
    /// Validates the typed Format Picture fields and crop-tab fields in one shared path. Hosts map the error code
    /// to localized text/focus behavior.
    /// </summary>
    public static bool TryCreatePictureResult(
        FormatObjectSubmission submission,
        string? cropInput,
        out PictureFormatResult? result,
        out FormatPictureDialogValidationError? error)
    {
        result = null;
        error = null;

        if (!TryCreateResult(submission, out var format, out var formatError) || format is null)
        {
            error = string.Equals(formatError, InvalidRotationMessage, StringComparison.Ordinal)
                ? FormatPictureDialogValidationError.Rotation
                : FormatPictureDialogValidationError.Size;
            return false;
        }

        if (!PictureCropDialogPlanner.TryCreateResult(cropInput, out var crop, out _) || crop is null)
        {
            error = FormatPictureDialogValidationError.Crop;
            return false;
        }

        result = new PictureFormatResult(format, crop);
        return true;
    }

    public static bool TryCreatePictureResult(
        string? sizeText,
        string? rotationText,
        bool lockAspectRatio,
        string? cropInput,
        string? altText,
        out PictureFormatResult? result,
        out FormatPictureDialogValidationError? error)
    {
        var parts = (sizeText ?? string.Empty).Split(
            'x',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            result = null;
            error = FormatPictureDialogValidationError.Size;
            return false;
        }

        return TryCreatePictureResult(
            new FormatObjectSubmission(parts[0], parts[1], rotationText, lockAspectRatio, altText),
            cropInput,
            out result,
            out error);
    }

    /// <summary>Normalizes host-entered alt text to the command model's null-or-trimmed convention.</summary>
    public static string? NormalizeAltText(string? altText) =>
        string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();

    public static bool TryCreateResult(
        string? widthText,
        string? heightText,
        string? rotationText,
        bool lockAspectRatio,
        string? altText,
        out FormatObjectResult? result,
        out string? error)
    {
        result = null;
        error = null;

        if (!TryCreateSizeResult(widthText, heightText, out var size) || size is null)
        {
            error = InvalidSizeMessage;
            return false;
        }

        if (!TryCreateRotationResult(rotationText, out var rotation) || rotation is null)
        {
            error = InvalidRotationMessage;
            return false;
        }

        var normalizedAlt = NormalizeAltText(altText);
        result = new FormatObjectResult(size.Width, size.Height, rotation.Degrees, lockAspectRatio, normalizedAlt);
        return true;
    }
}
