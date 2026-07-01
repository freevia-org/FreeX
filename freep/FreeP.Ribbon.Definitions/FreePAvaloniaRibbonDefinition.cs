using Free.Shared.Ribbon;

namespace FreeP.Ribbon.Definitions;

/// <summary>
/// FreeP ribbon definition for the cross-platform host. Command wiring stays in
/// the consuming app's registry; do not add per-command lambdas here.
/// </summary>
internal static class FreePAvaloniaRibbonDefinition
{
    internal static RibbonDefinition Build()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", FreePRibbonText.HomeTabLabel, FreePRibbonText.HomeTabKeyTip, tab =>
            {
                tab.Group("file", FreePRibbonText.FileGroupLabel, FreePRibbonText.FileGroupKeyTip, 100, g =>
                {
                    g.Large("freep.file.new", FreePRibbonText.FileNewLabel, RibbonCommandIconKind.Insert, FreePRibbonText.FileNewKeyTip);
                    g.Large("freep.file.open", FreePRibbonText.FileOpenLabel, RibbonCommandIconKind.Refresh, FreePRibbonText.FileOpenKeyTip);
                    g.Large("freep.file.save", FreePRibbonText.FileSaveLabel, RibbonCommandIconKind.Save, FreePRibbonText.FileSaveKeyTip);
                    g.Medium("freep.file.save-as", FreePRibbonText.FileSaveAsLabel, RibbonCommandIconKind.Save, FreePRibbonText.FileSaveAsKeyTip);
                });
                tab.Group("slides", FreePRibbonText.SlidesGroupLabel, FreePRibbonText.SlidesGroupKeyTip, 90, g =>
                {
                    g.Large("freep.new-slide", FreePRibbonText.NewSlideLabel, RibbonCommandIconKind.Insert, FreePRibbonText.NewSlideAvaloniaKeyTip);
                    g.Medium("freep.duplicate-slide", FreePRibbonText.DuplicateSlideLabel, RibbonCommandIconKind.Copy, FreePRibbonText.DuplicateSlideKeyTip);
                    g.Medium("freep.delete-slide", FreePRibbonText.DeleteSlideLabel, RibbonCommandIconKind.Delete, FreePRibbonText.DeleteSlideKeyTip);
                });
                tab.Group("clipboard", FreePRibbonText.ClipboardGroupLabel, FreePRibbonText.ClipboardGroupKeyTip, 88, g =>
                {
                    g.Large("freep.paste", FreePRibbonText.PasteLabel, RibbonCommandIconKind.Paste, FreePRibbonText.PasteKeyTip);
                    g.Medium("freep.cut", FreePRibbonText.CutLabel, RibbonCommandIconKind.Cut, FreePRibbonText.CutKeyTip);
                    g.Medium("freep.copy", FreePRibbonText.CopyLabel, RibbonCommandIconKind.Copy, FreePRibbonText.CopyKeyTip);
                    g.Medium("freep.format-painter", FreePRibbonText.FormatPainterLabel, RibbonCommandIconKind.FormatPainter, FreePRibbonText.FormatPainterKeyTip);
                });
                tab.Group("arrange", FreePRibbonText.ArrangeGroup.Label, FreePRibbonText.ArrangeGroup.KeyTip, 85, g =>
                {
                    g.Large("freep.arrange.group", FreePRibbonText.ArrangeGroupCommand.Label, RibbonCommandIconKind.Group, FreePRibbonText.ArrangeGroupCommand.KeyTip);
                    g.Medium("freep.arrange.ungroup", FreePRibbonText.ArrangeUngroupCommand.Label, RibbonCommandIconKind.Ungroup, FreePRibbonText.ArrangeUngroupCommand.KeyTip);
                    g.Separator();
                    g.Medium("freep.arrange.bring-to-front", FreePRibbonText.ArrangeBringToFrontCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringToFrontCommand.KeyTip);
                    g.Medium("freep.arrange.bring-forward", FreePRibbonText.ArrangeBringForwardCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringForwardCommand.KeyTip);
                    g.Medium("freep.arrange.send-backward", FreePRibbonText.ArrangeSendBackwardCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendBackwardCommand.KeyTip);
                    g.Medium("freep.arrange.send-to-back", FreePRibbonText.ArrangeSendToBackCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendToBackCommand.KeyTip);
                    g.Separator();
                    g.Medium("freep.arrange.align-left", FreePRibbonText.ArrangeAlignLeftCommand.Label, RibbonCommandIconKind.AlignLeft, FreePRibbonText.ArrangeAlignLeftCommand.KeyTip);
                    g.Medium("freep.arrange.align-center-h", FreePRibbonText.ArrangeAlignCenterHorizontalCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeAlignCenterHorizontalCommand.KeyTip);
                    g.Medium("freep.arrange.align-right", FreePRibbonText.ArrangeAlignRightCommand.Label, RibbonCommandIconKind.AlignRight, FreePRibbonText.ArrangeAlignRightCommand.KeyTip);
                    g.Medium("freep.arrange.align-top", FreePRibbonText.ArrangeAlignTopCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeAlignTopCommand.KeyTip);
                    g.Medium("freep.arrange.align-middle", FreePRibbonText.ArrangeAlignMiddleCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.ArrangeAlignMiddleCommand.KeyTip);
                    g.Medium("freep.arrange.align-bottom", FreePRibbonText.ArrangeAlignBottomCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeAlignBottomCommand.KeyTip);
                    g.Separator();
                    g.Medium("freep.arrange.distribute-h", FreePRibbonText.ArrangeDistributeHorizontalCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeDistributeHorizontalCommand.KeyTip);
                    g.Medium("freep.arrange.distribute-v", FreePRibbonText.ArrangeDistributeVerticalCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.ArrangeDistributeVerticalCommand.KeyTip);
                });
                tab.Group("edit", FreePRibbonText.EditGroupLabel, FreePRibbonText.EditGroupKeyTip, 80, g =>
                {
                    g.Large("freep.undo", FreePRibbonText.UndoLabel, RibbonCommandIconKind.Undo, FreePRibbonText.UndoKeyTip);
                    g.Large("freep.redo", FreePRibbonText.RedoLabel, RibbonCommandIconKind.Redo, FreePRibbonText.RedoKeyTip);
                });
                tab.Group("slideshow", FreePRibbonText.SlideShowGroupLabel, FreePRibbonText.SlideShowGroupAvaloniaKeyTip, 70, g =>
                {
                    g.Large("freep.slideshow.from-beginning", FreePRibbonText.SlideShowFromBeginningLabel,
                        RibbonCommandIconKind.Next, FreePRibbonText.SlideShowFromBeginningKeyTip);
                    g.Large("freep.slideshow.from-current", FreePRibbonText.SlideShowFromCurrentSlideLabel,
                        RibbonCommandIconKind.Next, FreePRibbonText.SlideShowFromCurrentSlideKeyTip);
                });
            })
            .Tab("insert", FreePRibbonText.InsertTabLabel, FreePRibbonText.InsertTabKeyTip, tab =>
            {
                tab.Group("text", FreePRibbonText.TextGroupLabel, FreePRibbonText.TextGroupKeyTip, 100, g =>
                {
                    g.Large("freep.text-box", FreePRibbonText.TextBoxLabel, RibbonCommandIconKind.TextBox, FreePRibbonText.TextBoxKeyTip);
                });
                tab.Group("tables", FreePRibbonText.TablesGroupLabel, FreePRibbonText.TablesGroupKeyTip, 95, g =>
                {
                    g.Large("freep.insert-table-3x3", FreePRibbonText.InsertTable3x3Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable3x3KeyTip);
                    g.Medium("freep.insert-table-2x2", FreePRibbonText.InsertTable2x2Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable2x2KeyTip);
                    g.Medium("freep.insert-table-4x4", FreePRibbonText.InsertTable4x4Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable4x4KeyTip);
                });
                tab.Group("charts", FreePRibbonText.ChartsGroupLabel, FreePRibbonText.ChartsGroupKeyTip, 93, g =>
                {
                    g.Medium("freep.insert-chart-column", FreePRibbonText.InsertChartColumnLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartColumnKeyTip);
                    g.Medium("freep.insert-chart-bar", FreePRibbonText.InsertChartBarLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartBarKeyTip);
                    g.Medium("freep.insert-chart-line", FreePRibbonText.InsertChartLineLabel, RibbonCommandIconKind.ChartLine, FreePRibbonText.InsertChartLineKeyTip);
                    g.Medium("freep.insert-chart-pie", FreePRibbonText.InsertChartPieLabel, RibbonCommandIconKind.ChartPie, FreePRibbonText.InsertChartPieKeyTip);
                    g.Medium("freep.chart.edit-data", FreePRibbonText.ChartEditDataLabel, RibbonCommandIconKind.ChartTitle, FreePRibbonText.ChartEditDataKeyTip);
                });
                tab.Group("illustrations", FreePRibbonText.IllustrationsGroupLabel, FreePRibbonText.IllustrationsGroupKeyTip, 90, g =>
                {
                    g.Large("freep.picture", FreePRibbonText.PictureLabel, RibbonCommandIconKind.Picture, FreePRibbonText.PictureKeyTip);
                    g.Medium("freep.shape-rectangle", FreePRibbonText.ShapeRectangleLabel, RibbonCommandIconKind.Rectangle, FreePRibbonText.ShapeRectangleKeyTip);
                    g.Medium("freep.shape-ellipse", FreePRibbonText.ShapeEllipseLabel, RibbonCommandIconKind.Ellipse, FreePRibbonText.ShapeEllipseKeyTip);
                });
            })
            .Build();
    }
}
