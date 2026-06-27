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
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("file", "File", "F", 100, g =>
                {
                    g.Large("freep.file.new", "New", RibbonCommandIconKind.Insert, "N");
                    g.Large("freep.file.open", "Open", RibbonCommandIconKind.Refresh, "O");
                    g.Large("freep.file.save", "Save", RibbonCommandIconKind.Save, "S");
                    g.Medium("freep.file.save-as", "Save As", RibbonCommandIconKind.Save, "A");
                });
                tab.Group("slides", "Slides", "S", 90, g =>
                {
                    g.Large("freep.new-slide", "New Slide", RibbonCommandIconKind.Insert, "I");
                    g.Medium("freep.duplicate-slide", "Duplicate Slide", RibbonCommandIconKind.Copy, "D");
                    g.Medium("freep.delete-slide", "Delete Slide", RibbonCommandIconKind.Delete, "X");
                });
                tab.Group("edit", "Edit", "E", 80, g =>
                {
                    g.Large("freep.undo", "Undo", RibbonCommandIconKind.Undo, "U");
                    g.Large("freep.redo", "Redo", RibbonCommandIconKind.Redo, "R");
                });
                tab.Group("slideshow", "Slide Show", "W", 70, g =>
                {
                    g.Large("freep.slideshow.from-beginning", "From Beginning",
                        RibbonCommandIconKind.Next, "B");
                    g.Large("freep.slideshow.from-current", "From Current Slide",
                        RibbonCommandIconKind.Next, "C");
                });
            })
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("text", "Text", "T", 100, g =>
                {
                    g.Large("freep.text-box", "Text Box", RibbonCommandIconKind.TextBox, "X");
                });
                tab.Group("illustrations", "Illustrations", "I", 90, g =>
                {
                    g.Medium("freep.shape-rectangle", "Rectangle", RibbonCommandIconKind.Rectangle, "R");
                    g.Medium("freep.shape-ellipse", "Ellipse", RibbonCommandIconKind.Ellipse, "E");
                });
            })
            .Build();
    }
}
