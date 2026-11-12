using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// WPF projection of the new chart families (stacked / 100% stacked, radar, bubble, stock, combination):
/// each renders through the shared scene, survives the editor's commit, and is reachable from the
/// Change Chart Type path.
/// </summary>
public sealed class ChartFamilyRenderingTests
{
    private static Chart Family(ChartKind kind, ChartGrouping grouping = ChartGrouping.Standard)
    {
        var chart = new Chart { Kind = kind, Grouping = grouping, Title = "Family", ShowLegend = true };
        chart.Categories.AddRange(["1", "2", "3"]);
        chart.Series.Add(new ChartSeries("High", [12, 14, 13]));
        chart.Series.Add(new ChartSeries("Low", [8, 9, 7]));
        chart.Series.Add(new ChartSeries("Close", [10, 13, 9]) { PlotAsLine = kind == ChartKind.Combo, UseSecondaryAxis = kind == ChartKind.Combo });
        ChartKindNormalizer.Normalize(chart);
        return chart;
    }

    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject dependencyObject)
                continue;
            if (dependencyObject is T match)
                result.Add(match);
            result.AddRange(LogicalDescendants<T>(dependencyObject));
        }

        return result;
    }

    public static IEnumerable<object[]> FamilyCharts()
    {
        foreach (var kind in new[] { ChartKind.Column, ChartKind.Bar, ChartKind.Line, ChartKind.Area })
        {
            yield return [kind, ChartGrouping.Stacked];
            yield return [kind, ChartGrouping.PercentStacked];
        }

        foreach (var kind in new[] { ChartKind.Radar, ChartKind.Bubble, ChartKind.Stock, ChartKind.Combo })
            yield return [kind, ChartGrouping.Standard];
    }

    [StaTheory]
    [MemberData(nameof(FamilyCharts))]
    public void FamilyChart_Renders_AndSurvivesTheCommitRoundTrip(ChartKind kind, ChartGrouping grouping)
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        view.InsertChart(Family(kind, grouping));

        LogicalDescendants<FrameworkElement>(view.Document).Should().NotBeEmpty("the chart must draw something");
        var chart = view.Model.Paragraphs.SelectMany(paragraph => paragraph.Runs).Single(run => run.Chart is not null).Chart!;
        chart.Kind.Should().Be(kind);
        chart.Grouping.Should().Be(grouping);
        chart.Series.Should().HaveCount(3);
        if (kind == ChartKind.Combo)
            chart.Series[2].Should().Match<ChartSeries>(series => series.PlotAsLine && series.UseSecondaryAxis);
    }

    [StaFact]
    public void PercentStackedChart_LabelsItsValueAxisInPercent()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());

        view.InsertChart(Family(ChartKind.Column, ChartGrouping.PercentStacked));

        LogicalDescendants<TextBlock>(view.Document).Select(block => block.Text).Should().Contain("100%");
    }

    [StaFact]
    public void InsertChartDialog_OffersEveryKindAndStackedVariant_FromTheSharedPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "InsertChartDialog.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MediaDialogParity.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("InsertChartDialogPlanner.BuildChartTypeChoices(");
            source.Should().Contain("InsertChartDialogPlanner.ChartTypeChoiceAt(");
            source.Should().NotContain("Enum.GetValues<ChartKind>()");
        }

        InsertChartDialogPlanner.BuildChartTypeChoices(UiText.Get).Select(choice => choice.Label)
            .Should().OnlyHaveUniqueItems().And.NotContain(label => label.StartsWith("[[", StringComparison.Ordinal));
    }
}
