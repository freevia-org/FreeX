using System.Globalization;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public enum ChartTitleDialogField
{
    Title
}

public static class ChartTitleDialogPlanner
{
    public static DialogSurfaceSpec<ChartTitleDialogField> Surface { get; } = new(
        Title: "Chart Title",
        AutomationId: "ChartTitleDialog",
        AutomationName: "Chart Title",
        Fields:
        [
            new(ChartTitleDialogField.Title, "Title:", "ChartTitleTextBox", "Chart title"),
        ]);

    public static string? NormalizeTitle(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static ChartTitleDialogResult BuildResult(string? text) =>
        new(true, NormalizeTitle(text));
}

public sealed record ChartTitleDialogResult(bool Accepted, string? NewTitle);

public sealed record ChartAxisTitlesDialogResult(
    string? CategoryTitle,
    string? ValueTitle);

public enum ChartAxisTitlesDialogField
{
    Category,
    Value
}

public static class ChartAxisTitlesDialogPlanner
{
    public static DialogSurfaceSpec<ChartAxisTitlesDialogField> Surface { get; } = new(
        Title: "Axis Titles",
        AutomationId: "ChartAxisTitlesDialog",
        AutomationName: "Axis Titles",
        Fields:
        [
            new(ChartAxisTitlesDialogField.Category, "Category axis:", "ChartCategoryAxisTitleTextBox", "Category axis title"),
            new(ChartAxisTitlesDialogField.Value, "Value axis:", "ChartValueAxisTitleTextBox", "Value axis title"),
        ]);

    public static ChartAxisTitlesDialogResult BuildResult(string? categoryText, string? valueText) =>
        new(
            ChartTitleDialogPlanner.NormalizeTitle(categoryText),
            ChartTitleDialogPlanner.NormalizeTitle(valueText));
}

public sealed record InsertChartDialogRow(
    string Category,
    IReadOnlyList<string> SeriesValues);

public sealed record InsertChartDialogInitialState(
    ChartKind Kind,
    string Title,
    IReadOnlyList<string> SeriesNames,
    IReadOnlyList<InsertChartDialogRow> Rows);

public enum InsertChartDialogField
{
    ChartType,
    Title,
    Data
}

public static class InsertChartDialogPlanner
{
    public const string DefaultSeriesName = ChartDataPresetCatalog.DefaultSeriesName;
    public const string DefaultTitle = ChartDataPresetCatalog.DefaultTitle;
    public const string EmptyRowsValidationMessage = "Enter at least one data row.";
    public const string CategoryColumnHeader = "Category";

    public static DialogSurfaceSpec<InsertChartDialogField> Surface { get; } = new(
        Title: "Insert Chart",
        AutomationId: "InsertChartDialog",
        AutomationName: "Insert Chart",
        Fields:
        [
            new(InsertChartDialogField.ChartType, "Chart type:", "InsertChartTypeComboBox", "Chart type"),
            new(InsertChartDialogField.Title, "Title (optional):", "InsertChartTitleTextBox", "Chart title"),
            new(
                InsertChartDialogField.Data,
                "Chart data  (first column = category labels, remaining columns = series values):",
                "InsertChartDataEditor",
                "Chart data"),
        ],
        ValidationAutomationId: "InsertChartValidationText");

    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new("OK", IsDefault: true),
        new("Cancel", IsCancel: true),
    ];

    public static InsertChartDialogInitialState BuildInitialState(
        Chart? seed,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var source = seed ?? ChartDataPresetCatalog.CreateDefaultInsertion();
        var kind = source.Kind;
        var title = source.Title ?? DefaultTitle;
        var seriesCount = source.Series.Count > 0 ? source.Series.Count : 1;
        var seriesNames = Enumerable.Range(0, seriesCount)
            .Select(index => source.Series.Count > index && !string.IsNullOrWhiteSpace(source.Series[index].Name)
                ? source.Series[index].Name!
                : index == 0 ? DefaultSeriesName : $"Series {index + 1}")
            .ToArray();

        var rows = new List<InsertChartDialogRow>();
        var rowCount = Math.Max(
            source.Categories.Count,
            source.Series.Count > 0 ? source.Series.Max(series => series.Values.Count) : 0);
        for (var row = 0; row < rowCount; row++)
        {
            var values = seriesNames.Select((_, series) =>
                source.Series.Count > series && source.Series[series].Values.Count > row
                    ? source.Series[series].Values[row].ToString("G", culture)
                    : "0").ToArray();
            rows.Add(new InsertChartDialogRow(
                row < source.Categories.Count ? source.Categories[row] : string.Empty,
                values));
        }

        if (rows.Count == 0)
        {
            var fallback = ChartDataPresetCatalog.DefaultInsertion;
            rows.Add(new InsertChartDialogRow(
                fallback.Categories[0],
                [fallback.Series[0].Values[0].ToString("G", culture)]));
        }

        return new InsertChartDialogInitialState(kind, title, seriesNames, rows);
    }

