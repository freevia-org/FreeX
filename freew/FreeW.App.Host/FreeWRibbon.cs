using System.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// FreeW's Word-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX, proving the ribbon library is app-neutral.
/// </summary>
internal static class FreeWRibbon
{
    public static RibbonDefinition Build()
    {
        static RibbonButton Icon(RibbonButton button, RibbonCommandIconKind kind, RibbonCommandIconAccent accent = RibbonCommandIconAccent.None) =>
            button with { Icon = new RibbonCommandIcon(kind, accent) };

        return new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", tab =>
            {
                tab.Group("clipboard", "Clipboard", "C", 100, g =>
                {
                    // Paste is the hero (Large); the rest stack as labelled medium buttons, like Word.
                    g.Large("freew.paste", "Paste", RibbonCommandIconKind.Paste, "V");
                    g.Medium("freew.cut", "Cut", RibbonCommandIconKind.Cut, "X");
                    g.Medium("freew.copy", "Copy", RibbonCommandIconKind.Copy, "C");
                    g.Medium("freew.format-painter", "Format Painter", RibbonCommandIconKind.FormatPainter, "FP");
                    g.Icon("freew.paste-plain", "Paste Text Only", RibbonCommandIconKind.Paste);
                    g.Icon("freew.paste-merge", "Merge Formatting", RibbonCommandIconKind.Paste);
                });
                tab.Group("font", "Font", "F", 90, g =>
                {
                    // Row 1: the font name + size combos. Row 2+: compact icon-only buttons, exactly like Word.
                    g.ComboBox("freew.font-family", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Times New Roman", "Georgia", "Consolas", "Verdana", "Cambria" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 140
                    });
                    g.ComboBox("freew.font-size", "Size", c => c with
                    {
                        Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "24", "28", "36", "48", "72" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 56
                    });
                    g.Icon("freew.grow-font", "Grow Font", RibbonCommandIconKind.ArrowUp);
                    g.Icon("freew.shrink-font", "Shrink Font", RibbonCommandIconKind.ArrowDown);
                    g.RowBreak();
                    g.IconToggle("freew.bold", "Bold", RibbonCommandIconKind.Bold, "1");
                    g.IconToggle("freew.italic", "Italic", RibbonCommandIconKind.Italic, "2");
                    g.IconToggle("freew.underline", "Underline", RibbonCommandIconKind.Underline, "3");
                    g.Icon("freew.subscript", "Subscript", RibbonCommandIconKind.Subscript);
                    g.Icon("freew.superscript", "Superscript", RibbonCommandIconKind.Superscript);
                    g.Icon("freew.change-case", "Change Case", RibbonCommandIconKind.ChangeCase);
                    g.Icon("freew.smallcaps", "Small Caps", RibbonCommandIconKind.Font);
                    g.Icon("freew.allcaps", "All Caps", RibbonCommandIconKind.Font);
                    g.Icon("freew.highlight", "Text Highlight Colour", RibbonCommandIconKind.Highlight);
                    g.Icon("freew.font-color", "Font Colour", RibbonCommandIconKind.FontColor);
                    g.Icon("freew.clear-formatting", "Clear All Formatting", RibbonCommandIconKind.Clear);
                });
                tab.Group("paragraph", "Paragraph", "P", 80, g =>
                {
                    // Row 1: list + indent + spacing. Row 2: alignment + shading/borders. Compact icon-only, Word-style.
                    g.Icon("freew.bullets", "Bullets", RibbonCommandIconKind.Bullets, dropdown: true);
                    g.Icon("freew.numbering", "Numbering", RibbonCommandIconKind.NumberedList, dropdown: true);
                    g.Icon("freew.multilevel-list", "Multilevel List", RibbonCommandIconKind.MultilevelList, dropdown: true);
                    g.Icon("freew.indent-decrease", "Decrease Indent", RibbonCommandIconKind.IndentDecrease);
                    g.Icon("freew.indent-increase", "Increase Indent", RibbonCommandIconKind.IndentIncrease);
                    g.RowBreak();
                    g.Icon("freew.align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.Icon("freew.align-center", "Center", RibbonCommandIconKind.AlignCenter);
                    g.Icon("freew.align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    g.Icon("freew.align-justify", "Justify", RibbonCommandIconKind.AlignJustify);
                    g.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", c => c with
                    {
                        Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.LineSpacing),
                        Width = 52
                    });
                    g.Icon("freew.para-shading", "Shading", RibbonCommandIconKind.Fill);
                    g.Icon("freew.para-border", "Borders", RibbonCommandIconKind.Border);
                    g.Icon("freew.space-before-toggle", "Add Space Before Paragraph", RibbonCommandIconKind.SpaceBefore);
                    g.Icon("freew.space-after-toggle", "Add Space After Paragraph", RibbonCommandIconKind.SpaceAfter);
                    g.Icon("freew.paragraph-dialog", "Paragraph Settings", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.keep-with-next", "Keep with Next", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.keep-lines", "Keep Lines Together", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.widow-control", "Widow/Orphan Control", RibbonCommandIconKind.TextFunction);
                });
                tab.Group("styles", "Styles", "S", 70, g =>
                {
                    g.ComboBox("freew.style", "Style", c => c with
                    {
                        Items = new[] { "Normal", "Heading 1", "Heading 2", "Heading 3", "Title", "Subtitle", "Quote" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
                        Width = 130
                    });
                    g.Button("freew.style-normal", "Normal", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-heading1", "Heading 1", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-title", "Title", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.new-style", "New Style", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.manage-styles", "Manage Styles", b => Icon(b, RibbonCommandIconKind.TextBox));
                });
            })
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("pages", "Pages", "P", 100, g =>
                {
                    g.Button("freew.cover-page", "Cover Page", b => Icon(b, RibbonCommandIconKind.Page));
                    g.Button("freew.blank-page", "Blank Page", b => Icon(b, RibbonCommandIconKind.Page));
                    g.Button("freew.horizontal-rule", "Horizontal Rule", b => Icon(b, RibbonCommandIconKind.Line));
                    g.Button("freew.page-break", "Page Break", b => Icon(b, RibbonCommandIconKind.PageBreak));
                    g.Button("freew.drop-cap", "Drop Cap", b => Icon(b, RibbonCommandIconKind.Font));
                });
                tab.Group("tables", "Tables", "T", 90, g => g.Button("freew.table", "Table", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green)));
                tab.Group("table-tools", "Table Tools", "B", 85, g =>
                {
                    g.Button("freew.table-insert-row", "Insert Row", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                    g.Button("freew.table-delete-row", "Delete Row", b => Icon(b, RibbonCommandIconKind.Delete));
                    g.Button("freew.table-insert-col", "Insert Column", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                    g.Button("freew.table-delete-col", "Delete Column", b => Icon(b, RibbonCommandIconKind.Delete));
                    g.Button("freew.cell-shading", "Cell Shading", b => Icon(b, RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill));
                    g.Button("freew.merge-cells", "Merge Cells", b => Icon(b, RibbonCommandIconKind.Merge));
                    g.Button("freew.split-cell", "Split Cell", b => Icon(b, RibbonCommandIconKind.Grid));
                    g.Button("freew.table-header-row", "Header Row", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                    g.Button("freew.table-banded-rows", "Banded Rows", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                    g.Button("freew.table-repeat-header", "Repeat Header", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                });
                tab.Group("illustrations", "Illustrations", "I", 80, g =>
                {
                    g.Button("freew.picture", "Picture", b => Icon(b, RibbonCommandIconKind.Picture));
                    g.Button("freew.image-size", "Image Size", b => Icon(b, RibbonCommandIconKind.Size));
                    g.Button("freew.image-alt-text", "Alt Text", b => Icon(b, RibbonCommandIconKind.Info));
                    g.Button("freew.image-align-left", "Align Left", b => Icon(b, RibbonCommandIconKind.Align));
                    g.Button("freew.image-align-center", "Align Center", b => Icon(b, RibbonCommandIconKind.Align));
                    g.Button("freew.image-align-right", "Align Right", b => Icon(b, RibbonCommandIconKind.Align));
                    // Shapes gallery: a dropdown of the preset shape kinds, each inserting the matching
                    // Shape via DocumentView.InsertShape (the items dispatch their own freew.shape-* ids).
                    g.Medium("freew.shapes", "Shapes", RibbonCommandIconKind.Rectangle, "SH", menu: m =>
                    {
                        m.Item("freew.shape-rectangle", "Rectangle", "R");
                        m.Item("freew.shape-rounded", "Rounded Rectangle", "O");
                        m.Item("freew.shape-ellipse", "Ellipse", "E");
                        m.Item("freew.shape-textbox", "Text Box", "T");
                    });
                });
                tab.Group("media", "Media", "M", 78, g =>
                {
                    g.Button("freew.equation", "Equation", b => Icon(b, RibbonCommandIconKind.Function));
                    g.Button("freew.chart", "Chart", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                    g.Button("freew.wordart", "WordArt", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.smartart", "SmartArt", b => Icon(b, RibbonCommandIconKind.Group));
                    g.Button("freew.object", "Object", b => Icon(b, RibbonCommandIconKind.Grid));
                });
                tab.Group("links", "Links", "K", 70, g =>
                {
                    g.Button("freew.hyperlink", "Link");
                    g.Button("freew.edit-hyperlink", "Edit Hyperlink");
                    g.Button("freew.remove-hyperlink", "Remove Hyperlink");
                    g.Button("freew.hyperlink-tooltip", "ScreenTip");
                    g.Button("freew.bookmark", "Bookmark");
                    g.Button("freew.link-bookmark", "Link to Bookmark");
                    g.Button("freew.bookmark-manager", "Bookmark Manager");
                });
                tab.Group("quick-parts", "Quick Parts", "Q", 67, g =>
                {
                    g.Button("freew.save-quickpart", "Save Selection");
                    g.Button("freew.insert-quickpart", "Insert Quick Part");
                    g.Button("freew.insert-file", "Text from File");
                });
                tab.Group("references", "References", "R", 65, g =>
                {
                    g.Button("freew.footnote", "Footnote");
                    g.Button("freew.endnote", "Endnote");
                    g.Button("freew.toc", "Table of Contents");
                    g.Button("freew.toc-refresh", "Update TOC");
                    g.Button("freew.citation", "Citation");
                    g.ComboBox("freew.citation-style", "Citation Style", c => c with
                    {
                        Items = new[] { "APA", "MLA", "Chicago" },
                        Width = 90
                    });
                    g.Button("freew.bibliography", "Bibliography");
                    g.Button("freew.caption", "Caption");
                    g.Button("freew.cross-reference", "Cross-reference");
                    g.Button("freew.index-mark", "Mark Entry");
                    g.Button("freew.index-insert", "Insert Index");
                    g.Button("freew.tof", "Table of Figures");
                    g.Button("freew.tof-refresh", "Update Figures");
                });
                tab.Group("controls", "Controls", "O", 62, g =>
                {
                    g.Button("freew.cc-text", "Text Control");
                    g.Button("freew.cc-checkbox", "Check Box");
                });
                tab.Group("header-footer", "Header & Footer", "H", 60, g =>
                {
                    g.Button("freew.header", "Header");
                    g.Button("freew.footer", "Footer");
                    g.Button("freew.page-number", "Page Number");
                });
                tab.Group("symbols", "Symbols", "Y", 50, g =>
                {
                    g.Button("freew.symbol", "Symbol");
                    g.Button("freew.datetime", "Date & Time");
                    g.Button("freew.field", "Field");
                });
            })
            .Tab("layout", "Layout", "L", tab =>
            {
                tab.Group("page-setup", "Page Setup", "P", 100, g =>
                {
                    g.Button("freew.margins", "Margins", b => Icon(b, RibbonCommandIconKind.Margins));
                    g.Button("freew.orientation", "Orientation", b => Icon(b, RibbonCommandIconKind.Orientation));
                    g.Button("freew.size", "Size", b => Icon(b, RibbonCommandIconKind.Page));
                    g.Button("freew.columns", "Columns", b => Icon(b, RibbonCommandIconKind.TextColumns));
                    g.Button("freew.line-numbers", "Line Numbers", b => Icon(b, RibbonCommandIconKind.Number));
                    g.Button("freew.hyphenation", "Hyphenation", b => Icon(b, RibbonCommandIconKind.TextFunction));
                    g.Button("freew.page-valign", "Vertical Align", b => Icon(b, RibbonCommandIconKind.Align));
                    g.Button("freew.different-first-page", "Different First Page", b => Icon(b, RibbonCommandIconKind.Page));
                });
                tab.Group("page-background", "Page Background", "B", 95, g =>
                {
                    g.Button("freew.page-border", "Page Border", b => Icon(b, RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border));
                    g.Button("freew.watermark", "Watermark", b => Icon(b, RibbonCommandIconKind.Page));
                });
                tab.Group("preview", "Preview", "V", 90, g =>
                {
                    g.Button("freew.print-preview", "Print Preview", b => Icon(b, RibbonCommandIconKind.Print));
                });
                tab.Group("data", "Data", "D", 88, g =>
                {
                    g.Button("freew.sort", "Sort", b => Icon(b, RibbonCommandIconKind.Sort));
                    g.Button("freew.text-to-table", "Text to Table", b => Icon(b, RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green));
                    g.Button("freew.table-to-text", "Table to Text", b => Icon(b, RibbonCommandIconKind.TextFunction));
                });
            })
            .Tab("design", "Design", "G", tab =>
            {
                tab.Group("themes", "Document Formatting", "T", 100, g =>
                {
                    g.ComboBox("freew.theme", "Themes", c => c with
                    {
                        Items = DocumentTheme.Catalog.Select(t => t.Name).ToArray(),
                        Width = 140
                    });
                });
            })
            .Tab("view", "View", "W", tab =>
            {
                tab.Group("show", "Show", "S", 100, g =>
                {
                    g.Toggle("freew.nav-pane", "Navigation Pane");
                    g.Toggle("freew.formatting-marks", "Show ¶");
                });
                tab.Group("views", "Views", "V", 90, g =>
                {
                    g.Toggle("freew.print-layout", "Print Layout");
                    g.Toggle("freew.read-mode", "Read Mode");
                });
            })
            .Tab("mailings", "Mailings", "M", tab =>
            {
                tab.Group("merge-data", "Start Mail Merge", "D", 100, g =>
                {
                    g.Button("freew.merge-data", "Set Data");
                });
                tab.Group("merge-write", "Write & Insert Fields", "W", 90, g =>
                {
                    g.Button("freew.merge-field", "Insert Merge Field");
                });
                tab.Group("merge-preview", "Preview Results", "P", 80, g =>
                {
                    g.Button("freew.merge-preview", "Preview Record");
                });
                tab.Group("merge-finish", "Finish", "F", 70, g =>
                {
                    g.Button("freew.merge-finish", "Finish & Merge");
                });
            })
            .Tab("review", "Review", "R", tab =>
            {
                tab.Group("proofing", "Proofing", "P", 100, g =>
                {
                    g.Button("freew.statistics", "Word Count");
                    g.Toggle("freew.spellcheck-toggle", "Spell Check");
                    g.Button("freew.add-to-dictionary", "Add to Dictionary");
                });
                tab.Group("comments", "Comments", "C", 100, g =>
                {
                    g.Button("freew.new-comment", "New Comment");
                });
                tab.Group("tracking", "Tracking", "G", 90, g =>
                {
                    g.Toggle("freew.track-changes", "Track Changes");
                    g.Button("freew.accept-all", "Accept All");
                    g.Button("freew.reject-all", "Reject All");
                });
                tab.Group("protect", "Protect", "T", 90, g =>
                {
                    g.Toggle("freew.restrict-editing", "Restrict Editing");
                });
                tab.Group("compare", "Compare", "M", 80, g =>
                {
                    g.Button("freew.compare", "Compare");
                });
                tab.Group("inspect", "Inspect", "I", 80, g =>
                {
                    g.Button("freew.inspect-document", "Inspect Document");
                    g.Button("freew.check-accessibility", "Check Accessibility", b => Icon(b, RibbonCommandIconKind.Accessibility));
                });
            })
            .Build();
    }
}
