using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum ImagePositionDialogField
{
    HorizontalOffset,
    HorizontalAnchor,
    VerticalOffset,
    VerticalAnchor
}

public sealed record ImagePositionDialogInitialState(
    string HorizontalOffsetText,
    string VerticalOffsetText,
    int HorizontalAnchorIndex,
    int VerticalAnchorIndex);

public sealed record ImagePositionDialogInput(
    string? HorizontalOffsetText,
    string? VerticalOffsetText,
    int HorizontalAnchorIndex,
    int VerticalAnchorIndex);

public sealed record ImagePositionValidation(
    ImagePositionDialogField Field,
    string Message);

public sealed record ImagePositionDialogResult(
    double HorizontalOffset,
    double VerticalOffset,
    HorizontalAnchor HorizontalAnchor,
    VerticalAnchor VerticalAnchor);

public static class ImagePositionDialogPlanner
{
    public const string DefaultTitle = "Picture Position";
    public const string OffsetValidationMessage = "Enter valid numeric offsets in points.";

    public static DialogSurfaceSpec<ImagePositionDialogField> Surface { get; } = new(
        Title: DefaultTitle,
        AutomationId: "ImagePositionDialog",
        AutomationName: "Picture Position",
        Fields:
        [
            new(ImagePositionDialogField.HorizontalOffset, "Horizontal offset (pt):", "ImagePositionHorizontalOffsetTextBox", "Horizontal offset"),
            new(ImagePositionDialogField.HorizontalAnchor, "Relative to:", "ImagePositionHorizontalAnchorComboBox", "Horizontal position relative to"),
            new(ImagePositionDialogField.VerticalOffset, "Vertical offset (pt):", "ImagePositionVerticalOffsetTextBox", "Vertical offset"),
            new(ImagePositionDialogField.VerticalAnchor, "Relative to:", "ImagePositionVerticalAnchorComboBox", "Vertical position relative to"),
        ],
        ValidationAutomationId: "ImagePositionValidationText");

    public static readonly IReadOnlyList<ImageDialogChoice<HorizontalAnchor>> HorizontalAnchorItems =
    [
        new("Column", HorizontalAnchor.Column),
        new("Margin", HorizontalAnchor.Margin),
        new("Page", HorizontalAnchor.Page),
    ];

    public static readonly IReadOnlyList<ImageDialogChoice<VerticalAnchor>> VerticalAnchorItems =
    [
        new("Paragraph", VerticalAnchor.Paragraph),
        new("Margin", VerticalAnchor.Margin),
        new("Page", VerticalAnchor.Page),
    ];

    public static ImagePositionDialogInitialState BuildInitialState(
        double horizontalOffsetPt,
        double verticalOffsetPt,
        HorizontalAnchor horizontalAnchor,
        VerticalAnchor verticalAnchor,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new ImagePositionDialogInitialState(
            HorizontalOffsetText: FormatPoints(horizontalOffsetPt, culture),
            VerticalOffsetText: FormatPoints(verticalOffsetPt, culture),
            HorizontalAnchorIndex: IndexOf(HorizontalAnchorItems, horizontalAnchor),
            VerticalAnchorIndex: IndexOf(VerticalAnchorItems, verticalAnchor));
    }

    public static bool TryBuildResult(
        ImagePositionDialogInput input,
        CultureInfo culture,
        out ImagePositionDialogResult? result,
        out ImagePositionValidation? validation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        validation = null;

        if (!TryParseOffset(input.HorizontalOffsetText, culture, out var horizontalOffset))
        {
            validation = new ImagePositionValidation(
                ImagePositionDialogField.HorizontalOffset,
                OffsetValidationMessage);
            return false;
        }

        if (!TryParseOffset(input.VerticalOffsetText, culture, out var verticalOffset))
        {
            validation = new ImagePositionValidation(
                ImagePositionDialogField.VerticalOffset,
                OffsetValidationMessage);
            return false;
        }

        result = new ImagePositionDialogResult(
            HorizontalOffset: horizontalOffset,
            VerticalOffset: verticalOffset,
            HorizontalAnchor: ChoiceAt(HorizontalAnchorItems, input.HorizontalAnchorIndex).Value,
            VerticalAnchor: ChoiceAt(VerticalAnchorItems, input.VerticalAnchorIndex).Value);
        return true;
    }

    public static string FormatPoints(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("0.##", culture);
    }

    private static bool TryParseOffset(string? text, CultureInfo culture, out double value)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return double.TryParse(trimmed, NumberStyles.Float, culture, out value);
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
}
