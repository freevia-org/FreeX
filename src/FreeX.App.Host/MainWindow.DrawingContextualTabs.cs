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
        if (SheetGrid.IsPictureCropMode && !GetDrawingObjectContextualRibbonPlan().CropPictureEnabled)
            SheetGrid.IsPictureCropMode = false;
    }

    private void RefreshDrawingObjectContextualTabs()
    {
        var plan = GetDrawingObjectContextualRibbonPlan();

        // Enablement of the contextual Shape/Picture ribbon buttons flows through the neutral state
        // store, which drives the rendered controls. The Crop Picture button is store-disabled when
        // cropping is unavailable, which gates access to its Crop / Reset Crop dropdown items.
        _ribbonState.SetEnabled(DrawingObjectContextualRibbonPlanner.ShapeGradientCommandName, plan.ShapeGradientEnabled);
        _ribbonState.SetEnabled(DrawingObjectContextualRibbonPlanner.ShapeEffectsCommandName, plan.ShapeEffectsEnabled);
        _ribbonState.SetEnabled(DrawingObjectContextualRibbonPlanner.CropPictureCommandName, plan.CropPictureEnabled);
        var currentShapeEffect = GetTargetDrawingShape(_currentSheetId)?.GetEffectiveEffectPreset()
            ?? DrawingShapeEffectPreset.None;
        foreach (var commandState in DrawingObjectContextualRibbonPlanner.BuildShapeEffectCommandStates(
                     currentShapeEffect,
                     plan.ShapeEffectsEnabled))
        {
            _ribbonState.SetState(commandState.CommandId, commandState.State);
        }

        SetDrawingObjectContextualTabsVisible(plan.ShapeFormatVisible, plan.PictureFormatVisible);
    }

    private DrawingObjectContextualRibbonPlan GetDrawingObjectContextualRibbonPlan() =>
        DrawingObjectContextualRibbonPlanner.Build(
            _workbook.GetSheet(_currentSheetId),
            SheetGrid.SelectedRange?.Start,
            GetSelectedDrawingObjectSelectionKind(),
            SheetGrid.SelectedObjectId);

    private PictureModel? GetSelectedPictureOnSheet(Sheet? sheet)
    {
        return DrawingTargetResolver.ResolveSelectedPicture(
                sheet,
                GetSelectedDrawingObjectSelectionKind(),
                SheetGrid.SelectedObjectId)
            .Target;
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
