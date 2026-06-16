namespace FreeW.App.Host;

/// <summary>
/// FreeW's Word-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX, proving the ribbon library is app-neutral.
/// </summary>
internal static class FreeWRibbon
{
    public static RibbonDefinition Build() =>
        new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("clipboard", "Clipboard", "C", 100, g =>
                {
                    g.Button("freew.paste", "Paste");
                    g.Button("freew.cut", "Cut");
                    g.Button("freew.copy", "Copy");
                    g.Button("freew.format-painter", "Format Painter");
                });
                tab.Group("font", "Font", "F", 90, g =>
                {
                    g.ComboBox("freew.font-family", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Times New Roman", "Georgia", "Consolas", "Verdana", "Cambria" },
                        Width = 140
                    });
                    g.ComboBox("freew.font-size", "Size", c => c with
                    {
                        Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "24", "28", "36", "48", "72" },
                        Width = 56
                    });
                    g.Toggle("freew.bold", "Bold");
                    g.Toggle("freew.italic", "Italic");
                    g.Toggle("freew.underline", "Underline");
                    g.Button("freew.superscript", "Superscript");
                    g.Button("freew.subscript", "Subscript");
                    g.Button("freew.smallcaps", "Small Caps");
                    g.Button("freew.allcaps", "All Caps");
                    g.Button("freew.font-color", "Text Colour");
                    g.Button("freew.highlight", "Highlight");
                    g.Button("freew.grow-font", "Grow");
                    g.Button("freew.shrink-font", "Shrink");
                });
                tab.Group("paragraph", "Paragraph", "P", 80, g =>
                {
                    g.Button("freew.bullets", "Bullets");
                    g.Button("freew.numbering", "Numbering");
                    g.Button("freew.align-left", "Align Left");
                    g.Button("freew.align-center", "Center");
                    g.Button("freew.align-right", "Align Right");
                    g.ComboBox("freew.line-spacing", "Line Spacing", c => c with
                    {
                        Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                        Width = 56
                    });
                    g.Button("freew.space-before-toggle", "Space Before");
                    g.Button("freew.space-after-toggle", "Space After");
                    g.Button("freew.para-border", "Border");
                    g.Button("freew.para-shading", "Shading");
                });
                tab.Group("styles", "Styles", "S", 70, g =>
                {
                    g.Button("freew.style-normal", "Normal");
                    g.Button("freew.style-heading1", "Heading 1");
                    g.Button("freew.style-title", "Title");
                });
            })
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("pages", "Pages", "P", 100, g =>
                {
                    g.Button("freew.cover-page", "Cover Page");
                    g.Button("freew.blank-page", "Blank Page");
                    g.Button("freew.page-break", "Page Break");
                });
                tab.Group("tables", "Tables", "T", 90, g => g.Button("freew.table", "Table"));
                tab.Group("table-tools", "Table Tools", "B", 85, g =>
                {
                    g.Button("freew.table-insert-row", "Insert Row");
                    g.Button("freew.table-delete-row", "Delete Row");
                    g.Button("freew.table-insert-col", "Insert Column");
                    g.Button("freew.table-delete-col", "Delete Column");
                });
                tab.Group("illustrations", "Illustrations", "I", 80, g =>
                {
                    g.Button("freew.picture", "Picture");
                    g.Button("freew.image-size", "Image Size");
                    g.Button("freew.shapes", "Shapes");
                });
                tab.Group("links", "Links", "K", 70, g =>
                {
                    g.Button("freew.hyperlink", "Link");
                    g.Button("freew.bookmark", "Bookmark");
                    g.Button("freew.link-bookmark", "Link to Bookmark");
                });
                tab.Group("header-footer", "Header & Footer", "H", 60, g =>
                {
                    g.Button("freew.header", "Header");
                    g.Button("freew.footer", "Footer");
                    g.Button("freew.page-number", "Page Number");
                });
            })
            .Tab("layout", "Layout", "L", tab =>
            {
                tab.Group("page-setup", "Page Setup", "P", 100, g =>
                {
                    g.Button("freew.margins", "Margins");
                    g.Button("freew.orientation", "Orientation");
                    g.Button("freew.size", "Size");
                });
                tab.Group("preview", "Preview", "V", 90, g =>
                {
                    g.Button("freew.print-preview", "Print Preview");
                });
            })
            .Build();
}
