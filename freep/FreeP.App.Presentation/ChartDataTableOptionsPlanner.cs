using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record ChartDataTableOptionsSurfacePlan(
    string CommandId,
    string Title,
    string ShowDataTableLabel,
    string HorizontalBorderLabel,
    string VerticalBorderLabel,
    string OutlineBorderLabel,
    string LegendKeysLabel,
    string BackgroundColorLabel,
    string BorderColorLabel,
    string BorderWidthLabel,
    string TextColorLabel,
    string FontSizeLabel,
    string FontFamilyLabel,
    string BoldLabel,
    string ItalicLabel,
    string OkLabel,
    string CancelLabel);

/// <summary>Working-copy planner for chart data-table authoring options.</summary>
public sealed class ChartDataTableOptionsPlanner
{
    public const string CommandId = "freep.chart.data-table-options";
    public const string DialogTitle = "Chart Data Table Options";
    public const string ShowDataTableLabel = "Show data table";
    public const string HorizontalBorderLabel = "Horizontal borders";
    public const string VerticalBorderLabel = "Vertical borders";
    public const string OutlineBorderLabel = "Outline border";
    public const string LegendKeysLabel = "Legend keys";
    public const string BackgroundColorLabel = "Background color (#RRGGBB)";
    public const string BorderColorLabel = "Border color (#RRGGBB)";
    public const string BorderWidthLabel = "Border width (pt)";
    public const string TextColorLabel = "Text color (#RRGGBB)";
    public const string FontSizeLabel = "Font size (pt)";
    public const string FontFamilyLabel = "Font family";
    public const string BoldLabel = "Bold";
    public const string ItalicLabel = "Italic";
    public const string OkLabel = "OK";
    public const string CancelLabel = "Cancel";
    public const double DefaultDialogWidth = 380;
    public const double DefaultDialogHeight = 560;

    private bool _showDataTable;
    private bool _showHorizontalBorder = true;
    private bool _showVerticalBorder = true;
    private bool _showOutlineBorder = true;
    private bool _showLegendKeys;
    private string _backgroundColor = string.Empty;
    private string _borderColor = string.Empty;
    private double? _borderWidthPt;
    private string _textColor = string.Empty;
    private double? _fontSizePt;
    private string _fontFamily = string.Empty;
    private bool? _bold;
    private bool? _italic;

    private ChartDataTableOptionsPlanner(ChartShape chart)
    {
        _showDataTable = chart.DataTable is not null;
        if (chart.DataTable is { } dataTable)
        {
            _showHorizontalBorder = dataTable.ShowHorizontalBorder;
            _showVerticalBorder = dataTable.ShowVerticalBorder;
            _showOutlineBorder = dataTable.ShowOutlineBorder;
            _showLegendKeys = dataTable.ShowLegendKeys;
            _backgroundColor = FormatFillColor(dataTable.BackgroundFill);
            if (dataTable.BorderOutline is ShapeOutline.Visible border)
            {
                _borderColor = border.Color.Resolved.ToString();
                _borderWidthPt = border.WidthPt;
            }

            if (dataTable.TextStyle is { } textStyle)
            {
                _textColor = textStyle.Color?.Resolved.ToString() ?? string.Empty;
                _fontSizePt = textStyle.FontSizePt;
                _fontFamily = textStyle.FontFamily ?? string.Empty;
                _bold = textStyle.Bold;
                _italic = textStyle.Italic;
            }
        }
    }

    public static ChartDataTableOptionsSurfacePlan BuildSurfacePlan() => new(
        CommandId,
        DialogTitle,
        ShowDataTableLabel,
        HorizontalBorderLabel,
        VerticalBorderLabel,
        OutlineBorderLabel,
        LegendKeysLabel,
        BackgroundColorLabel,
        BorderColorLabel,
        BorderWidthLabel,
        TextColorLabel,
        FontSizeLabel,
        FontFamilyLabel,
        BoldLabel,
        ItalicLabel,
        OkLabel,
        CancelLabel);

    public static ChartDataTableOptionsPlanner FromChart(ChartShape chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new ChartDataTableOptionsPlanner(chart);
    }

    public bool ShowDataTable => _showDataTable;
    public bool ShowHorizontalBorder => _showHorizontalBorder;
    public bool ShowVerticalBorder => _showVerticalBorder;
    public bool ShowOutlineBorder => _showOutlineBorder;
    public bool ShowLegendKeys => _showLegendKeys;
    public string BackgroundColor => _backgroundColor;
    public string BorderColor => _borderColor;
    public double? BorderWidthPt => _borderWidthPt;
    public string TextColor => _textColor;
    public double? FontSizePt => _fontSizePt;
    public string FontFamily => _fontFamily;
    public bool? Bold => _bold;
    public bool? Italic => _italic;

    public void SetShowDataTable(bool value) => _showDataTable = value;
    public void SetShowHorizontalBorder(bool value) => _showHorizontalBorder = value;
    public void SetShowVerticalBorder(bool value) => _showVerticalBorder = value;
    public void SetShowOutlineBorder(bool value) => _showOutlineBorder = value;
    public void SetShowLegendKeys(bool value) => _showLegendKeys = value;
    public void SetBackgroundColor(string? value) => _backgroundColor = value?.Trim() ?? string.Empty;
    public void SetBorderColor(string? value) => _borderColor = value?.Trim() ?? string.Empty;
    public void SetBorderWidth(double? value) => _borderWidthPt = NormalizePositive(value);
    public void SetTextColor(string? value) => _textColor = value?.Trim() ?? string.Empty;
    public void SetFontSize(double? value) => _fontSizePt = NormalizePositive(value);
    public void SetFontFamily(string? value) => _fontFamily = value?.Trim() ?? string.Empty;
    public void SetBold(bool? value) => _bold = value;
    public void SetItalic(bool? value) => _italic = value;

    public ChartDataTableOptions BuildCommitPlan() => new(
        _showDataTable,
        _showHorizontalBorder,
        _showVerticalBorder,
        _showOutlineBorder,
        _showLegendKeys,
        NullIfBlank(_backgroundColor),
        NullIfBlank(_borderColor),
        _borderWidthPt,
        NullIfBlank(_textColor),
        _fontSizePt,
        NullIfBlank(_fontFamily),
        _bold,
        _italic);

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static double? NormalizePositive(double? value) =>
        value.HasValue && value.Value > 0 && double.IsFinite(value.Value) ? value : null;

    private static string FormatFillColor(ShapeFill? fill) =>
        fill is ShapeFill.Solid solid ? solid.Color.Resolved.ToString() : string.Empty;
}
