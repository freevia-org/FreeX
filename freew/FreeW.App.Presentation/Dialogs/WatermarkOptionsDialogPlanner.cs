using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum WatermarkDialogValidationTarget
{
    Text,
    Color,
    Image,
    Scale
}

public sealed record WatermarkOptionsDialogInitialState(
    bool IsPicture,
    string Text,
    string FontFamily,
    string FontColorHex,
    bool TextIsHorizontal,
    bool TextIsSemitransparent,
    string PicturePathText,
    string ScaleText,
    bool PictureIsHorizontal,
    bool PictureWashout);

public sealed record WatermarkTextDialogInput(
    string? Text,
    string? FontFamily,
    string? ColorText,
    bool IsHorizontal,
    bool IsSemitransparent);

public sealed record WatermarkPictureDialogInput(
    byte[]? ImageBytes,
    string? ScaleText,
    bool IsHorizontal,
    bool IsWashout);

public sealed record WatermarkOptionsDialogValidation(
    WatermarkDialogValidationTarget Target,
    string Message);

public sealed record WatermarkImageImportPlan(
    byte[] ImageBytes,
    string DisplayLabel);

public enum WatermarkDialogMode
{
    Text,
    Picture,
}

public sealed record WatermarkOptionsDialogSubmission(
    string? Text,
    string? FontFamily,
    string? ColorText,
    bool TextIsHorizontal,
    bool TextIsSemitransparent,
    string? PictureScaleText,
    bool PictureIsHorizontal,
    bool PictureIsWashout);

public sealed record WatermarkOptionsDialogAcceptance(
    bool IsAccepted,
    WatermarkOptions? Result = null,
    WatermarkOptionsDialogValidation? Validation = null,
    bool RemoveRequested = false);

public sealed class WatermarkOptionsDialogSession
{
    private readonly CultureInfo _culture;
    private byte[]? _pendingImageBytes;

    public WatermarkOptionsDialogSession(WatermarkOptions? current, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        _pendingImageBytes = current?.ImageBytes;
        InitialState = WatermarkOptionsDialogPlanner.BuildInitialState(current, culture);
        Mode = InitialState.IsPicture ? WatermarkDialogMode.Picture : WatermarkDialogMode.Text;
    }

    public WatermarkOptionsDialogInitialState InitialState { get; }

    public WatermarkDialogMode Mode { get; private set; }

    public void SelectMode(WatermarkDialogMode mode) => Mode = mode;

    public WatermarkImageImportPlan ImportImage(string? fileName, byte[] imageBytes)
    {
        var plan = WatermarkOptionsDialogPlanner.BuildImageImportPlan(fileName, imageBytes);
        _pendingImageBytes = plan.ImageBytes;
        return plan;
    }

    public WatermarkOptionsDialogAcceptance Submit(WatermarkOptionsDialogSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        WatermarkOptions? result;
        WatermarkOptionsDialogValidation? validation;
        var accepted = Mode == WatermarkDialogMode.Picture
            ? WatermarkOptionsDialogPlanner.TryBuildPictureResult(
                new WatermarkPictureDialogInput(
                    _pendingImageBytes,
                    submission.PictureScaleText,
                    submission.PictureIsHorizontal,
                    submission.PictureIsWashout),
                _culture,
                out result,
                out validation)
            : WatermarkOptionsDialogPlanner.TryBuildTextResult(
                new WatermarkTextDialogInput(
                    submission.Text,
                    submission.FontFamily,
                    submission.ColorText,
                    submission.TextIsHorizontal,
                    submission.TextIsSemitransparent),
                out result,
                out validation);

        return new WatermarkOptionsDialogAcceptance(accepted, result, validation);
    }

    public WatermarkOptionsDialogAcceptance Remove() =>
        new(IsAccepted: true, RemoveRequested: true);
}

