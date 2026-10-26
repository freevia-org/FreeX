using System.Globalization;

namespace FreeP.Core.Model;

/// <summary>Atomically updates chart data-table visibility and authored style options.</summary>
public sealed class SetChartDataTableOptionsCommand : IPresentationCommand
{
    private readonly int _slideIndex;
    private readonly uint _shapeId;
    private readonly ChartDataTableOptions _newOptions;
    private ChartDataTableSettings? _oldDataTable;

    public SetChartDataTableOptionsCommand(int slideIndex, uint shapeId, ChartDataTableOptions options)
    {
        _slideIndex = slideIndex;
        _shapeId = shapeId;
        _newOptions = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Label => "Set Chart Data Table Options";

    // r202: the same guard Apply opens with. Without it the bus pushed an undo entry for a
    // command that a protection-locked chart makes a no-op -- and that push clears redo.
    public bool HasEffect(Presentation p) =>
        ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId) is not null;

    public void Apply(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        _oldDataTable = Clone(chart.DataTable);
        if (!_newOptions.ShowDataTable)
        {
            chart.DataTable = null;
        }
        else
        {
            var dataTable = new ChartDataTableSettings
            {
                ShowHorizontalBorder = _newOptions.ShowHorizontalBorder,
                ShowVerticalBorder = _newOptions.ShowVerticalBorder,
                ShowOutlineBorder = _newOptions.ShowOutlineBorder,
                ShowLegendKeys = _newOptions.ShowLegendKeys,
                BackgroundFill = _oldDataTable?.BackgroundFill,
                BorderOutline = _oldDataTable?.BorderOutline,
                TextStyle = CloneTextStyle(_oldDataTable?.TextStyle),
            };
            ApplyStyle(dataTable, _oldDataTable, _newOptions);
            chart.DataTable = dataTable;
        }

        ChartHelper.MarkWorkbookDirty(chart);
    }

    public void Revert(Presentation p)
    {
        var chart = ChartHelper.FindFormattingEditable(p, _slideIndex, _shapeId);
        if (chart is null)
            return;

        chart.DataTable = Clone(_oldDataTable);
        ChartHelper.MarkWorkbookDirty(chart);
    }

    private static ChartDataTableSettings? Clone(ChartDataTableSettings? source) => source is null
        ? null
        : new ChartDataTableSettings
        {
            ShowHorizontalBorder = source.ShowHorizontalBorder,
            ShowVerticalBorder = source.ShowVerticalBorder,
            ShowOutlineBorder = source.ShowOutlineBorder,
            ShowLegendKeys = source.ShowLegendKeys,
            BackgroundFill = source.BackgroundFill,
            BorderOutline = source.BorderOutline,
            TextStyle = CloneTextStyle(source.TextStyle),
        };

    private static void ApplyStyle(
        ChartDataTableSettings target,
        ChartDataTableSettings? old,
        ChartDataTableOptions options)
    {
        if (options.BackgroundColor is not null)
            target.BackgroundFill = new ShapeFill.Solid(ParseColor(options.BackgroundColor, nameof(options.BackgroundColor)));

        if (options.BorderColor is not null || options.BorderWidthPt is not null)
        {
            var previous = old?.BorderOutline as ShapeOutline.Visible;
            var color = options.BorderColor is not null
                ? ParseColor(options.BorderColor, nameof(options.BorderColor))
                : previous?.Color ?? ThemeAwareColor.Black;
            var width = options.BorderWidthPt ?? previous?.WidthPt ?? 0.75;
            target.BorderOutline = new ShapeOutline.Visible(
                color,
                Math.Max(0.01, width),
                previous?.Dash ?? OutlineDash.Solid,
                previous?.BeginLineEnd,
                previous?.EndLineEnd);
        }

        if (options.TextColor is not null || options.FontSizePt is not null ||
            options.FontFamily is not null || options.Bold is not null || options.Italic is not null)
        {
            var textStyle = CloneTextStyle(old?.TextStyle) ?? new ChartTextStyle();
            if (options.TextColor is not null)
                textStyle.Color = ParseColor(options.TextColor, nameof(options.TextColor));
            if (options.FontSizePt is not null)
                textStyle.FontSizePt = Math.Max(0.01, options.FontSizePt.Value);
            if (options.FontFamily is not null)
                textStyle.FontFamily = options.FontFamily;
            if (options.Bold is not null)
                textStyle.Bold = options.Bold;
            if (options.Italic is not null)
                textStyle.Italic = options.Italic;
            target.TextStyle = textStyle;
        }
    }

    private static ChartTextStyle? CloneTextStyle(ChartTextStyle? source) => source is null
        ? null
        : new ChartTextStyle
        {
            IsImplicitDefault = source.IsImplicitDefault,
            FontSizePt = source.FontSizePt,
            Bold = source.Bold,
            Italic = source.Italic,
            Color = source.Color,
            FontFamily = source.FontFamily,
        };

    private static ThemeAwareColor ParseColor(string value, string field)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6 ||
            !int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException($"{field} must be a six-digit #RRGGBB color.", field);
        return new ThemeAwareColor(SrgbColor.FromRgb(rgb));
    }
}
