namespace FreeP.App.Host;

/// <summary>
/// FreeP's minimal PowerPoint-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX and FreeW, proving the ribbon library is app-neutral. Deliberately tiny
/// (Home + Insert with stub commands); the real presentation ribbon (Design / Transitions / Animations /
/// Slide Show) is for the presentation-domain session.
/// </summary>
internal static class FreePRibbon
{
    public static RibbonDefinition Build()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("slides", "Slides", "S", 100, g =>
                {
                    // New Slide is the hero; the rest are compact stubs, mirroring PowerPoint's Slides group.
                    g.Large("freep.new-slide", "New Slide", RibbonCommandIconKind.Insert, "N");
                    g.Medium("freep.duplicate-slide", "Duplicate Slide", RibbonCommandIconKind.Copy, "D");
                    g.Medium("freep.delete-slide", "Delete Slide", RibbonCommandIconKind.Delete, "X");
                    g.Medium("freep.layout", "Layout", RibbonCommandIconKind.Grid, "L");
                });
                tab.Group("clipboard", "Clipboard", "C", 90, g =>
                {
                    g.Large("freep.paste", "Paste", RibbonCommandIconKind.Paste, "V");
                    g.Medium("freep.cut", "Cut", RibbonCommandIconKind.Cut, "T");
                    g.Medium("freep.copy", "Copy", RibbonCommandIconKind.Copy, "C");
                });
                tab.Group("font", "Font", "F", 80, g =>
                {
                    g.ComboBox("freep.font-family", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Segoe UI", "Georgia", "Verdana" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 140
                    });
                    g.IconToggle("freep.bold", "Bold", RibbonCommandIconKind.Bold, "1");
                    g.IconToggle("freep.italic", "Italic", RibbonCommandIconKind.Italic, "2");
                    g.IconToggle("freep.underline", "Underline", RibbonCommandIconKind.Underline, "3");
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
                    g.Large("freep.picture", "Picture", RibbonCommandIconKind.Picture, "P");
                    g.Medium("freep.shape-rectangle", "Rectangle", RibbonCommandIconKind.Rectangle, "R");
                    g.Medium("freep.shape-ellipse", "Ellipse", RibbonCommandIconKind.Ellipse, "E");
                });
            })
            .Build();
    }
}
