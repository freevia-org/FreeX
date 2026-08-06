using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowWorksheetContextMenuSourceTests
{
    [Fact]
    public void InsertDeleteContextMenuActionsRouteToExistingWorksheetMutationCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");

        source.Should().Contain("WorkbookApplicationCommandIntent.InsertCells");
        source.Should().Contain("InsertCellsMenuItem_Click(this, new RoutedEventArgs())");
        source.Should().Contain("WorkbookApplicationCommandIntent.InsertRowAbove");
        source.Should().Contain("InsertRows(TargetAddress(invocation).Row)");
        source.Should().Contain("WorkbookApplicationCommandIntent.InsertRowBelow");
        source.Should().Contain("InsertRows(TargetAddress(invocation).Row + 1)");
        source.Should().Contain("WorkbookApplicationCommandIntent.InsertColumnLeft");
        source.Should().Contain("InsertColumns(TargetAddress(invocation).Col)");
        source.Should().Contain("WorkbookApplicationCommandIntent.InsertColumnRight");
        source.Should().Contain("InsertColumns(TargetAddress(invocation).Col + 1)");
        source.Should().Contain("WorkbookApplicationCommandIntent.DeleteCells");
        source.Should().Contain("DeleteCellsMenuItem_Click(this, new RoutedEventArgs())");
        source.Should().Contain("WorkbookApplicationCommandIntent.DeleteRows");
        source.Should().Contain("DeleteSelectedRows()");
        source.Should().Contain("WorkbookApplicationCommandIntent.DeleteColumns");
        source.Should().Contain("DeleteSelectedColumns()");
    }

    [Fact]
    public void ObjectContextMenuActionsRouteToExistingPictureShapeAndAltTextCommands()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        source.Should().Contain("case WorksheetContextMenuAction.FormatPicture:");
        source.Should().Contain("PictureSizeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.CropPicture:");
        source.Should().Contain("PictureCropBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ResetPictureCrop:");
        source.Should().Contain("PictureResetCropMenuItem_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.FormatDrawingObject:");
        source.Should().Contain("case WorksheetContextMenuAction.ResizeDrawingObject:");
        source.Should().Contain("ObjectSizeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.RotateDrawingObject:");
        source.Should().Contain("ObjectRotateBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ShapeFill:");
        source.Should().Contain("ObjectFillBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ShapeOutline:");
        source.Should().Contain("ObjectOutlineBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.FormatChartArea:");
        source.Should().Contain("FormatChartAreaBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.SelectChartData:");
        source.Should().Contain("SelectChartDataSourceBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ChangeChartType:");
        source.Should().Contain("ChangeChartTypeBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ChartStyles:");
        source.Should().Contain("ChartStylesBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ChartTitles:");
        source.Should().Contain("ChartTitlesBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.ChartSizeAndProperties:");
        source.Should().Contain("ResizeSelectedChartObject();");
        source.Should().Contain("case WorksheetContextMenuAction.MoveChart:");
        source.Should().Contain("MoveChartBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.BringForward:");
        source.Should().Contain("BringForwardBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.SendBackward:");
        source.Should().Contain("SendBackwardBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.EditAltText:");
        source.Should().Contain("SetAltTextBtn_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.SelectionPane:");
        source.Should().Contain("SelectionPaneBtn_Click(this, new RoutedEventArgs());");
    }

    [Fact]
    public void ObjectContextMenuTargeting_UsesSelectedObjectOrExactAnchorWithoutFallback()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("GetSelectedWorksheetContextMenuTargetKind(sheet, address)");
        source.Should().Contain("DrawingTargetResolver.GetTargetPicture(sheet, address, allowFallback: false)");
        source.Should().Contain("allowFallback: false)?.Kind switch");
        source.Should().Contain("includePictures: true");
        source.Should().Contain("target.Anchor.Row != address.Row");
        source.Should().Contain("DrawingObjectTargetKind.Picture => WorksheetContextMenuTargetKind.Picture");
        source.Should().Contain("SheetGrid.SelectedObjectKind == FreeX.App.UI.ObjectKind.Chart");
        source.Should().Contain("ChartWorkflowTargetPlanner.HasSelectedChart(sheet, GetSelectedChartIdOnCurrentSheet())");
        source.Should().Contain("WorksheetContextMenuTargetKind.Chart");
    }

    [Fact]
    public void GridContextMenuClearsTransientCellUiBeforeOpeningMenu()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        var contextMenuRequested = source[
            source.IndexOf("private void OnGridContextMenuRequested", StringComparison.Ordinal)..
            source.IndexOf("private void OnGridHeaderContextMenuRequested", StringComparison.Ordinal)];

        contextMenuRequested.Should().Contain("HideValidationDropdown();");
        contextMenuRequested.Should().Contain("ClearCommentPreview();");
        contextMenuRequested.IndexOf("HideValidationDropdown();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(contextMenuRequested.IndexOf("var targetKind = GetWorksheetContextMenuTargetKind(actualAddr);", StringComparison.Ordinal));
        contextMenuRequested.IndexOf("ClearCommentPreview();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(contextMenuRequested.IndexOf("var targetKind = GetWorksheetContextMenuTargetKind(actualAddr);", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextMenuStateSkipsValidationLookupWhenSheetHasNoValidationRules()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        var stateMethod = source[
            source.IndexOf("private WorksheetContextMenuState GetWorksheetContextMenuState", StringComparison.Ordinal)..];

        stateMethod.Should().Contain("sheet.DataValidations.Count > 0 &&");
        stateMethod.IndexOf("sheet.DataValidations.Count > 0 &&", StringComparison.Ordinal)
            .Should()
            .BeLessThan(stateMethod.IndexOf("DataValidationService.GetApplicable(sheet, address)", StringComparison.Ordinal));
    }

    [Fact]
    public void WorksheetContextMenuItemsExposeStableAutomationNamesAndIds()
    {
        // The worksheet context menu now renders from the shared ribbon-menu model via
        // WorksheetContextMenuRenderer, which preserves the prior automation contract.
        var source = DialogSourceTestSupport.ReadHostSources("WorksheetContextMenuRenderer.cs");

        source.Should().Contain("AutomationProperties.SetName(menuItem, cleanHeader);");
        source.Should().Contain("AutomationProperties.SetAutomationId(");
        source.Should().Contain("WorksheetContextMenu_{action}");
        source.Should().Contain("WorksheetContextMenu_{NormalizeAutomationId(cleanHeader)}");
    }

    [Fact]
    public void PivotTableContextMenuStateUsesClickedCell()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.WorksheetContextMenu.cs");

        var stateMethod = source[
            source.IndexOf("private WorksheetContextMenuState GetWorksheetContextMenuState", StringComparison.Ordinal)..];

        stateMethod.Should().Contain("PivotUiPlanner.FindPivotTableContainingCell(sheet, address) is not null");
        stateMethod.Should().Contain("HasPivotTableTarget: hasPivotTableTarget");
    }

    [Fact]
    public void PivotTableOptionsContextMenuActionRoutesToClickedPivotDialog()
    {
        var contextSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ApplicationCommandRouting.cs");
        var designSource = DialogSourceTestSupport.ReadHostSources("MainWindow.PivotDesignCommands.cs");

        contextSource.Should().Contain("WorkbookApplicationCommandIntent.PivotTableOptions");
        contextSource.Should().Contain("ShowPivotTableOptionsDialog(TargetAddress(invocation))");
        designSource.Should().Contain("private void ShowPivotTableOptionsDialog(CellAddress address)");
        designSource.Should().Contain("PivotUiPlanner.FindPivotTableContainingCell(sheet, address)");
        designSource.Should().Contain("private void ShowPivotTableOptionsDialog(PivotTableModel pivotTable)");
        designSource.Should().Contain("new PivotTableOptionsDialog(pivotTable, cache)");
    }
}
