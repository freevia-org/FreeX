using System;
using System.Windows;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void OnSelectedObjectContextChanged(object? sender, EventArgs e)
    {
        RefreshDrawingObjectContextualTabs();
        RefreshChartContextualTabs();
        RefreshTableContextualTab();
        RefreshPivotFieldListPaneAfterSelectionChange();
        if (SheetGrid.IsPictureCropMode && GetSelectedPictureOnSheet(_workbook.GetSheet(_currentSheetId))?.Kind != PictureKind.Image)
            SheetGrid.IsPictureCropMode = false;
    }

    private void RefreshDrawingObjectContextualTabs()
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var selectedTarget = GetSelectedDrawingObjectContextualTarget(sheet);
        var shapeVisible = selectedTarget?.Kind is DrawingObjectTargetKind.Shape or DrawingObjectTargetKind.TextBox;
        var picture = GetSelectedPictureOnSheet(sheet);
        var pictureVisible = picture is not null;
        var selectedShape = selectedTarget?.Kind == DrawingObjectTargetKind.Shape;
        var canCropPicture = picture?.Kind == PictureKind.Image;

        // Enablement of the contextual Shape/Picture ribbon buttons flows through the neutral state
        // store, which drives the rendered controls. The Crop Picture button is store-disabled when
        // cropping is unavailable, which gates access to its Crop / Reset Crop dropdown items.
        _ribbonState.SetEnabled("Shape Gradient", selectedShape);
        _ribbonState.SetEnabled("Shape Effects", selectedShape);
        _ribbonState.SetEnabled("Crop Picture", canCropPicture);

        SetDrawingObjectContextualTabsVisible(shapeVisible, pictureVisible);
    }

    private DrawingObjectTarget? GetSelectedDrawingObjectContextualTarget(Sheet? sheet)
    {
        var selectedKind = GetSelectedDrawingObjectTargetKind();
        if (selectedKind is null || SheetGrid.SelectedObjectId == Guid.Empty)
            return null;

        return DrawingTargetResolver.GetTargetDrawingObject(
            sheet,
            SheetGrid.SelectedRange?.Start,
            selectedKind,
            SheetGrid.SelectedObjectId,
            includePictures: true,
            allowFallback: false);
    }

    private PictureModel? GetSelectedPictureOnSheet(Sheet? sheet)
    {
        if (sheet is null ||
            SheetGrid.SelectedObjectKind != FreeX.App.UI.ObjectKind.Picture ||
            SheetGrid.SelectedObjectId == Guid.Empty)
            return null;

        foreach (var picture in sheet.Pictures)
        {
            if (picture.Id == SheetGrid.SelectedObjectId && picture.IsVisible)
                return picture;
        }

        return null;
    }

    private void SetDrawingObjectContextualTabsVisible(bool shapeVisible, bool pictureVisible)
    {
        if (ShapeFormatTab is not null)
            ShapeFormatTab.Visibility = shapeVisible ? Visibility.Visible : Visibility.Collapsed;
        if (PictureFormatTab is not null)
            PictureFormatTab.Visibility = pictureVisible ? Visibility.Visible : Visibility.Collapsed;

        if (RibbonTabs is not null &&
            ((!shapeVisible && ReferenceEquals(RibbonTabs.SelectedItem, ShapeFormatTab)) ||
             (!pictureVisible && ReferenceEquals(RibbonTabs.SelectedItem, PictureFormatTab))))
        {
            RibbonTabs.SelectedIndex = 1;
        }

        InvalidateVisibleKeyTipElementCache();
    }
}
