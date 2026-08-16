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
    public const ChartTitleDialogField InitialFocusField = ChartTitleDialogField.Title;

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("ChartTitle_Dialog_Title", "Chart Title"),
        new("ChartTitle_Title_Label", "Title:"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static DialogSurfaceSpec<ChartTitleDialogField> Surface { get; } = BuildSurface();

    public static DialogSurfaceSpec<ChartTitleDialogField> BuildSurface(
        Func<string, string?>? getText = null) => new(
        Title: Texts[0].Resolve(getText),
        AutomationId: "ChartTitleDialog",
        AutomationName: "Chart Title",
        Fields:
        [
            new(ChartTitleDialogField.Title, Texts[1].Resolve(getText), "ChartTitleTextBox", "Chart title"),
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
    public const ChartAxisTitlesDialogField InitialFocusField = ChartAxisTitlesDialogField.Category;

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("ChartAxisTitles_Dialog_Title", "Axis Titles"),
        new("ChartAxisTitles_Category_Label", "Category axis:"),
        new("ChartAxisTitles_Value_Label", "Value axis:"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static DialogSurfaceSpec<ChartAxisTitlesDialogField> Surface { get; } = BuildSurface();

    public static DialogSurfaceSpec<ChartAxisTitlesDialogField> BuildSurface(
        Func<string, string?>? getText = null) => new(
        Title: Texts[0].Resolve(getText),
        AutomationId: "ChartAxisTitlesDialog",
        AutomationName: "Axis Titles",
        Fields:
        [
            new(ChartAxisTitlesDialogField.Category, Texts[1].Resolve(getText), "ChartCategoryAxisTitleTextBox", "Category axis title"),
            new(ChartAxisTitlesDialogField.Value, Texts[2].Resolve(getText), "ChartValueAxisTitleTextBox", "Value axis title"),
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

public sealed record InsertChartDialogText(
    string Title,
    string ChartTypeLabel,
    string TitleLabel,
    string DataLabel,
    string CategoryColumnHeader,
    string AddRowLabel,
    string RemoveRowLabel,
    string EmptyRowsValidationMessage,
    string OkButton,
    string CancelButton);

public static class InsertChartDialogPlanner
{
    public const string DefaultSeriesName = ChartDataPresetCatalog.DefaultSeriesName;
    public const string DefaultTitle = ChartDataPresetCatalog.DefaultTitle;
    public const string EmptyRowsValidationMessage = "Enter at least one data row.";
    public const string CategoryColumnHeader = "Category";

    private static readonly ResourceTextDescriptor[] Texts =
    [
        new("InsertChart_Dialog_Title", "Insert Chart"),
        new("InsertChart_ChartType_Label", "Chart type:"),
        new("InsertChart_Title_Label", "Title (optional):"),
        new("InsertChart_Data_Label", "Chart data  (first column = category labels, remaining columns = series values):"),
        new("InsertChart_Category_Header", CategoryColumnHeader),
        new("InsertChart_AddRow_Label", "Add Row"),
        new("InsertChart_RemoveRow_Label", "Remove Row"),
        new("InsertChart_EmptyRows_Validation", EmptyRowsValidationMessage),
        new("Common_Ok", "OK"),
        new("Common_Cancel", "Cancel"),
    ];

    public static IReadOnlyList<string> RequiredResourceKeys =>
        Texts.Select(text => text.ResourceKey).ToArray();

    public static InsertChartDialogText ResolveText(Func<string, string?>? getText = null) =>
        new(
            Texts[0].Resolve(getText),
            Texts[1].Resolve(getText),
            Texts[2].Resolve(getText),
            Texts[3].Resolve(getText),
            Texts[4].Resolve(getText),
            Texts[5].Resolve(getText),
            Texts[6].Resolve(getText),
            Texts[7].Resolve(getText),
            Texts[8].Resolve(getText),
            Texts[9].Resolve(getText));

    public static DialogSurfaceSpec<InsertChartDialogField> Surface { get; } = BuildSurface();

    public static DialogSurfaceSpec<InsertChartDialogField> BuildSurface(
        Func<string, string?>? getText = null)
    {
        var text = ResolveText(getText);
        return new(
        Title: text.Title,
        AutomationId: "InsertChartDialog",
        AutomationName: "Insert Chart",
        Fields:
        [
            new(InsertChartDialogField.ChartType, text.ChartTypeLabel, "InsertChartTypeComboBox", "Chart type"),
            new(InsertChartDialogField.Title, text.TitleLabel, "InsertChartTitleTextBox", "Chart title"),
            new(
                InsertChartDialogField.Data,
                text.DataLabel,
                "InsertChartDataEditor",
                "Chart data"),
        ],
        ValidationAutomationId: "InsertChartValidationText");
    }

    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
        BuildActionButtons();

    public static IReadOnlyList<DialogActionButtonPlan> BuildActionButtons(
        Func<string, string?>? getText = null)
    {
        var text = ResolveText(getText);
        return
        [
            new(text.OkButton, IsDefault: true),
            new(text.CancelButton, IsCancel: true),
        ];
    }

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
        => TryBuildResult(
            kind,
            titleText,
            seriesNames,
            inputRows,
            culture,
            getText: null,
            out result,
            out errorMessage);

    public static bool TryBuildResult(
        ChartKind kind,
        string? titleText,
        IReadOnlyList<string> seriesNames,
        IEnumerable<InsertChartDialogRow> inputRows,
        CultureInfo culture,
        Func<string, string?>? getText,
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
            errorMessage = ResolveText(getText).EmptyRowsValidationMessage;
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

public readonly record struct SmartArtDialogVisualMetrics(
    double DialogWidth,
    double MinimumDialogHeight,
    double OuterMargin,
    double LabelBottomMargin,
    double LayoutControlBottomMargin,
    double NodeListHeight,
    double NodeListBottomMargin,
    double EditorBottomMargin,
    double InlineActionSpacing,
    double InlineActionBottomMargin,
    double FooterTopMargin,
    double FooterButtonWidth,
    double InlineButtonHorizontalPadding,
    double ButtonVerticalPadding);

public static class SmartArtDialogPlanner
{
    public const string EmptyNodesValidationMessage = "Enter at least one node text.";
    public const string NodeTextLabel = "Diagram text (one item per node - use Add/Remove to manage):";
    public static readonly IReadOnlyList<string> DefaultNodeTexts = ["First", "Second", "Third"];
    public static SmartArtDialogVisualMetrics VisualMetrics { get; } = new(
        DialogWidth: 440,
        MinimumDialogHeight: 360,
        OuterMargin: 14,
        LabelBottomMargin: 4,
        LayoutControlBottomMargin: 10,
        NodeListHeight: 130,
        NodeListBottomMargin: 6,
        EditorBottomMargin: 6,
        InlineActionSpacing: 6,
        InlineActionBottomMargin: 10,
        FooterTopMargin: 4,
        FooterButtonWidth: 72,
        InlineButtonHorizontalPadding: 8,
        ButtonVerticalPadding: 3);

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