    public static bool TryBuildResult(
        ChartKind kind,
        string? titleText,
        IReadOnlyList<string> seriesNames,
        IEnumerable<InsertChartDialogRow> inputRows,
        CultureInfo culture,
        out Chart? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(seriesNames);
        ArgumentNullException.ThrowIfNull(inputRows);
        ArgumentNullException.ThrowIfNull(culture);

        result = null;
        errorMessage = null;
        var rows = inputRows
            .Where(row => !string.IsNullOrWhiteSpace(row.Category)
                || row.SeriesValues.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();
        if (rows.Count == 0)
        {
            errorMessage = EmptyRowsValidationMessage;
            return false;
        }

        var chart = new Chart
        {
            Kind = kind,
            Title = ChartTitleDialogPlanner.NormalizeTitle(titleText),
        };
        foreach (var row in rows)
            chart.Categories.Add(row.Category.Trim());

        var seriesCount = Math.Max(1, seriesNames.Count);
        for (var seriesIndex = 0; seriesIndex < seriesCount; seriesIndex++)
        {
            var series = new ChartSeries
            {
                Name = seriesIndex < seriesNames.Count
                    ? ChartTitleDialogPlanner.NormalizeTitle(seriesNames[seriesIndex])
                    : null,
            };
            foreach (var row in rows)
            {
                var valueText = seriesIndex < row.SeriesValues.Count
                    ? row.SeriesValues[seriesIndex]
                    : null;
                series.Values.Add(double.TryParse(
                    valueText,
                    NumberStyles.Float,
                    culture,
                    out var value) ? value : 0.0);
            }
            chart.Series.Add(series);
        }

        result = chart;
        return true;
    }
}

public sealed record SmartArtDialogInitialState(
    SmartArtKind Kind,
    IReadOnlyList<string> NodeTexts);

public sealed record SmartArtDialogText(
    string InsertTitle,
    string EditTitle,
    string LayoutLabel,
    string NodeTextLabel,
    string EditNodeTextLabel,
    string AddShapeLabel,
    string RemoveShapeLabel,
    string NewItemLabel,
    string EmptyNodesValidationMessage);

public static class SmartArtDialogPlanner
{
    public const string EmptyNodesValidationMessage = "Enter at least one node text.";
    public const string NodeTextLabel = "Diagram text (one item per node - use Add/Remove to manage):";
    public static readonly IReadOnlyList<string> DefaultNodeTexts = ["First", "Second", "Third"];

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("SmartArt_Dialog_Insert_Title", "Insert SmartArt"),
        new("SmartArt_Dialog_Edit_Title", "Edit SmartArt Text"),
        new("SmartArt_Dialog_Layout_Label", "Layout:"),
        new("SmartArt_Dialog_NodeText_Label", NodeTextLabel),
        new("SmartArt_Dialog_EditNodeText_Label", "One shape per line:"),
        new("SmartArt_Dialog_AddShape_Label", "Add Shape"),
        new("SmartArt_Dialog_RemoveShape_Label", "Remove Shape"),
        new("SmartArt_Dialog_NewItem_Label", "New Item"),
        new("SmartArt_Dialog_EmptyNodes_Validation", EmptyNodesValidationMessage),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static SmartArtDialogText ResolveText(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText),
            Texts[5].Resolve(getText),
            Texts[6].Resolve(getText),
            Texts[7].Resolve(getText),
            Texts[8].Resolve(getText));

    public static SmartArtDialogInitialState BuildInitialState(SmartArt? seed) =>
        new(seed?.Kind ?? SmartArtKind.Process, FlattenNodeTexts(seed).ToArray());

    public static bool TryBuildResult(
        SmartArtKind kind,
        IEnumerable<string> nodeTexts,
        out SmartArt? result,
        out string? errorMessage,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(nodeTexts);

        var texts = nodeTexts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToArray();
        if (texts.Length == 0)
        {
            result = null;
            errorMessage = ResolveText(getText).EmptyNodesValidationMessage;
            return false;
        }

        result = SmartArt.Create(kind, texts);
        errorMessage = null;
        return true;
    }

    private static IEnumerable<string> FlattenNodeTexts(SmartArt? seed)
    {
        if (seed is null)
            return DefaultNodeTexts;

        var texts = new List<string>();
        foreach (var node in seed.Nodes)
        {
            texts.Add(node.Text);
            texts.AddRange(node.Children.Select(child => child.Text));
        }
        return texts.Count == 0 ? DefaultNodeTexts : texts;
    }
}
