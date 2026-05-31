using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowWorksheetContextMenuSourceTests
{
    [Fact]
    public void InsertDeleteContextMenuActionsRouteToExistingWorksheetMutationCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.WorksheetContextMenu.cs"));

        source.Should().Contain("case WorksheetContextMenuAction.InsertCells:");
        source.Should().Contain("InsertCellsMenuItem_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.InsertRowAbove:");
        source.Should().Contain("InsertRows(address.Row);");
        source.Should().Contain("case WorksheetContextMenuAction.InsertRowBelow:");
        source.Should().Contain("InsertRows(address.Row + 1);");
        source.Should().Contain("case WorksheetContextMenuAction.InsertColumnLeft:");
        source.Should().Contain("InsertColumns(address.Col);");
        source.Should().Contain("case WorksheetContextMenuAction.InsertColumnRight:");
        source.Should().Contain("InsertColumns(address.Col + 1);");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteCells:");
        source.Should().Contain("DeleteCellsMenuItem_Click(this, new RoutedEventArgs());");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteRows:");
        source.Should().Contain("DeleteSelectedRows();");
        source.Should().Contain("case WorksheetContextMenuAction.DeleteColumns:");
        source.Should().Contain("DeleteSelectedColumns();");
    }

    [Fact]
    public void ObjectContextMenuActionsRouteToExistingPictureShapeAndAltTextCommands()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.WorksheetContextMenu.cs"));

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
    public void GridContextMenuClearsTransientCellUiBeforeOpeningMenu()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.WorksheetContextMenu.cs"));

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
}
