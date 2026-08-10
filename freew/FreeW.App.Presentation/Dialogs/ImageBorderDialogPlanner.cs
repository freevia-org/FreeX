using System.Globalization;
using System.Text.RegularExpressions;

namespace FreeW.App.Presentation.Dialogs;

public enum ImageBorderDialogField
{
    Color,
    Width,
    Style
}

public sealed record ImageBorderDialogInitialState(
    string ColorText,
    string WidthText,
    int DashIndex);

public sealed record ImageBorderDialogInput(
    string? ColorText,
    string? WidthText,
    int DashIndex);

public sealed record ImageBorderValidation(
    ImageBorderDialogField Field,
    string Message);

public sealed record ImageBorderDialogResult(
    string? Color,
    double Width,
    string? Dash);

public static partial class ImageBorderDialogPlanner
{
    public const double DefaultWidthPt = 0.75;
    public const string ColorValidationMessage =
        "Enter a valid 6-digit hex color (e.g. FF0000) or leave blank to remove the border.";
    public const string WidthValidationMessage = "Enter a positive border width in points.";

    public static DialogSurfaceSpec<ImageBorderDialogField> Surface { get; } = new(
        Title: "Picture Border",
        AutomationId: "ImageBorderDialog",
        AutomationName: "Picture Border",
        Fields:
        [
            new(ImageBorderDialogField.Color, "Color (hex, empty = no border):", "ImageBorderColorTextBox", "Picture border color"),
            new(ImageBorderDialogField.Width, "Width (pt):", "ImageBorderWidthTextBox", "Picture border width"),
            new(ImageBorderDialogField.Style, "Style:", "ImageBorderStyleComboBox", "Picture border style"),
        ],
        SupportingText: "Color: 6-digit RGB hex, e.g. 000000 for black. Leave blank to remove the border.",
        ValidationAutomationId: "ImageBorderValidationText");

    public static readonly IReadOnlyList<ImageDialogChoice<string>> DashItems =
    [
        new("solid", "solid"),
        new("dash", "dash"),
        new("dot", "dot"),
        new("dashDot", "dashDot"),
        new("dashDotDot", "dashDotDot"),
        new("lgDash", "lgDash"),
        new("lgDashDot", "lgDashDot"),
    ];

    public static ImageBorderDialogInitialState BuildInitialState(
        string? colorHex,
        double widthPt,
        string? dash,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new ImageBorderDialogInitialState(
            ColorText: FormatColorText(colorHex),
            WidthText: FormatPoints(widthPt > 0 ? widthPt : DefaultWidthPt, culture),
            DashIndex: IndexOf(DashItems, string.IsNullOrEmpty(dash) ? "solid" : dash));
    }

    public static bool TryBuildResult(
        ImageBorderDialogInput input,
        CultureInfo culture,
        out ImageBorderDialogResult? result,
        out ImageBorderValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        // Picture-border dialog policy is narrower than watermark color text: blank removes the border,
        // otherwise the model receives bare six-digit RGB for the DOCX writer's a:srgbClr boundary.
        var color = FormatColorText(input.ColorText);
        if (color.Length == 0)
        {
            result = new ImageBorderDialogResult(null, 0, null);
            return true;
        }

        if (color.Length != 6 || !HexColorRegex().IsMatch(color))
        {
            validation = new ImageBorderValidation(ImageBorderDialogField.Color, ColorValidationMessage);
            return false;
        }

        if (!TryParsePositive(input.WidthText, culture, out var width))
        {
            validation = new ImageBorderValidation(ImageBorderDialogField.Width, WidthValidationMessage);
            return false;
        }

        var dash = ChoiceAt(DashItems, input.DashIndex).Value;
        result = new ImageBorderDialogResult(
            Color: color.ToUpperInvariant(),
            Width: width,
            Dash: dash == "solid" ? null : dash);
        return true;
    }

    public static string FormatColorText(string? colorHex) =>
        (colorHex ?? string.Empty).Trim().TrimStart('#');

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static bool TryParsePositive(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value) && value > 0;
    }

    private static ImageDialogChoice<TValue> ChoiceAt<TValue>(
        IReadOnlyList<ImageDialogChoice<TValue>> choices,
        int index) =>
        choices[Math.Clamp(index, 0, choices.Count - 1)];

    private static int IndexOf<TValue>(
        IReadOnlyList<ImageDialogChoice<TValue>> choices,
        TValue value)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (EqualityComparer<TValue>.Default.Equals(choices[i].Value, value))
                return i;
        }

        return 0;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorRegex();
}
