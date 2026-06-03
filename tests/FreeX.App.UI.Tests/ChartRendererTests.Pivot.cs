using System.Globalization;
using System.IO;
using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

public sealed partial class ChartRendererTests
{
    [Fact]
    public void PivotChartRenderer_AddsFieldButtonAnnotations()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Sum of Amount"),
                Cell(2, 1, "East"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .Contain(["PivotTable1", "Axis Fields", "Values"]);
    }

    [Fact]
    public void PivotChartFieldButtons_AddAnnotationsWithoutCaptionList()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "ChartRenderer.Annotations.cs"));
        var fieldButtons = source[
            source.IndexOf("private static void AddPivotChartFieldButtons", StringComparison.Ordinal)..
            source.IndexOf("private static void AddPivotChartFieldButtonAnnotation", StringComparison.Ordinal)];

        fieldButtons.Should().Contain("var index = 0;");
        fieldButtons.Should().Contain("AddPivotChartFieldButtonAnnotation(");
        fieldButtons.Should().NotContain("new List<string>");
        fieldButtons.Should().NotContain("captions.Add(");
        fieldButtons.Should().NotContain("captions.Count");
    }

    [Fact]
    public void PivotChartRenderer_HidesFieldButtonAnnotationsWhenDisabled()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Sum of Amount"),
                Cell(2, 1, "East"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .NotContain(["PivotTable1", "Axis Fields", "Values"]);
    }

    [Fact]
    public void PivotChartRenderer_HidesIndividualFieldButtonAnnotations()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartValueFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2))
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Region"),
                Cell(1, 2, "Sum of Amount"),
                Cell(2, 1, "East"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        model.Annotations
            .OfType<TextAnnotation>()
            .Select(annotation => annotation.Text)
            .Should()
            .Contain(["PivotTable1", "Axis Fields"])
            .And
            .NotContain("Values");
    }

    [Fact]
    public void GridView_HitTestsPivotChartFieldButtons()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 116),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "PivotTable1"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Axis Fields"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(428, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Values"));
    }

    [Fact]
    public void GridView_HitTestsPivotChartFieldButtonBoundaries()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(296, 134),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "PivotTable1"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(264, 392),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Axis Fields"));

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(524, 392),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Values"));
    }

    [Fact]
    public void GridView_DoesNotHitTestHiddenPivotChartFieldButtons()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 116),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GridView_DoesNotHitTestPivotChartFieldButtonsOutsideChartBounds()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 40,
            Height = 120
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(185, 116),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .BeNull();
    }

    [Fact]
    public void GridView_DoesNotHitTestIndividuallyHiddenPivotChartFieldButtons()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartValueFieldButtons = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            Left = 100,
            Top = 80,
            Width = 400,
            Height = 300
        };

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(428, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .BeNull();

        GridView.HitTestPivotChartFieldButton(
                [chart],
                new System.Windows.Point(148, 374),
                rowHeaderWidth: 40,
                columnHeaderHeight: 24)
            .Should()
            .Be((chart, "Axis Fields"));
    }
}