public static class WatermarkOptionsDialogPlanner
{
    public const string Title = "Printed Watermark";
    public const string TextModeLabel = "Text watermark";
    public const string PictureModeLabel = "Picture watermark";
    public const string TextLabel = "Text:";
    public const string FontLabel = "Font:";
    public const string ColorLabel = "Color (hex):";
    public const string LayoutLabel = "Layout:";
    public const string DiagonalLabel = "Diagonal";
    public const string HorizontalLabel = "Horizontal";
    public const string SemitransparentLabel = "Semitransparent";
    public const string ImageFileLabel = "Image file:";
    public const string ScaleLabel = "Scale (%, 0=Auto):";
    public const string SelectPictureButton = "Select Picture\u2026";
    public const string SelectWatermarkImageTitle = "Select a watermark image";
    public const string WatermarkImageFilter =
        "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*";
    public const string WashoutLabel = "Washout (semitransparent)";
    public const string OkButton = "OK";
    public const string RemoveWatermarkButton = "Remove Watermark";
    public const string CancelButton = "Cancel";
    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new(OkButton, IsDefault: true),
        new(RemoveWatermarkButton),
        new(CancelButton, IsCancel: true),
    ];
    public const string DefaultText = "DRAFT";
    public const string DefaultFontFamily = "Calibri";
    public const string DefaultFontColorHex = "#808080";
    public const string DefaultPicturePathText = "(choose an image file...)";
    public const string TextValidationMessage = "Enter watermark text, or click 'Remove Watermark' to clear.";
    public const string ColorValidationMessage = "Enter a valid colour hex value (e.g. #808080).";
    public const string ImageValidationMessage = "Select an image file for the picture watermark.";
    public const string ScaleValidationMessage = "Scale must be 0 (Auto) or 1-500.";
    public const string ImageReadFailurePrefix = "Could not read image file:";

    public static WatermarkOptionsDialogInitialState BuildInitialState(
        WatermarkOptions? current,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var isPicture = current?.IsPicture ?? false;
        var seed = current ?? new WatermarkOptions(DefaultText);
        var imageBytes = current?.ImageBytes;

        return new WatermarkOptionsDialogInitialState(
            IsPicture: isPicture,
            Text: isPicture ? DefaultText : seed.Text,
            FontFamily: string.IsNullOrWhiteSpace(seed.FontFamily) ? DefaultFontFamily : seed.FontFamily,
            FontColorHex: string.IsNullOrWhiteSpace(seed.FontColorHex) ? DefaultFontColorHex : seed.FontColorHex,
            TextIsHorizontal: seed.Layout == WatermarkLayout.Horizontal,
            TextIsSemitransparent: seed.Opacity < 1.0,
            PicturePathText: FormatLoadedImageLabel(imageBytes),
            ScaleText: (current?.ScalePct ?? 0).ToString(culture),
            PictureIsHorizontal: seed.Layout == WatermarkLayout.Horizontal,
            PictureWashout: isPicture ? seed.Opacity < 1.0 : true);
    }

    public static bool TryBuildTextResult(
        WatermarkTextDialogInput input,
        out WatermarkOptions? result,
        out WatermarkOptionsDialogValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);

        result = null;
        validation = null;

        var text = (input.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            validation = new WatermarkOptionsDialogValidation(
                WatermarkDialogValidationTarget.Text,
                TextValidationMessage);
            return false;
        }

        var font = (input.FontFamily ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(font))
            font = DefaultFontFamily;

        if (!TryNormalizeColorHex(input.ColorText, out var color))
        {
            validation = new WatermarkOptionsDialogValidation(
                WatermarkDialogValidationTarget.Color,
                ColorValidationMessage);
            return false;
        }

        result = new WatermarkOptions(text)
        {
            FontFamily = font,
            FontColorHex = color,
            Layout = ToLayout(input.IsHorizontal),
            Opacity = ToOpacity(input.IsSemitransparent),
        };
        return true;
    }

    public static bool TryBuildPictureResult(
        WatermarkPictureDialogInput input,
        CultureInfo culture,
        out WatermarkOptions? result,
        out WatermarkOptionsDialogValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (input.ImageBytes is not { Length: > 0 })
        {
            validation = new WatermarkOptionsDialogValidation(
                WatermarkDialogValidationTarget.Image,
                ImageValidationMessage);
            return false;
        }

        var scaleText = (input.ScaleText ?? string.Empty).Trim();
        if (!int.TryParse(scaleText, NumberStyles.Integer, culture, out var scale) ||
            scale is < 0 or > 500)
        {
            validation = new WatermarkOptionsDialogValidation(
                WatermarkDialogValidationTarget.Scale,
                ScaleValidationMessage);
            return false;
        }

        result = new WatermarkOptions(string.Empty)
        {
            FontFamily = DefaultFontFamily,
            FontColorHex = DefaultFontColorHex,
            Layout = ToLayout(input.IsHorizontal),
            Opacity = ToOpacity(input.IsWashout),
            ImageBytes = input.ImageBytes,
            ScalePct = scale,
        };
        return true;
    }

    public static string FormatLoadedImageLabel(byte[]? imageBytes) =>
        imageBytes is { Length: > 0 }
            ? $"(image loaded - {imageBytes.Length / 1024} KB)"
            : DefaultPicturePathText;

    public static string FormatPickedImageLabel(string fileName, long byteCount) =>
        $"{fileName} ({byteCount / 1024} KB)";

    public static WatermarkImageImportPlan BuildImageImportPlan(string? fileName, byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var displayName = Path.GetFileName((fileName ?? string.Empty).Trim());
        return new WatermarkImageImportPlan(
            imageBytes,
            FormatPickedImageLabel(displayName, imageBytes.LongLength));
    }

    public static string FormatImageReadFailure(string? errorMessage)
    {
        var detail = string.IsNullOrWhiteSpace(errorMessage)
            ? "Unknown error."
            : errorMessage.Trim();
        return $"{ImageReadFailurePrefix} {detail}";
    }

    private static WatermarkLayout ToLayout(bool isHorizontal) =>
        isHorizontal ? WatermarkLayout.Horizontal : WatermarkLayout.Diagonal;

    private static double ToOpacity(bool semitransparent) => semitransparent ? 0.3 : 1.0;

    // Watermark UI accepts Word-dialog-friendly color text: optional '#', 3/4/6/8 hex digits, and the
    // user's notation is preserved for the model. That is deliberately broader than DrawingML srgbClr and
    // not the same as shared ThemeColor, which normalizes opaque/translucent ARGB values.
    private static bool TryNormalizeColorHex(string? text, out string color)
    {
        color = string.Empty;

        var trimmed = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return false;

        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;

        var hexDigits = trimmed.AsSpan(1);
        if (hexDigits.Length is not (3 or 4 or 6 or 8))
            return false;

        for (var i = 0; i < hexDigits.Length; i++)
        {
            if (!Uri.IsHexDigit(hexDigits[i]))
                return false;
        }

        color = trimmed;
        return true;
    }
}
