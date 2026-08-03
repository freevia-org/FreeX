using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public static class ChartTitleDialogPlanner
{
    public static string? NormalizeTitle(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    public static ChartTitleDialogResult BuildResult(string? text) =>
        new(true, NormalizeTitle(text));
}

public sealed record ChartTitleDialogResult(bool Accepted, string? NewTitle);

public sealed record ChartAxisTitlesDialogResult(
    string? CategoryTitle,
    string? ValueTitle);

public static class ChartAxisTitlesDialogPlanner
{
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

public static class InsertChartDialogPlanner
{
    public const string DefaultSeriesName = "Sales";
    public const string DefaultTitle = "Quarterly Sales";
    public const string EmptyRowsValidationMessage = "Enter at least one data row.";

    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new("OK", IsDefault: true),
        new("Cancel", IsCancel: true),
    ];

    private static readonly string[] DefaultCategories = ["Q1", "Q2", "Q3", "Q4"];
    private static readonly double[] DefaultValues = [8.0, 5.0, 11.0, 7.0];

    public static InsertChartDialogInitialState BuildInitialState(
        Chart? seed,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var kind = seed?.Kind ?? ChartKind.Column;
        var title = seed?.Title ?? DefaultTitle;
        var seriesCount = seed?.Series.Count > 0 ? seed.Series.Count : 1;
        var seriesNames = Enumerable.Range(0, seriesCount)
            .Select(index => seed?.Series.Count > index && !string.IsNullOrWhiteSpace(seed.Series[index].Name)
                ? seed.Series[index].Name!
                : index == 0 ? DefaultSeriesName : $"Series {index + 1}")
            .ToArray();

        var rows = new List<InsertChartDialogRow>();
        if (seed is null)
        {
            for (var index = 0; index < DefaultCategories.Length; index++)
            {
                rows.Add(new InsertChartDialogRow(
                    DefaultCategories[index],
                    [DefaultValues[index].ToString("G", culture)]));
            }
        }
        else
        {
            var rowCount = Math.Max(
                seed.Categories.Count,
                seed.Series.Count > 0 ? seed.Series.Max(series => series.Values.Count) : 0);
            for (var row = 0; row < rowCount; row++)
            {
                var values = seriesNames.Select((_, series) =>
                    seed.Series.Count > series && seed.Series[series].Values.Count > row
                        ? seed.Series[series].Values[row].ToString("G", culture)
                        : "0").ToArray();
                rows.Add(new InsertChartDialogRow(
                    row < seed.Categories.Count ? seed.Categories[row] : string.Empty,
                    values));
            }

            if (rows.Count == 0)
                rows.Add(new InsertChartDialogRow(DefaultCategories[0], [DefaultValues[0].ToString("G", culture)]));
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

public static class SmartArtDialogPlanner
{
    public const string EmptyNodesValidationMessage = "Enter at least one node text.";
    public static readonly IReadOnlyList<string> DefaultNodeTexts = ["First", "Second", "Third"];

    public static SmartArtDialogInitialState BuildInitialState(SmartArt? seed) =>
        new(seed?.Kind ?? SmartArtKind.Process, FlattenNodeTexts(seed).ToArray());

    public static bool TryBuildResult(
        SmartArtKind kind,
        IEnumerable<string> nodeTexts,
        out SmartArt? result,
        out string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(nodeTexts);

        var texts = nodeTexts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text.Trim())
            .ToArray();
        if (texts.Length == 0)
        {
            result = null;
            errorMessage = EmptyNodesValidationMessage;
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
