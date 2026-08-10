using FreeW.Core.Model;

namespace FreeW.App.Presentation.Editing;

public sealed record ChartSeriesDataPreset(
    string? Name,
    IReadOnlyList<double> Values);

public sealed record ChartDataPreset(
    string Name,
    ChartKind Kind,
    string? Title,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ChartSeriesDataPreset> Series)
{
    public Chart CreateChart()
    {
        var chart = new Chart
        {
            Kind = Kind,
            Title = Title,
        };
        chart.Categories.AddRange(Categories);
        foreach (var series in Series)
            chart.Series.Add(new ChartSeries(series.Name, series.Values));
        return chart;
    }
}

/// <summary>
/// Owns renderer-neutral chart data recipes used by default insertion, the Insert Chart dialog, and
/// typed ribbon replacement commands. Every request materializes a fresh mutable chart model.
/// </summary>
public static class ChartDataPresetCatalog
{
    public const string DefaultTitle = "Quarterly Sales";
    public const string DefaultSeriesName = "Sales";

    public static ChartDataPreset DefaultInsertion { get; } = new(
        Name: "Default quarterly sales",
        Kind: ChartKind.Column,
        Title: DefaultTitle,
        Categories: ["Q1", "Q2", "Q3", "Q4"],
        Series: [new ChartSeriesDataPreset(DefaultSeriesName, [8d, 5d, 11d, 7d])]);

    public static IReadOnlyList<ChartDataPreset> NamedReplacements { get; } =
    [
        new(
            Name: "Quarterly Sales",
            Kind: ChartKind.Column,
            Title: "Quarterly Sales",
            Categories: ["Q1", "Q2", "Q3", "Q4"],
            Series: [new ChartSeriesDataPreset("Sales", [12d, 18d, 16d, 24d])]),
        new(
            Name: "Monthly Revenue",
            Kind: ChartKind.Line,
            Title: "Monthly Revenue",
            Categories: ["Jan", "Feb", "Mar"],
            Series: [new ChartSeriesDataPreset("Revenue", [5d, 6d, 7d])]),
    ];

    public static Chart CreateDefaultInsertion() => DefaultInsertion.CreateChart();

    public static bool TryCreateNamedReplacement(string? name, out Chart chart)
    {
        chart = null!;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var normalized = name.Trim();
        var preset = NamedReplacements.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, normalized, StringComparison.Ordinal));
        if (preset is null)
            return false;

        chart = preset.CreateChart();
        return true;
    }
}
