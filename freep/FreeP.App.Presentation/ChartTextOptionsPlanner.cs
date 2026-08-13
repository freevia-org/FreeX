using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartTextBooleanOption(bool? Value, string Label);

public sealed record ChartTextOptionsSurfacePlan(
    string CommandId,
    string Title,
    string FontFamilyLabel,
    string FontSizeLabel,
    string BoldLabel,
    string ItalicLabel,
    string ColorLabel,
    string AutoHint,
    string OkLabel,
    string CancelLabel);

/// <summary>
/// Working-copy planner for the chart-wide default text properties already supported by the
/// chart reader, writer, and renderer. Blank values preserve PowerPoint's automatic defaults.
/// </summary>
public sealed class ChartTextOptionsPlanner
{
    public const string CommandId = "freep.chart.text-options";
    public const string DialogTitle = "Chart Text Options";
    public const string FontFamilyLabel = "Font family";
    public const string FontSizeLabel = "Font size (pt)";
    public const string BoldLabel = "Bold";
    public const string ItalicLabel = "Italic";
    public const string ColorLabel = "Text color (#RRGGBB)";
    public const string AutoHint = "Blank values use the chart or theme default.";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 440;
    public const double DefaultDialogHeight = 320;

    public static IReadOnlyList<ChartTextBooleanOption> BooleanOptions { get; } =
    [
        new(null, "Automatic"),
        new(true, "On"),
        new(false, "Off"),
    ];

    private string? _fontFamily;
    private double? _fontSizePt;
    private bool? _bold;
    private bool? _italic;
    private ThemeAwareColor? _color;

    private readonly ChartTextTarget _target;

    private ChartTextOptionsPlanner(ChartShape chart, ChartTextTarget target)
    {
        _target = target;
        var style = target switch
        {
            ChartTextTarget.Title => chart.TitleStyle,
            ChartTextTarget.Legend => chart.LegendTextStyle,
            _ => chart.TextStyle,
        };
        if (style is { IsImplicitDefault: false })
        {
            _fontFamily = style.FontFamily;
            _fontSizePt = style.FontSizePt;
            _bold = style.Bold;
            _italic = style.Italic;
            _color = style.Color;
        }
    }

    public static ChartTextOptionsSurfacePlan BuildSurfacePlan(ChartTextTarget target = ChartTextTarget.Chart) => new(
        CommandId,
        target switch
        {
            ChartTextTarget.Title => "Chart Title Text Options",
            ChartTextTarget.Legend => "Chart Legend Text Options",
            _ => DialogTitle,
        },
        FontFamilyLabel,
        FontSizeLabel,
        BoldLabel,
        ItalicLabel,
        ColorLabel,
        AutoHint,
        OkLabel,
        CancelLabel);

    public static ChartTextOptionsPlanner FromChart(
        ChartShape chart,
        ChartTextTarget target = ChartTextTarget.Chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartTextOptionsPlanner(chart, target);
    }

    public string? FontFamily => _fontFamily;
    public double? FontSizePt => _fontSizePt;
    public bool? Bold => _bold;
    public bool? Italic => _italic;
    public string ColorText => _color is null ? string.Empty : _color.Resolved.ToString();

    public void SetFontFamily(string? value) => _fontFamily =
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void SetFontSizePt(double? value) => _fontSizePt =
        value is null ? null : Math.Clamp(value.Value, 1, 400);

    public void SetBold(bool? value) => _bold = value;
    public void SetItalic(bool? value) => _italic = value;

    public void SetColor(string? value) => _color =
        ChartPointOptionsPlanner.ParseColor(value, ColorLabel);

    public void SetColor(ThemeAwareColor? value) => _color = value;

    public ChartTextOptions BuildCommitPlan() => new(
        _fontFamily,
        _fontSizePt,
        _bold,
        _italic,
        _color,
        _target);

}
