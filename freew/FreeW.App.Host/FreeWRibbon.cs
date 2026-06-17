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
                    g.Icon("freew.cover-page", "Cover Page", RibbonCommandIconKind.CoverPage);
                    g.Icon("freew.blank-page", "Blank Page", RibbonCommandIconKind.OnePage);
                    g.Icon("freew.horizontal-rule", "Horizontal Rule", RibbonCommandIconKind.HorizontalRule);
                    g.Icon("freew.page-break", "Page Break", RibbonCommandIconKind.PageBreak);
                    g.Icon("freew.drop-cap", "Drop Cap", RibbonCommandIconKind.DropCap);
                });
                tab.Group("tables", "Tables", "T", 90, g => g.Large("freew.table", "Table", RibbonCommandIconKind.Table, dropdown: true));
                tab.Group("table-tools", "Table Tools", "B", 85, g =>
                {
                    g.Icon("freew.table-insert-row", "Insert Row", RibbonCommandIconKind.Insert, RibbonCommandIconAccent.Green);
                    g.Icon("freew.table-delete-row", "Delete Row", RibbonCommandIconKind.Delete);
                    g.Icon("freew.table-insert-col", "Insert Column", RibbonCommandIconKind.Insert, RibbonCommandIconAccent.Green);
                    g.Icon("freew.table-delete-col", "Delete Column", RibbonCommandIconKind.Delete);
                    g.RowBreak();
                    g.Icon("freew.cell-shading", "Cell Shading", RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Fill);
                    g.Icon("freew.merge-cells", "Merge Cells", RibbonCommandIconKind.Merge);
                    g.Icon("freew.split-cell", "Split Cell", RibbonCommandIconKind.Grid);
                    g.Icon("freew.table-header-row", "Header Row", RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
                    g.Icon("freew.table-banded-rows", "Banded Rows", RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
                    g.Icon("freew.table-repeat-header", "Repeat Header", RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
                });
                tab.Group("illustrations", "Illustrations", "I", 80, g =>
                {
                    g.Large("freew.picture", "Picture", RibbonCommandIconKind.Picture);
                    g.Icon("freew.image-size", "Image Size", RibbonCommandIconKind.Size);
                    g.Icon("freew.image-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                    g.Icon("freew.image-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.RowBreak();
                    g.Icon("freew.image-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                    g.Icon("freew.image-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    // Shapes gallery: a dropdown of the preset shape kinds, each inserting the matching
                    // Shape via DocumentView.InsertShape (the items dispatch their own freew.shape-* ids).
                    g.Icon("freew.shapes", "Shapes", RibbonCommandIconKind.Shapes, "SH", menu: m =>
                    {
                        m.Item("freew.shape-rectangle", "Rectangle", "R");
                        m.Item("freew.shape-rounded", "Rounded Rectangle", "O");
                        m.Item("freew.shape-ellipse", "Ellipse", "E");
                        m.Item("freew.shape-textbox", "Text Box", "T");
                    });
                });
                tab.Group("media", "Media", "M", 78, g =>
                {
                    g.Icon("freew.equation", "Equation", RibbonCommandIconKind.Equation);
                    g.Icon("freew.chart", "Chart", RibbonCommandIconKind.ChartColumn, RibbonCommandIconAccent.Chart);
                    g.Icon("freew.wordart", "WordArt", RibbonCommandIconKind.WordArt);
                    g.Icon("freew.smartart", "SmartArt", RibbonCommandIconKind.SmartArt);
                    g.Icon("freew.object", "Object", RibbonCommandIconKind.Object);
                });
                tab.Group("links", "Links", "K", 70, g =>
                {
                    g.Icon("freew.hyperlink", "Link", RibbonCommandIconKind.Link);
                    g.Icon("freew.edit-hyperlink", "Edit Hyperlink", RibbonCommandIconKind.Link);
                    g.Icon("freew.remove-hyperlink", "Remove Hyperlink", RibbonCommandIconKind.Link);
                    g.Icon("freew.hyperlink-tooltip", "ScreenTip", RibbonCommandIconKind.Info);
                    g.Icon("freew.bookmark", "Bookmark", RibbonCommandIconKind.Bookmark);
                    g.Icon("freew.link-bookmark", "Link to Bookmark", RibbonCommandIconKind.Bookmark);
                    g.Icon("freew.bookmark-manager", "Bookmark Manager", RibbonCommandIconKind.Bookmark);
                });
                tab.Group("quick-parts", "Quick Parts", "Q", 67, g =>
                {
                    g.Icon("freew.save-quickpart", "Save Selection", RibbonCommandIconKind.QuickParts);
                    g.Icon("freew.insert-quickpart", "Insert Quick Part", RibbonCommandIconKind.QuickParts);
                    g.Icon("freew.insert-file", "Text from File", RibbonCommandIconKind.TextFromFile);
                });
                tab.Group("references", "References", "R", 65, g =>
                {
                    g.Icon("freew.footnote", "Footnote", RibbonCommandIconKind.Footnote);
                    g.Icon("freew.endnote", "Endnote", RibbonCommandIconKind.Endnote);
                    g.Icon("freew.toc", "Table of Contents", RibbonCommandIconKind.TableOfContents);
                    g.Icon("freew.toc-refresh", "Update TOC", RibbonCommandIconKind.Refresh);
                    g.Icon("freew.citation", "Citation", RibbonCommandIconKind.Citation);
                    g.ComboBox("freew.citation-style", "Citation Style", c => c with
                    {
                        Items = new[] { "APA", "MLA", "Chicago" },
                        Width = 90
                    });
                    g.RowBreak();
                    g.Icon("freew.bibliography", "Bibliography", RibbonCommandIconKind.Bibliography);
                    g.Icon("freew.caption", "Caption", RibbonCommandIconKind.Caption);
                    g.Icon("freew.cross-reference", "Cross-reference", RibbonCommandIconKind.CrossReference);
                    g.Icon("freew.index-mark", "Mark Entry", RibbonCommandIconKind.Index);
                    g.Icon("freew.index-insert", "Insert Index", RibbonCommandIconKind.Index);
                    g.Icon("freew.tof", "Table of Figures", RibbonCommandIconKind.TableOfContents);
                    g.Icon("freew.tof-refresh", "Update Figures", RibbonCommandIconKind.Refresh);
                });
                tab.Group("controls", "Controls", "O", 62, g =>
                {
                    g.Icon("freew.cc-text", "Text Control", RibbonCommandIconKind.TextBox);
                    g.Icon("freew.cc-checkbox", "Check Box", RibbonCommandIconKind.CheckBox);
                });
                tab.Group("header-footer", "Header & Footer", "H", 60, g =>
                {
                    g.Icon("freew.header", "Header", RibbonCommandIconKind.Header);
                    g.Icon("freew.footer", "Footer", RibbonCommandIconKind.Footer);
                    g.Icon("freew.page-number", "Page Number", RibbonCommandIconKind.PageNumber);
                });
                tab.Group("symbols", "Symbols", "Y", 50, g =>
                {
                    g.Icon("freew.symbol", "Symbol", RibbonCommandIconKind.Symbol);
                    g.Icon("freew.datetime", "Date & Time", RibbonCommandIconKind.Date);
                    g.Icon("freew.field", "Field", RibbonCommandIconKind.Field);
                });
            })
            .Tab("layout", "Layout", "L", tab =>
            {
                tab.Group("page-setup", "Page Setup", "P", 100, g =>
                {
                    g.Large("freew.margins", "Margins", RibbonCommandIconKind.Margins, dropdown: true);
                    g.Icon("freew.orientation", "Orientation", RibbonCommandIconKind.Orientation, dropdown: true);
                    g.Icon("freew.size", "Size", RibbonCommandIconKind.OnePage, dropdown: true);
                    g.Icon("freew.columns", "Columns", RibbonCommandIconKind.TextColumns, dropdown: true);
                    g.RowBreak();
                    g.Icon("freew.line-numbers", "Line Numbers", RibbonCommandIconKind.Number);
                    g.Icon("freew.hyphenation", "Hyphenation", RibbonCommandIconKind.Hyphenation);
                    g.Icon("freew.page-valign", "Vertical Align", RibbonCommandIconKind.AlignJustify);
                    g.Icon("freew.different-first-page", "Different First Page", RibbonCommandIconKind.CoverPage);
                });
                tab.Group("page-background", "Page Background", "B", 95, g =>
                {
                    g.Icon("freew.page-border", "Page Border", RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border);
                    g.Icon("freew.watermark", "Watermark", RibbonCommandIconKind.Watermark);
                });
                tab.Group("preview", "Preview", "V", 90, g =>
                {
                    g.Large("freew.print-preview", "Print Preview", RibbonCommandIconKind.Print);
                });
                tab.Group("data", "Data", "D", 88, g =>
                {
                    g.Icon("freew.sort", "Sort", RibbonCommandIconKind.Sort);
                    g.Icon("freew.text-to-table", "Text to Table", RibbonCommandIconKind.Table, RibbonCommandIconAccent.Green);
                    g.Icon("freew.table-to-text", "Table to Text", RibbonCommandIconKind.TextFunction);
                });
            })
            .Tab("design", "Design", "G", tab =>
            {
                tab.Group("themes", "Document Formatting", "T", 100, g =>
                {
                    g.ComboBox("freew.theme", "Themes", c => c with
                    {
                        Items = DocumentTheme.Catalog.Select(t => t.Name).ToArray(),
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme),
                        Width = 140
                    });
                });
            })
            .Tab("view", "View", "W", tab =>
            {
                tab.Group("views", "Views", "V", 100, g =>
                {
                    g.IconToggle("freew.print-layout", "Print Layout", RibbonCommandIconKind.PrintLayout);
                    g.IconToggle("freew.read-mode", "Read Mode", RibbonCommandIconKind.ReadMode);
                });
                tab.Group("show", "Show", "S", 90, g =>
                {
                    g.IconToggle("freew.nav-pane", "Navigation Pane", RibbonCommandIconKind.NavigationPane);
                    g.IconToggle("freew.formatting-marks", "Show ¶", RibbonCommandIconKind.FormattingMarks);
                });
            })
            .Tab("mailings", "Mailings", "M", tab =>
            {
                tab.Group("merge-data", "Start Mail Merge", "D", 100, g =>
                {
                    g.Large("freew.merge-data", "Set Data", RibbonCommandIconKind.Recipients);
                });
                tab.Group("merge-write", "Write & Insert Fields", "W", 90, g =>
                {
                    g.Icon("freew.merge-field", "Insert Merge Field", RibbonCommandIconKind.MergeField);
                });
                tab.Group("merge-preview", "Preview Results", "P", 80, g =>
                {
                    g.Icon("freew.merge-preview", "Preview Record", RibbonCommandIconKind.PreviewResults);
                });
                tab.Group("merge-finish", "Finish", "F", 70, g =>
                {
                    g.Large("freew.merge-finish", "Finish & Merge", RibbonCommandIconKind.FinishMerge);
                });
            })
            .Tab("review", "Review", "R", tab =>
            {
                tab.Group("proofing", "Proofing", "P", 100, g =>
                {
                    g.Large("freew.statistics", "Word Count", RibbonCommandIconKind.WordCount);
                    g.IconToggle("freew.spellcheck-toggle", "Spell Check", RibbonCommandIconKind.Spelling);
                    g.Icon("freew.add-to-dictionary", "Add to Dictionary", RibbonCommandIconKind.Book);
                });
                tab.Group("comments", "Comments", "C", 95, g =>
                {
                    g.Large("freew.new-comment", "New Comment", RibbonCommandIconKind.Comment);
                });
                tab.Group("tracking", "Tracking", "G", 90, g =>
                {
                    g.IconToggle("freew.track-changes", "Track Changes", RibbonCommandIconKind.History);
                    g.Icon("freew.accept-all", "Accept All", RibbonCommandIconKind.AcceptChange);
                    g.Icon("freew.reject-all", "Reject All", RibbonCommandIconKind.RejectChange);
                });
                tab.Group("protect", "Protect", "T", 85, g =>
                {
                    g.IconToggle("freew.restrict-editing", "Restrict Editing", RibbonCommandIconKind.Protect);
                });
                tab.Group("compare", "Compare", "M", 80, g =>
                {
                    g.Icon("freew.compare", "Compare", RibbonCommandIconKind.Compare);
                });
                tab.Group("inspect", "Inspect", "I", 75, g =>
                {
                    g.Icon("freew.inspect-document", "Inspect Document", RibbonCommandIconKind.Search);
                    g.Icon("freew.check-accessibility", "Check Accessibility", RibbonCommandIconKind.Accessibility);
                });
            })
            .Build();
    }
}
