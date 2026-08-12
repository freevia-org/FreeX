using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Toolkit-neutral visual and accessibility contract for an inline embedded OLE object. Both renderers
/// consume the same dimensions, label, colours, and accessible description; decoding the optional icon
/// remains a renderer responsibility.
/// </summary>
public sealed record EmbeddedObjectVisualPlan(
    double WidthPt,
    double HeightPt,
    string Label,
    string AccessibleName,
    string HelpText,
    InlineImage? Icon,
    string BackgroundColorHex,
    string BorderColorHex,
    string ForegroundColorHex);

public static class EmbeddedObjectVisualPlanner
{
    public const double DefaultSizePt = 96;
    public const string BackgroundColorHex = "#F3F6FB";
    public const string BorderColorHex = "#C0C8D8";
    public const string ForegroundColorHex = "#404040";

    public static EmbeddedObjectVisualPlan Build(EmbeddedObject embeddedObject)
    {
        ArgumentNullException.ThrowIfNull(embeddedObject);
        var label = string.IsNullOrWhiteSpace(embeddedObject.ProgId)
            ? "Embedded object"
            : embeddedObject.ProgId.Trim();
        var iconName = embeddedObject.Icon?.AltText?.Trim();
        var accessibleName = string.IsNullOrWhiteSpace(iconName) ? label : iconName;
        var helpText = label == "Embedded object"
            ? embeddedObject.IsLinked ? "Linked embedded object" : label
            : embeddedObject.IsLinked
                ? $"Linked {label} object"
                : $"Embedded {label} object";

        return new EmbeddedObjectVisualPlan(
            PositiveOrDefault(embeddedObject.WidthPt),
            PositiveOrDefault(embeddedObject.HeightPt),
            label,
            accessibleName,
            helpText,
            embeddedObject.Icon,
            BackgroundColorHex,
            BorderColorHex,
            ForegroundColorHex);
    }

    private static double PositiveOrDefault(double value) =>
        double.IsFinite(value) && value > 0 ? value : DefaultSizePt;
}
