using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotAnalyzeCommandSourceTests
{
    [Theory]
    [InlineData("Show Details", "Show Details", "D", "PivotTableShowDetailsBtn_Click")]
    [InlineData("Field Settings", "Field Settings", "FS", "PivotFieldValueSettingsMenuItem_Click")]
    [InlineData("Group Field", "Group Field", "GF", "PivotGroupFieldBtn_Click")]
    [InlineData("Ungroup", "Ungroup", "UG", "PivotUngroupFieldBtn_Click")]
    [InlineData("Insert Slicer", "Insert Slicer", "IS", "PivotInsertSlicerBtn_Click")]
    [InlineData("Insert Timeline", "Insert Timeline", "IT", "PivotInsertTimelineBtn_Click")]
    [InlineData("Refresh", "Refresh", "R", "RefreshPivotTableBtn_Click")]
    [InlineData("Change Data Source", "Change Data Source", "CD", "PivotChangeDataSourceBtn_Click")]
    [InlineData("Calculated Field", "Calc Field", "CF", "PivotCalculatedFieldBtn_Click")]
    [InlineData("Calculated Item", "Calc Item", "CI", "PivotCalculatedItemBtn_Click")]
    [InlineData("PivotChart", "PivotChart", "PC", "PivotChartBtn_Click")]
    [InlineData("Change Chart Type", "Change Chart", "CT", "PivotChartChangeTypeBtn_Click")]
    [InlineData("PivotChart Options", "Chart Options", "CO", "PivotChartOptionsBtn_Click")]
    [InlineData("Field List", "Field List", "FL", "PivotFieldListBtn_Click")]
    public void PivotAnalyzeCommands_ExposeExpectedTitlesKeyTipsAndHandlers(
        string title,
        string content,
        string keyTip,
        string handler)
    {
        var button = ExtractButtonElementByTitle(ReadPivotAnalyzeTabXaml(), title);

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().Contain($"Click=\"{handler}\"");
    }

    [Theory]
    [InlineData("PivotTable Name", "PivotTable Name", "N")]
    [InlineData("PivotTable Options", "Options", "O")]
    [InlineData("Clear", "Clear", "CL")]
    [InlineData("Select", "Select", "SE")]
    [InlineData("Move PivotTable", "Move PivotTable", "M")]
    public void PivotAnalyzeDeferredCommands_RemainDisabledWithoutClickHandlers(
        string title,
        string content,
        string keyTip)
    {
        var button = ExtractButtonElementByTitle(ReadPivotAnalyzeTabXaml(), title);

        button.Should().Contain($"Content=\"{content}\"");
        button.Should().Contain("IsEnabled=\"False\"");
        button.Should().Contain($"local:RibbonTooltip.Title=\"{title}\"");
        button.Should().Contain($"local:RibbonTooltip.KeyTip=\"{keyTip}\"");
        button.Should().NotContain("Click=");
    }

    [Fact]
    public void PivotAnalyzeHandlers_RouteThroughExpectedPivotCommandsDialogsAndPanes()
    {
        var pivotSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotCommands.cs"));
        var advancedSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotAdvancedCommands.cs"));
        var chartSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.PivotChartCommands.cs"));

        pivotSource.Should().Contain("new RefreshPivotTableCommand(_currentSheetId, pivotTable.Name)");
        pivotSource.Should().Contain("new DrillDownPivotTableCommand(_currentSheetId, target.PivotTableName, target.PivotCell)");
        pivotSource.Should().Contain("PivotFieldListPane.Visibility = PivotFieldListPane.Visibility == Visibility.Visible");
        pivotSource.Should().Contain("new PivotTableDataSourceDialog(");
        pivotSource.Should().Contain("new ChangePivotTableSourceCommand(_currentSheetId, pivotTable.Name, sourceRange)");
        pivotSource.Should().Contain("new InsertSlicerDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddSlicerCommand(dialog.Result.SlicerName, pivotTable.Name, dialog.Result.FieldName)");
        pivotSource.Should().Contain("new InsertTimelineDialog(headers, fieldName)");
        pivotSource.Should().Contain("new AddTimelineCommand(dialog.Result.TimelineName, pivotTable.Name, dialog.Result.DateFieldName)");
        pivotSource.Should().Contain("new PivotValueFieldSettingsDialog(current, headers)");

        advancedSource.Should().Contain("new PivotFieldGroupingDialog(headers, currentField)");
        advancedSource.Should().Contain("PivotFieldGroupingDialog.CreateResult(");
        advancedSource.Should().Contain("new PivotCalculatedFieldDialog");
        advancedSource.Should().Contain("new PivotCalculatedItemDialog(headers, sourceIndex)");
        advancedSource.Should().Contain("new ConfigurePivotTableCalculatedItemsCommand(");

        chartSource.Should().Contain("new PivotChartTypeDialog(ChartType.Column)");
        chartSource.Should().Contain("new AddPivotChartCommand(_currentSheetId, pivotTable.Name, dialog.Result.ChartType");
        chartSource.Should().Contain("new ChangePivotChartTypeCommand(_currentSheetId, chart.Id, dialog.Result.ChartType)");
        chartSource.Should().Contain("new PivotChartOptionsDialog(chart)");
        chartSource.Should().Contain("new ConfigurePivotChartOptionsCommand(");
    }

    private static string ReadPivotAnalyzeTabXaml()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var start = xaml.IndexOf("Header=\"PivotTable Analyze\"", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the PivotTable Analyze contextual tab should be present");

        var end = xaml.IndexOf("x:Name=\"PivotTableDesignTab\"", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the PivotTable Design contextual tab should follow Analyze");
        return xaml[start..end];
    }

    private static string ExtractButtonElementByTitle(string xaml, string title)
    {
        var titleIndex = xaml.IndexOf($"local:RibbonTooltip.Title=\"{title}\"", StringComparison.Ordinal);
        titleIndex.Should().BeGreaterThanOrEqualTo(0, $"the {title} PivotTable Analyze command should be present");

        var start = xaml.LastIndexOf("<Button", titleIndex, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"the {title} PivotTable Analyze command should be a Button");

        var selfClosingEnd = xaml.IndexOf("/>", titleIndex, StringComparison.Ordinal);
        var closingEnd = xaml.IndexOf("</Button>", titleIndex, StringComparison.Ordinal);
        var end = closingEnd >= 0 && (selfClosingEnd < 0 || closingEnd < selfClosingEnd)
            ? closingEnd + "</Button>".Length
            : selfClosingEnd + 2;

        end.Should().BeGreaterThan(titleIndex, $"the {title} PivotTable Analyze button should have a closing marker");
        return xaml[start..end];
    }
}
