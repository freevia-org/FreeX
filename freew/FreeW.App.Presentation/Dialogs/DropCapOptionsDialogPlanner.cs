using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum DropCapDialogPosition
{
    None,
    Dropped,
    InMargin
}

public enum DropCapOptionsDialogField
{
    Position,
    None,
    Dropped,
    InMargin,
    Font,
    LinesToDrop,
    DistanceFromText,
}

public sealed record DropCapOptionsInitialState(
    int PositionIndex,
    int FontIndex,
    string LinesToDropText,
    string DistanceFromTextText);

public sealed record DropCapOptionsDialogInput(
    int PositionIndex,
    string? FontText,
    string? LinesToDropText,
    string? DistanceFromTextText);

public sealed record DropCapOptionsDialogResult(
    DropCapDialogPosition Position,
    string? Font,
    int LinesToDrop,
    double DistanceFromTextPt)
{
    public DropCapPosition ModelPosition =>
        Position == DropCapDialogPosition.InMargin ? DropCapPosition.InMargin : DropCapPosition.Dropped;

    public double SizePt => Math.Max(14, LinesToDrop * 14.4);
}

/// <summary>Shared state, normalization, and result mapping for Insert &gt; Drop Cap &gt; Options.</summary>
public static class DropCapOptionsDialogPlanner
{
    public const string Title = "Drop Cap Options";
    public const string PositionLabel = "Position:";
    public const string NoneLabel = "None";
    public const string DroppedLabel = "Dropped";
    public const string InMarginLabel = "In Margin";
    public const string FontLabel = "Font:";
    public const string LinesToDropLabel = "Lines to drop (1-10):";
    public const string DistanceFromTextLabel = "Distance from text (pt):";
    public const string CurrentFontLabel = "(Current font)";
    public const string AutomationId = "DropCapOptionsDialog";
    public const string NoneAutomationId = "DropCapNone";
    public const string DroppedAutomationId = "DropCapDropped";
    public const string InMarginAutomationId = "DropCapInMargin";
    public const string FontAutomationId = "DropCapFont";
    public const string LinesAutomationId = "DropCapLines";
    public const string DistanceAutomationId = "DropCapDistance";

    public static DialogSurfaceSpec<DropCapOptionsDialogField> Surface { get; } = new(
        Title,
        AutomationId,
        Title,
        [
            new(DropCapOptionsDialogField.Position, PositionLabel, "DropCapPositionGroup", "Drop cap position"),
            new(DropCapOptionsDialogField.None, NoneLabel, NoneAutomationId, "No drop cap"),
            new(DropCapOptionsDialogField.Dropped, DroppedLabel, DroppedAutomationId, "Dropped drop cap"),
            new(DropCapOptionsDialogField.InMargin, InMarginLabel, InMarginAutomationId, "Drop cap in margin"),
            new(DropCapOptionsDialogField.Font, FontLabel, FontAutomationId, "Drop cap font"),
            new(DropCapOptionsDialogField.LinesToDrop, LinesToDropLabel, LinesAutomationId, "Lines to drop"),
            new(DropCapOptionsDialogField.DistanceFromText, DistanceFromTextLabel, DistanceAutomationId, "Distance from text"),
        ]);

    public static readonly IReadOnlyList<string> FontNames =
        [CurrentFontLabel, "Arial", "Calibri", "Times New Roman", "Georgia", "Cambria"];

    public static DropCapOptionsInitialState BuildInitialState(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return new DropCapOptionsInitialState(
            PositionIndex: (int)DropCapDialogPosition.Dropped,
            FontIndex: 0,
            LinesToDropText: DropCap.DefaultLineSpan.ToString(culture),
            DistanceFromTextText: "0");
    }

    public const string ValidationMessage =
        "Enter a whole number of lines to drop and a non-negative distance from text.";

    /// <summary>
    /// r190: this used to be <c>BuildResult</c>, which discarded the bool from both TryParse calls
    /// (<c>_ = int.TryParse(...)</c>). Unparseable text therefore left the out-parameter at 0, and
    /// the Math.Clamp below turned that into LinesToDrop=1 and DistanceFromTextPt=0 -- values the
    /// user never typed, applied silently with no way to tell the dialog had ignored the input.
    /// Every sibling dialog in this file (Columns, Hyphenation, LineNumber, ParagraphBreaks) already
    /// reports a bad value instead of substituting one; this now matches them.
    ///
    /// Clamping a value the user DID type stays as it was: asking for 40 dropped lines is a real
    /// request out of range, not a typo, and silently using the nearest legal value is what the
    /// other dialogs do with it too.
    /// </summary>
    public static bool TryBuildResult(
        DropCapOptionsDialogInput input,
        CultureInfo culture,
        out DropCapOptionsDialogResult? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;

        if (!int.TryParse(input.LinesToDropText, NumberStyles.Integer, culture, out var lines) ||
            !double.TryParse(input.DistanceFromTextText, NumberStyles.Float, culture, out var distance) ||
            double.IsNaN(distance) ||
            double.IsInfinity(distance))
        {
            errorMessage = ValidationMessage;
            return false;
        }

        var font = (input.FontText ?? string.Empty).Trim();

        result = new DropCapOptionsDialogResult(
            Position: (DropCapDialogPosition)Math.Clamp(input.PositionIndex, 0, 2),
            Font: font is "" or CurrentFontLabel ? null : font,
            LinesToDrop: Math.Clamp(lines, 1, 10),
            DistanceFromTextPt: Math.Clamp(distance, 0, 100));
        return true;
    }
}
