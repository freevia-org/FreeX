using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ChartDataTableModelCloneTests
{
    [Fact]
    public void Clone_CopiesEveryFormattingFieldIntoIndependentModel()
    {
        var source = new ChartDataTableModel
        {
            ShowHorizontalBorder = false,
            ShowVerticalBorder = true,
            ShowOutline = false,
            ShowLegendKeys = true,
            FillColor = new CellColor(10, 20, 30),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            BorderColor = new CellColor(40, 50, 60),
            BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            BorderThickness = 2.5,
            TextColor = new CellColor(70, 80, 90),
            TextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            FontSize = 13.5
        };

        var clone = source.Clone();

        clone.Should().NotBeSameAs(source);
        clone.Should().BeEquivalentTo(source);

        clone.ShowLegendKeys = false;
        clone.FillColor = null;
        source.ShowLegendKeys.Should().BeTrue();
        source.FillColor.Should().Be(new CellColor(10, 20, 30));
    }

    [Fact]
    public void CloneCoverageGuard_TracksEveryPublicPropertyAndBothProductionCopyPaths()
    {
        var expectedProperties = new[]
        {
            nameof(ChartDataTableModel.ShowHorizontalBorder),
            nameof(ChartDataTableModel.ShowVerticalBorder),
            nameof(ChartDataTableModel.ShowOutline),
            nameof(ChartDataTableModel.ShowLegendKeys),
            nameof(ChartDataTableModel.FillColor),
            nameof(ChartDataTableModel.FillThemeColor),
            nameof(ChartDataTableModel.BorderColor),
            nameof(ChartDataTableModel.BorderThemeColor),
            nameof(ChartDataTableModel.BorderThickness),
            nameof(ChartDataTableModel.TextColor),
            nameof(ChartDataTableModel.TextThemeColor),
            nameof(ChartDataTableModel.FontSize)
        };
        var actualProperties = typeof(ChartDataTableModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);

        actualProperties.Should().BeEquivalentTo(expectedProperties,
            because: "a new data-table property must be added to the canonical clone and its exhaustive test");

        var configureSource = ModelSourceTestSupport.ReadCommandsSource("ConfigurePivotChartOptionsCommand.cs");
        configureSource.Should().Contain("chart.DataTable?.Clone()");
        configureSource.Should().Contain("_previousDataTable?.Clone()");
        configureSource.Should().NotContain("CloneDataTable(");

        var duplicateSource = ModelSourceTestSupport.ReadCommandsSource("DuplicateSheetDrawingCloner.cs");
        duplicateSource.Should().Contain("DataTable = chart.DataTable?.Clone()");
        duplicateSource.Should().NotContain("new ChartDataTableModel");
    }
}
