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
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("text", "Text", "T", 100, g =>
                {
                    g.Large("freep.text-box", "Text Box", RibbonCommandIconKind.TextBox, "X");
                });
                tab.Group("tables", "Tables", "A", 95, g =>
                {
                    g.Large("freep.insert-table-3x3", "Table", RibbonCommandIconKind.Table, "T");
                    g.Medium("freep.insert-table-2x2", "2x2", RibbonCommandIconKind.Table, "2");
                    g.Medium("freep.insert-table-4x4", "4x4", RibbonCommandIconKind.Table, "4");
                });
                tab.Group("charts", "Charts", "H", 93, g =>
                {
                    g.Medium("freep.insert-chart-column", "Column", RibbonCommandIconKind.ChartColumn, "C");
                    g.Medium("freep.insert-chart-bar", "Bar", RibbonCommandIconKind.ChartColumn, "B");
                    g.Medium("freep.insert-chart-line", "Line", RibbonCommandIconKind.ChartLine, "L");
                    g.Medium("freep.insert-chart-pie", "Pie", RibbonCommandIconKind.ChartPie, "P");
                });
                tab.Group("illustrations", "Illustrations", "I", 90, g =>
                {
                    g.Large("freep.picture", "Picture", RibbonCommandIconKind.Picture, "P");
                    g.Medium("freep.shape-rectangle", "Rectangle", RibbonCommandIconKind.Rectangle, "R");
                    g.Medium("freep.shape-ellipse", "Ellipse", RibbonCommandIconKind.Ellipse, "E");
                });
            })
            .Build();
    }
}
