using System.Linq;
using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// FreeW's Word-style ribbon, authored with the shared <see cref="RibbonDefinitionBuilder"/> —
/// the same model that drives FreeX, proving the ribbon library is app-neutral.
/// </summary>
public static class FreeWRibbon
{
    public static RibbonDefinition Build(FreeWRibbonCapabilities? capabilities = null)
    {
        capabilities ??= FreeWRibbonCapabilities.Wpf;
        if (capabilities.UseAvaloniaBackedSurface)
            return FreeWAvaloniaRibbonDefinition.Build(capabilities);

        static RibbonButton Icon(RibbonButton button, RibbonCommandIconKind kind, RibbonCommandIconAccent accent = RibbonCommandIconAccent.None) =>
            button with { Icon = new RibbonCommandIcon(kind, accent) };
        static void ThemeMenu(string commandId, RibbonMenuBuilder menu)
        {
            foreach (var theme in DocumentTheme.Catalog)
                menu.Item(commandId, theme.Name, theme.Name[0].ToString());
            menu.Separator();
            menu.Item("freew.customize-colors", "Customize Colors…", "Z");
        }

        static void FontSetMenu(string commandId, RibbonMenuBuilder menu)
        {
            foreach (var fontSet in DocumentFontSet.Catalog)
                menu.Item(commandId, fontSet.Name, fontSet.Name[0].ToString());
            menu.Separator();
            menu.Item("freew.customize-fonts", "Customize Fonts…", "Z");
        }

        static void ParagraphSpacingMenu(string commandId, RibbonMenuBuilder menu)
        {
            foreach (var spacingSet in DocumentParagraphSpacingSet.Catalog)
                menu.Item(commandId, spacingSet.Name, spacingSet.Name[0].ToString());
            menu.Separator();
            menu.Item("freew.custom-paragraph-spacing", "Custom Paragraph Spacing…", "U");
        }

        static void EffectsMenu(string commandId, RibbonMenuBuilder menu)
        {
            foreach (var effectSet in DocumentEffectSet.Catalog)
                menu.Item(commandId, effectSet.Name, effectSet.Name[0].ToString());
        }

        var homeTab = FreeWRibbonText.HomeTab;
        var clipboardGroup = FreeWRibbonText.ClipboardGroup;
        var pasteCommand = FreeWRibbonText.PasteCommand;
        var cutCommand = FreeWRibbonText.CutCommand;
        var copyCommand = FreeWRibbonText.CopyCommand;
        var formatPainterCommand = FreeWRibbonText.FormatPainterCommand;
        var pasteTextOnlyCommand = FreeWRibbonText.PasteTextOnlyCommand;
        var pasteMergeFormattingCommand = FreeWRibbonText.PasteMergeFormattingCommand;
        var pasteSpecialCommand = FreeWRibbonText.PasteSpecialCommand;
        var fontGroup = FreeWRibbonText.FontGroup;
        var fontFamilyCommand = FreeWRibbonText.FontFamilyCommand;
        var fontSizeCommand = FreeWRibbonText.FontSizeCommand;
        var boldCommand = FreeWRibbonText.BoldCommand;
        var italicCommand = FreeWRibbonText.ItalicCommand;
        var underlineCommand = FreeWRibbonText.UnderlineCommand;
        var strikethroughCommand = FreeWRibbonText.StrikethroughCommand;
        var growFontCommand = FreeWRibbonText.GrowFontCommand;
        var shrinkFontCommand = FreeWRibbonText.ShrinkFontCommand;
        var subscriptCommand = FreeWRibbonText.SubscriptCommand;
        var superscriptCommand = FreeWRibbonText.SuperscriptCommand;
        var changeCaseCommand = FreeWRibbonText.ChangeCaseCommand;
        var smallCapsCommand = FreeWRibbonText.SmallCapsCommand;
        var allCapsCommand = FreeWRibbonText.AllCapsCommand;
        var textHighlightColorCommand = FreeWRibbonText.TextHighlightColorCommand;
        var fontColorCommand = FreeWRibbonText.FontColorCommand;
        var characterBorderCommand = FreeWRibbonText.CharacterBorderCommand;
        var characterShadingCommand = FreeWRibbonText.CharacterShadingCommand;
        var clearAllFormattingCommand = FreeWRibbonText.ClearAllFormattingCommand;
        var fontDialogCommand = FreeWRibbonText.FontDialogCommand;
        var paragraphGroup = FreeWRibbonText.ParagraphGroup;
        var bulletsCommand = FreeWRibbonText.BulletsCommand;
        var numberingCommand = FreeWRibbonText.NumberingCommand;
        var multilevelListCommand = FreeWRibbonText.MultilevelListCommand;
        var multilevelPromoteCommand = FreeWRibbonText.MultilevelPromoteCommand;
        var multilevelDemoteCommand = FreeWRibbonText.MultilevelDemoteCommand;
        var multilevelDefineCommand = FreeWRibbonText.MultilevelDefineCommand;
        var symbolsGroup = FreeWRibbonText.SymbolsGroup;
        var symbolCommand = FreeWRibbonText.SymbolCommand;
        var pageBackgroundGroup = FreeWRibbonText.PageBackgroundGroup;
        var watermarkCommand = FreeWRibbonText.WatermarkCommand;
        var pageColorCommand = FreeWRibbonText.PageColorCommand;
        var pageBordersCommand = FreeWRibbonText.PageBordersCommand;

        var definition = new RibbonDefinitionBuilder()
            .Tab("home", homeTab.Label, homeTab.KeyTip, tab =>
            {
                tab.Group("clipboard", clipboardGroup.Label, clipboardGroup.KeyTip, 100, g =>
                {
                    // Paste is the hero (Large); the rest stack as labelled medium buttons, like Word.
                    g.Large("freew.paste", pasteCommand.Label, RibbonCommandIconKind.Paste, pasteCommand.KeyTip);
                    g.Medium("freew.cut", cutCommand.Label, RibbonCommandIconKind.Cut, cutCommand.KeyTip);
                    g.Medium("freew.copy", copyCommand.Label, RibbonCommandIconKind.Copy, copyCommand.KeyTip);
                    g.Medium("freew.format-painter", formatPainterCommand.Label, RibbonCommandIconKind.FormatPainter, formatPainterCommand.KeyTip);
                    g.Icon("freew.paste-plain", pasteTextOnlyCommand.Label, RibbonCommandIconKind.Paste);
                    g.Icon("freew.paste-merge", pasteMergeFormattingCommand.Label, RibbonCommandIconKind.Paste);
                    // Paste Special: dialog offering Keep Source Formatting / Merge Formatting / Keep Text Only.
                    g.Icon("freew.paste-special", pasteSpecialCommand.Label, RibbonCommandIconKind.Paste);
                });
                tab.Group("font", fontGroup.Label, fontGroup.KeyTip, 90, g =>
                {
                    // Row 1: the font name + size combos. Row 2+: compact icon-only buttons, exactly like Word.
                    g.ComboBox("freew.font-family", fontFamilyCommand.Label, c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Times New Roman", "Georgia", "Consolas", "Verdana", "Cambria" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 140
                    });
                    g.ComboBox("freew.font-size", fontSizeCommand.Label, c => c with
                    {
                        Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "24", "28", "36", "48", "72" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
                        Width = 56
                    });
                    g.Icon("freew.grow-font", growFontCommand.Label, RibbonCommandIconKind.ArrowUp);
                    g.Icon("freew.shrink-font", shrinkFontCommand.Label, RibbonCommandIconKind.ArrowDown);
                    g.RowBreak();
                    g.IconToggle("freew.bold", boldCommand.Label, RibbonCommandIconKind.Bold, boldCommand.KeyTip);
                    g.IconToggle("freew.italic", italicCommand.Label, RibbonCommandIconKind.Italic, italicCommand.KeyTip);
                    g.IconToggle("freew.underline", underlineCommand.Label, RibbonCommandIconKind.Underline, underlineCommand.KeyTip);
                    g.Icon("freew.strikethrough", strikethroughCommand.Label, RibbonCommandIconKind.Strikethrough);
                    g.Icon("freew.subscript", subscriptCommand.Label, RibbonCommandIconKind.Subscript);
                    g.Icon("freew.superscript", superscriptCommand.Label, RibbonCommandIconKind.Superscript);
                    g.Icon("freew.change-case", changeCaseCommand.Label, RibbonCommandIconKind.ChangeCase);
                    g.Icon("freew.smallcaps", smallCapsCommand.Label, RibbonCommandIconKind.Font);
                    g.Icon("freew.allcaps", allCapsCommand.Label, RibbonCommandIconKind.Font);
                    g.Icon("freew.highlight", textHighlightColorCommand.Label, RibbonCommandIconKind.Highlight);
                    g.Icon("freew.font-color", fontColorCommand.Label, RibbonCommandIconKind.FontColor);
                    g.Icon("freew.char-border", characterBorderCommand.Label, RibbonCommandIconKind.Border);
                    g.Icon("freew.char-shading", characterShadingCommand.Label, RibbonCommandIconKind.Fill);
                    g.Icon("freew.clear-formatting", clearAllFormattingCommand.Label, RibbonCommandIconKind.Clear);
                    // Font dialog-launcher: opens the two-tab Font dialog (Font + Advanced tab with
                    // character spacing, kerning, position, ligatures, stylistic sets, number form/spacing).
                    g.Icon("freew.font-dialog", fontDialogCommand.Label, RibbonCommandIconKind.Font);
                });
                tab.Group("paragraph", paragraphGroup.Label, paragraphGroup.KeyTip, 80, g =>
                {
                    // Row 1: list + indent + spacing. Row 2: alignment + shading/borders. Compact icon-only, Word-style.
                    g.Icon("freew.bullets", bulletsCommand.Label, RibbonCommandIconKind.Bullets, dropdown: true);
                    g.Icon("freew.numbering", numberingCommand.Label, RibbonCommandIconKind.NumberedList, dropdown: true);
                    g.Icon("freew.multilevel-list", multilevelListCommand.Label, RibbonCommandIconKind.MultilevelList, dropdown: true, menu: m =>
                    {
                        m.Item("freew.multilevel-promote", multilevelPromoteCommand.Label, multilevelPromoteCommand.KeyTip);
                        m.Item("freew.multilevel-demote", multilevelDemoteCommand.Label, multilevelDemoteCommand.KeyTip);
                        // Predefined multilevel list presets (mirrors Word's gallery of 3 presets).
                        foreach (var (preset, idx) in FreeWRibbonDefinitionData.MultilevelListPresetNames.Select((p, i) => (p, i)))
                            m.Item($"freew.multilevel-preset-{idx}", preset, (idx + 1).ToString());
                        // Define New Multilevel List: opens a dialog to configure levels and start-at.
                        m.Item("freew.multilevel-define", multilevelDefineCommand.Label, multilevelDefineCommand.KeyTip);
                    });
                    g.Icon("freew.indent-decrease", "Decrease Indent", RibbonCommandIconKind.IndentDecrease);
                    g.Icon("freew.indent-increase", "Increase Indent", RibbonCommandIconKind.IndentIncrease);
                    g.RowBreak();
                    g.Icon("freew.align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.Icon("freew.align-center", "Center", RibbonCommandIconKind.AlignCenter);
                    g.Icon("freew.align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    g.Icon("freew.align-justify", "Justify", RibbonCommandIconKind.AlignJustify);
                    g.Icon("freew.sort", "Sort", RibbonCommandIconKind.Sort);
                    g.IconToggle("freew.formatting-marks", "Show ¶", RibbonCommandIconKind.FormattingMarks);
                    g.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", c => c with
                    {
                        Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.LineSpacing),
                        Width = 52
                    });
                    g.Icon("freew.para-shading", "Shading", RibbonCommandIconKind.Fill);
                    g.Icon("freew.para-border", "Borders", RibbonCommandIconKind.Border);
                    g.Icon("freew.borders-shading", "Borders and Shading…", RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border);
                    g.Icon("freew.space-before-toggle", "Add Space Before Paragraph", RibbonCommandIconKind.SpaceBefore);
                    g.Icon("freew.space-after-toggle", "Add Space After Paragraph", RibbonCommandIconKind.SpaceAfter);
                    g.Icon("freew.paragraph-dialog", "Paragraph Settings", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.tabs-dialog", "Tabs", RibbonCommandIconKind.Ruler);
                    g.Icon("freew.keep-with-next", "Keep with Next", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.keep-lines", "Keep Lines Together", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.widow-control", "Widow/Orphan Control", RibbonCommandIconKind.TextFunction);
                });
                tab.Group("styles", "Styles", "S", 65, g =>
                {
                    g.ComboBox("freew.style", "Style", c => c with
                    {
                        Items = new[] { "Normal", "Heading 1", "Heading 2", "Heading 3", "Title", "Subtitle", "Quote" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
                        Width = 130
                    });
                    g.Button("freew.style-normal", "Normal", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-heading1", "Heading 1", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-heading2", "Heading 2", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-heading3", "Heading 3", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-title", "Title", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.style-clear", "Clear Style", b => Icon(b, RibbonCommandIconKind.Clear));
                    g.Button("freew.new-style", "New Style", b => Icon(b, RibbonCommandIconKind.TextBox));
                    g.Button("freew.manage-styles", "Manage Styles", b => Icon(b, RibbonCommandIconKind.TextBox));
                });
                tab.Group("formatting", "Formatting", "M", 70, g =>
                {
                    g.MediumToggle("freew.reveal-formatting", "Reveal Formatting", RibbonCommandIconKind.Info);
                });
                tab.Group("editing", "Editing", "E", 75, g =>
                {
                    g.Medium("freew.undo", "Undo", RibbonCommandIconKind.Undo);
                    g.Medium("freew.redo", "Redo", RibbonCommandIconKind.Redo);
                    g.Medium("freew.find", "Find", RibbonCommandIconKind.Search, "F");
                    g.Medium("freew.replace", "Replace", RibbonCommandIconKind.Search, "R");
                    g.Medium("freew.select", "Select", RibbonCommandIconKind.Search, "SL");
                });
            })
            .Tab("insert", "Insert", "N", tab =>
            {
                tab.Group("pages", "Pages", "P", 100, g =>
                {
                    // Word shows the Pages group as labelled icon+label rows — use Medium so the labels read.
                    g.Medium("freew.cover-page", "Cover Page", RibbonCommandIconKind.CoverPage, menu: m =>
                    {
                        m.Item("freew.cover-page-default", "Default", "D");
                        m.Item("freew.cover-page-banded", "Banded", "B");
                        m.Item("freew.cover-page-motion", "Motion", "M");
                    });
                    g.Medium("freew.blank-page", "Blank Page", RibbonCommandIconKind.OnePage);
                    g.Medium("freew.page-break", "Page Break", RibbonCommandIconKind.PageBreak);
                    g.RowBreak();
                    g.Medium("freew.horizontal-rule", "Horizontal Rule", RibbonCommandIconKind.HorizontalRule);
                });
                // Single-command group → unmistakable Large hero button.
                tab.Group("tables", "Tables", "T", 90, g => g.Large("freew.table", "Table", RibbonCommandIconKind.Table, dropdown: true));
                tab.Group("illustrations", "Illustrations", "I", 88, g =>
                {
                    g.Medium("freew.picture", "Pictures", RibbonCommandIconKind.Picture);
                    // Shapes gallery: a dropdown of the preset shape kinds, each inserting the matching
                    // Shape via DocumentView.InsertShape (the items dispatch their own freew.shape-* ids).
                    g.Medium("freew.shapes", "Shapes", RibbonCommandIconKind.Shapes, "SH", menu: m =>
                    {
                        m.Item("freew.shape-rectangle", "Rectangle", "R");
                        m.Item("freew.shape-rounded", "Rounded Rectangle", "O");
                        m.Item("freew.shape-ellipse", "Ellipse", "E");
                        m.Item("freew.shape-textbox", "Text Box", "T");
                    });
                    g.Medium("freew.smartart", "SmartArt", RibbonCommandIconKind.SmartArt);
                    g.RowBreak();
                    g.Medium("freew.chart", "Chart", RibbonCommandIconKind.ChartColumn, accent: RibbonCommandIconAccent.Chart);
                    // Screenshot gallery: "Screen Clipping" drag-selects a screen region and inserts the
                    // capture as an inline image (same path as Insert Picture). The top-level id only opens
                    // the menu (no direct insert), mirroring the Shapes dropdown above.
                    g.Medium("freew.screenshot", "Screenshot", RibbonCommandIconKind.Picture, "SC", menu: m =>
                    {
                        m.Item("freew.screen-clipping", "Screen Clipping", "C");
                    });
                    // Icons picker: opens a searchable icon library and inserts the chosen icon as a
                    // rasterised InlineImage (same PNG path as Insert Picture / Screen Clipping).
                    g.Medium("freew.insert-icon", "Icons", RibbonCommandIconKind.Icons);
                });
                // Links stay compact so the backed Insert surfaces fit without hiding commands at normal
                // desktop widths.
                tab.Group("links", "Links", "K", 70, g =>
                {
                    g.Icon("freew.hyperlink", "Link", RibbonCommandIconKind.Link);
                    g.Icon("freew.bookmark", "Bookmark", RibbonCommandIconKind.Bookmark);
                    g.Icon("freew.cross-reference", "Cross-reference", RibbonCommandIconKind.CrossReference);
                    g.Icon("freew.edit-hyperlink", "Edit Hyperlink", RibbonCommandIconKind.Link);
                    g.RowBreak();
                    g.Icon("freew.remove-hyperlink", "Remove Hyperlink", RibbonCommandIconKind.Link);
                    g.Icon("freew.hyperlink-tooltip", "ScreenTip", RibbonCommandIconKind.Info);
                    g.Icon("freew.link-bookmark", "Link to Bookmark", RibbonCommandIconKind.Bookmark);
                    g.Icon("freew.bookmark-manager", "Bookmark Manager", RibbonCommandIconKind.Bookmark);
                });
                tab.Group("header-footer", "Header & Footer", "H", 60, g =>
                {
                    // Small group -> labelled Medium buttons, Word-style.
                    g.Medium("freew.header", "Header", RibbonCommandIconKind.Header);
                    g.Medium("freew.footer", "Footer", RibbonCommandIconKind.Footer);
                    g.Medium("freew.page-number", "Page Number", RibbonCommandIconKind.PageNumber, menu: m =>
                    {
                        m.Item("freew.page-number-top", "Top of Page", "T");
                        m.Item("freew.page-number-bottom", "Bottom of Page", "B");
                        m.Item("freew.page-number-current", "Current Position", "C");
                        m.Separator();
                        m.Item("freew.page-number-format", "Format Page Numbers…", "F");
                    });
                });
                tab.Group("text", "Text", "X", 74, g =>
                {
                    // Text Box gallery: Simple (plain), Sidebar/Banded (accent fill), and Quote (indented
                    // italic) presets — each inserts a pre-styled Shape.TextBox at the caret. The top-level
                    // id falls through to Simple (same as the existing plain text-box insert).
                    g.Icon("freew.shape-textbox", "Text Box", RibbonCommandIconKind.TextBox, menu: m =>
                    {
                        m.Item("freew.textbox-simple",  "Simple Text Box",         "S");
                        m.Item("freew.textbox-sidebar",  "Sidebar (Banded)",        "B");
                        m.Item("freew.textbox-quote",    "Quote",                   "Q");
                    });
                    // Quick Parts: a dropdown with Document Property sub-items + the existing AutoText entry.
                    g.Icon("freew.insert-quickpart", "Quick Parts", RibbonCommandIconKind.QuickParts, menu: m =>
                    {
                        m.Item("freew.docprop-title",    "Document Property: Title",    "T");
                        m.Item("freew.docprop-subject",  "Document Property: Subject",  "S");
                        m.Item("freew.docprop-author",   "Document Property: Author",   "A");
                        m.Item("freew.docprop-keywords", "Document Property: Keywords", "K");
                        m.Item("freew.docprop-comments", "Document Property: Comments", "C");
                        m.Separator();
                        m.Item("freew.field",             "Field…",                     "F");
                        m.Separator();
                        m.Item("freew.save-quickpart",    "Save Selection to Quick Part Gallery…", "V");
                        m.Item("freew.building-blocks-organizer", "Building Blocks Organizer…", "B");
                    });
                    g.Icon("freew.insert-file", "Text from File", RibbonCommandIconKind.TextFromFile);
                    g.Icon("freew.wordart", "WordArt", RibbonCommandIconKind.WordArt);
                    g.RowBreak();
                    // Drop Cap: top-level applies the default drop cap; dropdown opens the options dialog.
                    g.Icon("freew.drop-cap", "Drop Cap", RibbonCommandIconKind.DropCap, menu: m =>
                    {
                        m.Item("freew.drop-cap-dropped",   "Dropped",         "D");
                        m.Item("freew.drop-cap-in-margin", "In Margin",       "M");
                        m.Item("freew.drop-cap-none",      "None (Remove)",   "N");
                        m.Separator();
                        m.Item("freew.drop-cap-options",   "Drop Cap Options…", "O");
                    });
                    g.Icon("freew.datetime", "Date & Time", RibbonCommandIconKind.Date);
                    g.Icon("freew.field", "Field", RibbonCommandIconKind.Field);
                    g.Icon("freew.update-fields", "Update Fields", RibbonCommandIconKind.Refresh);
                    g.Icon("freew.toggle-field-codes", "Toggle Field Codes", RibbonCommandIconKind.Field);
                    g.Icon("freew.object", "Object", RibbonCommandIconKind.Object);
                    g.Icon("freew.save-quickpart", "Save Selection", RibbonCommandIconKind.QuickParts);
                    g.Icon("freew.building-blocks-organizer", "Building Blocks Organizer", RibbonCommandIconKind.QuickParts);
                });
                tab.Group("symbols", symbolsGroup.Label, symbolsGroup.KeyTip, 50, g =>
                {
                    // Equation gallery: the top-level id inserts the default sample equation (E = mc^2),
                    // and the dropdown offers Word's common structure presets.
                    g.Medium("freew.equation", "Equation", RibbonCommandIconKind.Equation, menu: m =>
                    {
                        m.Item("freew.equation-fraction", "Fraction", "F");
                        m.Item("freew.equation-script", "Subscript / Superscript", "S");
                        m.Item("freew.equation-radical", "Radical (Square Root)", "R");
                        m.Item("freew.equation-nthroot", "Radical (nth Root)", "N");
                        m.Item("freew.equation-integral", "Integral", "I");
                        m.Item("freew.equation-summation", "Summation", "U");
                        m.Item("freew.equation-product", "Product", "P");
                        m.Item("freew.equation-accent", "Accent (Hat)", "A");
                        m.Item("freew.equation-bar", "Overbar", "O");
                        m.Item("freew.equation-bracket", "Bracket", "B");
                        m.Item("freew.equation-matrix", "Matrix (2x2)", "M");
                        m.Item("freew.equation-func", "Function (sin)", "C");
                        m.Item("freew.equation-groupchr", "Group (brace)", "G");
                    });
                    g.Medium("freew.symbol", symbolCommand.Label, RibbonCommandIconKind.Symbol);
                });
            })
            .Tab("references", "References", "R", tab =>
            {
                tab.Group("table-of-contents", "Table of Contents", "T", 100, g =>
                {
                    g.Medium("freew.toc", "Table of Contents", RibbonCommandIconKind.TableOfContents);
                    g.Medium("freew.toc-add-text", "Add Text", RibbonCommandIconKind.TableOfContents, dropdown: true, menu: m =>
                    {
                        m.Item("freew.toc-addtext-none", "Do Not Show in Table of Contents", "N");
                        m.Separator();
                        m.Item("freew.toc-addtext-level1", "Level 1", "1");
                        m.Item("freew.toc-addtext-level2", "Level 2", "2");
                        m.Item("freew.toc-addtext-level3", "Level 3", "3");
                    });
                    g.Medium("freew.toc-refresh", "Update Table", RibbonCommandIconKind.Refresh);
                });
                tab.Group("footnotes", "Footnotes", "F", 92, g =>
                {
                    g.Medium("freew.footnote", "Insert Footnote", RibbonCommandIconKind.Footnote);
                    g.Medium("freew.endnote", "Insert Endnote", RibbonCommandIconKind.Endnote);
                    g.Medium("freew.next-footnote", "Next Footnote", RibbonCommandIconKind.Footnote, dropdown: true, menu: m =>
                    {
                        m.Item("freew.next-footnote", "Next Footnote", "N");
                        m.Item("freew.previous-footnote", "Previous Footnote", "P");
                        m.Separator();
                        m.Item("freew.next-endnote", "Next Endnote", "E");
                        m.Item("freew.previous-endnote", "Previous Endnote", "V");
                    });
                    g.Medium("freew.show-notes", "Show Notes", RibbonCommandIconKind.Footnote);
                    g.Medium("freew.footnote-endnote-options", "Footnote/Endnote Options…", RibbonCommandIconKind.Footnote);
                });
                tab.Group("citations", "Citations & Bibliography", "C", 84, g =>
                {
                    g.Medium("freew.citation", "Insert Citation", RibbonCommandIconKind.Citation);
                    g.Medium("freew.manage-sources", "Manage Sources", RibbonCommandIconKind.Citation);
                    g.ComboBox("freew.citation-style", "Style", c => c with
                    {
                        Items = FreeWRibbonDefinitionData.CitationStyleNames,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Citation),
                        Width = 90
                    });
                    g.Medium("freew.bibliography", "Bibliography", RibbonCommandIconKind.Bibliography);
                });
                tab.Group("captions", "Captions", "P", 78, g =>
                {
                    g.Medium("freew.caption", "Insert Caption", RibbonCommandIconKind.Caption, menu: m =>
                    {
                        m.Item("freew.insert-caption.figure", "Figure", "F");
                        m.Item("freew.insert-caption.table", "Table", "T");
                        m.Item("freew.insert-caption.equation", "Equation", "E");
                    });
                    g.Medium("freew.tof", "Insert Table of Figures", RibbonCommandIconKind.TableOfContents, menu: m =>
                    {
                        m.Item("freew.tof.figure", "Figure", "F");
                        m.Item("freew.tof.table", "Table", "T");
                        m.Item("freew.tof.equation", "Equation", "E");
                    });
                    g.Medium("freew.tof-refresh", "Update Table", RibbonCommandIconKind.Refresh);
                    g.Medium("freew.cross-reference", "Cross-reference", RibbonCommandIconKind.CrossReference);
                });
                tab.Group("index", "Index", "I", 72, g =>
                {
                    g.Medium("freew.index-mark", "Mark Entry", RibbonCommandIconKind.Index);
                    g.Medium("freew.index-insert", "Insert Index", RibbonCommandIconKind.Index);
                    g.Medium("freew.index-refresh", "Update Index", RibbonCommandIconKind.Refresh);
                });
                tab.Group("authorities", "Table of Authorities", "A", 66, g =>
                {
                    g.Medium("freew.mark-citation", "Mark Citation", RibbonCommandIconKind.Citation);
                    g.Medium("freew.table-of-authorities", "Insert Table of Authorities", RibbonCommandIconKind.Bibliography);
                    g.Medium("freew.table-of-authorities-refresh", "Update Table", RibbonCommandIconKind.Refresh);
                });
            })
            .Tab("layout", "Layout", "L", tab =>
            {
                tab.Group("page-setup", "Page Setup", "P", 100, g =>
                {
                    // Margins is the hero; the remaining page-setup dropdowns read as labelled Medium rows.
                    // Margins / Size carry a menu with the "Custom Margins…" / "More Paper Sizes…" launchers that
                    // open the unified Page Setup dialog (on the Margins / Paper tab).
                    g.Large("freew.margins", "Margins", RibbonCommandIconKind.Margins, "M", menu: m =>
                    {
                        m.Item("freew.margins", "Normal / Narrow (toggle)", "N");
                        m.Item("freew.custom-margins", "Custom Margins…", "A");
                    });
                    g.Medium("freew.orientation", "Orientation", RibbonCommandIconKind.Orientation, dropdown: true);
                    g.Medium("freew.size", "Size", RibbonCommandIconKind.OnePage, "Z", menu: m =>
                    {
                        m.Item("freew.size", "Letter / A4 (toggle)", "L");
                        m.Item("freew.more-paper-sizes", "More Paper Sizes…", "M");
                    });
                    g.Medium("freew.columns", "Columns", RibbonCommandIconKind.TextColumns, menu: m =>
                    {
                        m.Item("freew.columns-one", "One", "O");
                        m.Item("freew.columns-two", "Two", "T");
                        m.Item("freew.columns-three", "Three", "H");
                        m.Item("freew.columns-left", "Left", "L");
                        m.Item("freew.columns-right", "Right", "R");
                        m.Item("freew.columns-more", "More Columns...", "M");
                    });
                    g.Medium("freew.breaks", "Breaks", RibbonCommandIconKind.PageBreak, "B", menu: m =>
                    {
                        m.Item("freew.page-break", "Page Break", "P");
                        m.Item("freew.column-break", "Column Break", "C");
                        m.Separator();
                        m.Item("freew.section-break-next-page", "Next Page", "N");
                        m.Item("freew.section-break-continuous", "Continuous", "O");
                        m.Item("freew.section-break-even-page", "Even Page", "E");
                        m.Item("freew.section-break-odd-page", "Odd Page", "D");
                    });
                    g.RowBreak();
                    // Page Setup launcher: the unified Margins / Paper / Layout dialog (Word's group launcher).
                    g.Icon("freew.page-setup", "Page Setup", RibbonCommandIconKind.Margins, "G");
                    g.Icon("freew.line-numbers", "Line Numbers", RibbonCommandIconKind.Number, menu: m =>
                    {
                        m.Item("freew.line-numbers-none", "None", "N");
                        m.Item("freew.line-numbers-continuous", "Continuous", "C");
                        m.Item("freew.line-numbers-restart-page", "Restart Each Page", "P");
                        m.Item("freew.line-numbers-restart-section", "Restart Each Section", "S");
                        m.Item("freew.line-numbers-options", "Line Numbering Options...", "O");
                    });
                    // Hyphenation dropdown (Word's Layout > Page Setup > Hyphenation): None / Automatic /
                    // Manual, plus the Hyphenation Options… dialog. The mode items set the document flag; the
                    // options item opens the dialog (zone, consecutive-hyphen limit, hyphenate-caps).
                    g.Icon("freew.hyphenation", "Hyphenation", RibbonCommandIconKind.Hyphenation, "HY", menu: m =>
                    {
                        m.Item("freew.hyphenation-none", "None", "N");
                        m.Item("freew.hyphenation-auto", "Automatic", "A");
                        m.Item("freew.hyphenation-manual", "Manual", "M");
                        m.Item("freew.hyphenation-options", "Hyphenation Options…", "H");
                    });
                    g.Icon("freew.page-valign", "Vertical Align", RibbonCommandIconKind.AlignJustify);
                    g.Icon("freew.different-first-page", "Different First Page", RibbonCommandIconKind.CoverPage);
                });
                // Single-command group → Large.
                tab.Group("paragraph", paragraphGroup.Label, "A", 76, g =>
                {
                    // Indent-decrease/increase and line-spacing carry over from Home; the four numeric
                    // combos below are unique to the Layout tab and mirror Word's Layout > Paragraph group
                    // (Indent Left/Right in points; Spacing Before/After in points).
                    g.Icon("freew.indent-decrease", "Decrease Indent", RibbonCommandIconKind.IndentDecrease);
                    g.Icon("freew.indent-increase", "Increase Indent", RibbonCommandIconKind.IndentIncrease);
                    g.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", c => c with
                    {
                        Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.LineSpacing),
                        Width = 52
                    });
                    // Indent Left: exact left-indent value (points); 36 pt = 0.5 in, 72 pt = 1 in.
                    g.ComboBox("freew.indent-left", "Indent Left", c => c with
                    {
                        Items = new[] { "0", "18", "36", "54", "72" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentIncrease),
                        Width = 52
                    });
                    // Indent Right: exact right-indent value (points).
                    g.ComboBox("freew.indent-right", "Indent Right", c => c with
                    {
                        Items = new[] { "0", "18", "36", "54", "72" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentDecrease),
                        Width = 52
                    });
                    g.RowBreak();
                    g.Icon("freew.space-before-toggle", "Add Space Before Paragraph", RibbonCommandIconKind.SpaceBefore);
                    g.Icon("freew.space-after-toggle", "Add Space After Paragraph", RibbonCommandIconKind.SpaceAfter);
                    // Spacing Before / After: exact space-before and space-after values in points.
                    g.ComboBox("freew.space-before", "Spacing Before", c => c with
                    {
                        Items = new[] { "0", "6", "12", "18", "24" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceBefore),
                        Width = 52
                    });
                    g.ComboBox("freew.space-after", "Spacing After", c => c with
                    {
                        Items = new[] { "0", "6", "8", "12", "18", "24" },
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceAfter),
                        Width = 52
                    });
                    g.Icon("freew.paragraph-dialog", "Paragraph Settings", RibbonCommandIconKind.TextFunction);
                    g.Icon("freew.tabs-dialog", "Tabs", RibbonCommandIconKind.Ruler);
                });
                tab.Group("preview", "Preview", "V", 90, g =>
                {
                    g.Large("freew.print-preview", "Print Preview", RibbonCommandIconKind.Print);
                });
                tab.Group("data", "Data", "D", 88, g =>
                {
                    // Small group → labelled Medium buttons. (Sort lives in Home > Paragraph, matching Word.)
                    g.Medium("freew.text-to-table", "Text to Table", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-to-text", "Table to Text", RibbonCommandIconKind.TextFunction);
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
                    g.ComboBox("freew.style-set", "Style Sets", c => c with
                    {
                        Items = DocumentStyleSet.Catalog.Select(s => s.Name).ToArray(),
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font, RibbonCommandIconAccent.Theme),
                        Width = 140
                    });
                    g.Icon("freew.reset-style-set", "Reset to Default Style Set", RibbonCommandIconKind.Refresh);
                    g.Medium("freew.theme-colors", "Colors", RibbonCommandIconKind.Color, "C", menu: m => ThemeMenu("freew.theme-colors", m), accent: RibbonCommandIconAccent.Color);
                    g.Medium("freew.theme-fonts", "Fonts", RibbonCommandIconKind.Font, "F", menu: m => FontSetMenu("freew.theme-fonts", m), accent: RibbonCommandIconAccent.Theme);
                    g.Medium("freew.paragraph-spacing", "Paragraph Spacing", RibbonCommandIconKind.LineSpacing, "P", menu: m => ParagraphSpacingMenu("freew.paragraph-spacing", m), accent: RibbonCommandIconAccent.Theme);
                    g.Medium("freew.theme-effects", "Effects", RibbonCommandIconKind.Effects, "E", menu: m => EffectsMenu("freew.theme-effects", m), accent: RibbonCommandIconAccent.Theme);
                });
                // Design > Page Background: set the whole-page background colour (Word's Page Color). The
                // command opens a swatch palette (+ No Color + More Colors...) and writes the model's page
                // BackgroundColorHex, which already round-trips as w:background in docx.
                tab.Group("page-background", pageBackgroundGroup.Label, pageBackgroundGroup.KeyTip, 90, g =>
                {
                    g.Medium("freew.watermark", watermarkCommand.Label, RibbonCommandIconKind.Watermark);
                    g.Medium("freew.page-color", pageColorCommand.Label, RibbonCommandIconKind.Fill, accent: RibbonCommandIconAccent.Fill, dropdown: true);
                    // Word's Design > Page Background > Page Borders: opens the full Borders and Shading dialog.
                    g.Medium("freew.page-border", pageBordersCommand.Label, RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border);
                });
            })
            .Tab("view", "View", "W", tab =>
            {
                // Small toggle groups → labelled Medium toggles so Print Layout / Read Mode read clearly.
                tab.Group("views", "Views", "V", 100, g =>
                {
                    g.Medium("freew.read-mode", "Read Mode", RibbonCommandIconKind.ReadMode, menu: m =>
                    {
                        m.Item("freew.read-mode-column-narrow",  "Narrow Column Width",  "N");
                        m.Item("freew.read-mode-column-default", "Default Column Width",  "D");
                        m.Item("freew.read-mode-column-wide",    "Wide Column Width",     "W");
                        m.Separator();
                        m.Item("freew.read-mode-color-none",    FreeWRibbonText.PageColorNoColorOption, "O");
                        m.Item("freew.read-mode-color-sepia",   "Sepia",                 "S");
                        m.Item("freew.read-mode-color-inverse", "Inverse (Dark Mode)",   "I");
                    });
                    g.MediumToggle("freew.print-layout", "Print Layout", RibbonCommandIconKind.PrintLayout);
                    g.MediumToggle("freew.web-layout", "Web Layout", RibbonCommandIconKind.WebLayout);
                    g.MediumToggle("freew.outline-view", "Outline", RibbonCommandIconKind.MultilevelList);
                    g.MediumToggle("freew.draft-view", "Draft", RibbonCommandIconKind.Draft);
                    g.MediumToggle("freew.paged-edit-view", "Page Edit", RibbonCommandIconKind.PrintLayout);
                });
                tab.Group("show", "Show", "S", 90, g =>
                {
                    g.MediumToggle("freew.ruler", "Ruler", RibbonCommandIconKind.Ruler);
                    g.MediumToggle("freew.nav-pane", "Navigation Pane", RibbonCommandIconKind.NavigationPane);
                    g.MediumToggle("freew.gridlines", "Gridlines", RibbonCommandIconKind.Grid);
                });
                // Zoom group → Word's View > Zoom hero, opening the Zoom dialog (presets / page fits / custom %).
                // Multiple Pages and Side to Side are paginated read-only overlays reusing the print paginator.
                tab.Group("zoom", "Zoom", "Z", 80, g =>
                {
                    g.Large("freew.zoom-dialog", "Zoom", RibbonCommandIconKind.Zoom);
                    g.Medium("freew.zoom-100", "100%", RibbonCommandIconKind.Zoom);
                    g.Medium("freew.zoom-one-page", "One Page", RibbonCommandIconKind.OnePage);
                    g.Medium("freew.zoom-page-width", "Page Width", RibbonCommandIconKind.Scale);
                    g.MediumToggle("freew.zoom-multiple-pages", "Multiple Pages", RibbonCommandIconKind.PreviewResults);
                    g.MediumToggle("freew.zoom-side-to-side", "Side to Side", RibbonCommandIconKind.OnePage);
                });
                // Window group → Word's View > Window group. Split splits the workspace into a live top pane
                // (the editable surface) and a read-only snapshot bottom pane (built from the print paginator).
                // New Window opens a second MainWindow bound to the same document. Arrange All tiles all open
                // FreeW windows on screen.
                tab.Group("window", "Window", "N", 70, g =>
                {
                    g.MediumToggle("freew.split-window", "Split", RibbonCommandIconKind.Scale);
                    g.Medium("freew.new-window", "New Window", RibbonCommandIconKind.Page);
                    g.Medium("freew.arrange-all", "Arrange All", RibbonCommandIconKind.Grid);
                });
            })
            .Tab("help", "Help", "Y", tab =>
            {
                tab.Group("help", "Help", "H", 100, g =>
                {
                    g.Large("freew.help-online", "Help Online", RibbonCommandIconKind.Help, "H", accent: RibbonCommandIconAccent.Help);
                    g.Large("freew.feedback", "Feedback", RibbonCommandIconKind.Feedback, "F", accent: RibbonCommandIconAccent.Help);
                    g.Large("freew.copy-diagnostics", "Copy Diagnostics", RibbonCommandIconKind.Info, "D", accent: RibbonCommandIconAccent.Help);
                });
                tab.Group("product", "Product", "P", 90, g =>
                {
                    g.Large("freew.check-updates", "Check for Updates", RibbonCommandIconKind.Refresh, "U", accent: RibbonCommandIconAccent.Help);
                    g.Large("freew.about", "About FreeW", RibbonCommandIconKind.Info, "A", accent: RibbonCommandIconAccent.Help);
                    g.Large("freew.legal-notices", "Legal Notices", RibbonCommandIconKind.Book, "L", accent: RibbonCommandIconAccent.Help);
                });
            })
            .Tab("mailings", "Mailings", "M", tab =>
            {
                // Word's "Create" group (Envelopes, Labels) sits at the far left of the Mailings tab.
                tab.Group("create", "Create", "C", 130, g =>
                {
                    g.Medium("freew.merge-envelopes", "Envelopes", RibbonCommandIconKind.Envelope, "E");
                    g.Medium("freew.merge-labels", "Labels", RibbonCommandIconKind.MergeField, "L");
                });
                tab.Group("merge-data", "Start Mail Merge", "D", 155, g =>
                {
                    g.Medium("freew.start-mail-merge", "Start Mail Merge", RibbonCommandIconKind.Envelope, "S", menu: m =>
                    {
                        m.Item("freew.start-mail-merge-letters", "Letters", "L");
                        m.Item("freew.start-mail-merge-directory", "Directory", "D");
                        m.Separator();
                        m.Item("freew.start-mail-merge-normal", "Normal Word Document", "N");
                    });
                    g.Medium("freew.merge-data", "Select Recipients", RibbonCommandIconKind.Recipients);
                    g.Medium("freew.merge-edit-recipients", "Edit Recipient List", RibbonCommandIconKind.Recipients);
                    // Filter & Sort refines the active recipient list without touching the merge template.
                    g.Medium("freew.merge-filter-sort", "Filter & Sort Recipients", RibbonCommandIconKind.Recipients);
                });
                // Each Mailings group is a single labelled command so Word's command names stay readable.
                tab.Group("merge-write", "Write & Insert Fields", "W", 145, g =>
                {
                    g.Medium("freew.merge-address-block", "Address Block", RibbonCommandIconKind.Recipients, "A");
                    g.Medium("freew.merge-greeting-line", "Greeting Line", RibbonCommandIconKind.GreetingLine, "G");
                    g.Medium("freew.merge-field", "Insert Merge Field", RibbonCommandIconKind.MergeField, "F");
                    g.Medium("freew.merge-match-fields", "Match Fields", RibbonCommandIconKind.MergeField, "H");
                    // Rules: Word's "Rules" dropdown — conditional expressions and special fields that are
                    // evaluated per-record by MergeRuleEvaluator during Preview Results and Finish & Merge.
                    g.Medium("freew.merge-rules", "Rules", RibbonCommandIconKind.Field, "U", menu: m =>
                    {
                        m.Item("freew.merge-rule-if", "If…Then…Else", "I");
                        m.Separator();
                        m.Item("freew.merge-rule-skip-record-if", "Skip Record If", "K");
                        m.Item("freew.merge-rule-next-record-if", "Next Record If", "X");
                        m.Separator();
                        m.Item("freew.merge-next-record", "Next Record", "N");
                        m.Item("freew.merge-record-number", "Merge Record #", "R");
                        m.Item("freew.merge-sequence-number", "Merge Sequence #", "Q");
                        m.Separator();
                        m.Item("freew.merge-rule-fill-in", "Fill-in", "L");
                        m.Item("freew.merge-rule-ask", "Ask", "A");
                        m.Separator();
                        m.Item("freew.merge-rule-set", "Set Bookmark", "B");
                        m.Item("freew.merge-rule-ref", "Ref Bookmark", "E");
                    });
                });
                tab.Group("merge-preview", "Preview Results", "P", 120, g =>
                {
                    g.Medium("freew.merge-preview", "Preview Results", RibbonCommandIconKind.PreviewResults);
                    g.Icon("freew.merge-preview-first", "First Record", RibbonCommandIconKind.Previous);
                    g.Icon("freew.merge-preview-previous", "Previous Record", RibbonCommandIconKind.Previous);
                    g.Icon("freew.merge-preview-next", "Next Record", RibbonCommandIconKind.Next);
                    g.Icon("freew.merge-preview-last", "Last Record", RibbonCommandIconKind.Next);
                    g.Medium("freew.merge-find-recipient", "Find Recipient", RibbonCommandIconKind.Search);
                    g.Medium("freew.merge-check-errors", "Check for Errors", RibbonCommandIconKind.Warning,
                        accent: RibbonCommandIconAccent.Warning);
                });
                tab.Group("merge-finish", "Finish", "F", 110, g =>
                {
                    g.Medium("freew.merge-finish", "Finish & Merge", RibbonCommandIconKind.FinishMerge);
                    g.Medium("freew.merge-email", "Send E-mail Messages", RibbonCommandIconKind.Envelope, "E");
                });
            })
            .Tab("review", "Review", "R", tab =>
            {
                tab.Group("proofing", "Proofing", "P", 100, g =>
                {
                    // Word Count hero, then the two proofing toggles/commands as labelled Medium rows.
                    g.Large("freew.statistics", "Word Count", RibbonCommandIconKind.WordCount);
                    g.MediumToggle("freew.spellcheck-toggle", "Spelling & Grammar", RibbonCommandIconKind.Spelling);
                    g.Medium("freew.add-to-dictionary", "Add to Dictionary", RibbonCommandIconKind.Book);
                    // Thesaurus (Shift+F7): looks up synonyms for the selected/caret word in the bundled
                    // compact English synonym dictionary (Moby II derivative, ~3 000 headwords, public domain).
                    // Shows senses + synonyms in a docked pane with Insert (replace word) and Copy actions.
                    g.Medium("freew.thesaurus", "Thesaurus", RibbonCommandIconKind.Book, "T");
                    // Set Proofing Language lives in the Proofing group (matching Word's Review tab layout).
                    // It applies a BCP-47 language tag to the selected runs (rPr/w:lang) so the built-in
                    // spell checker uses the correct dictionary per run.
                    g.Medium("freew.set-proofing-language", "Set Proofing Language", RibbonCommandIconKind.Language);
                });
                // Single-command group → labelled Medium toggle (Word's Speech > Read Aloud). Reads the
                // document from the caret to the end using in-box text-to-speech; the toggle reflects
                // whether a read-through is currently active.
                tab.Group("speech", "Speech", "S", 97, g =>
                {
                    g.MediumToggle("freew.read-aloud", "Read Aloud", RibbonCommandIconKind.ReadAloud);
                });
                tab.Group("accessibility", "Accessibility", "A", 92, g =>
                {
                    g.Medium("freew.check-accessibility", "Check Accessibility", RibbonCommandIconKind.Accessibility);
                });
                tab.Group("comments", "Comments", "C", 95, g =>
                {
                    // Thread actions mirror Word's Review > Comments group and stay labelled at narrow widths.
                    g.Medium("freew.new-comment", "New Comment", RibbonCommandIconKind.Comment);
                    g.Medium("freew.delete-comment", "Delete", RibbonCommandIconKind.Delete);
                    g.Medium("freew.previous-comment", "Previous", RibbonCommandIconKind.Previous);
                    g.Medium("freew.next-comment", "Next", RibbonCommandIconKind.Next);
                    g.RowBreak();
                    g.Medium("freew.reply-comment", "Reply", RibbonCommandIconKind.Comment);
                    g.Medium("freew.resolve-comment", "Resolve", RibbonCommandIconKind.AcceptChange);
                    g.Medium("freew.show-comments", "Show Comments", RibbonCommandIconKind.Comment);
                });
                tab.Group("tracking", "Tracking", "G", 90, g =>
                {
                    // Track Changes is the big toggle; the Reviewing Pane toggle opens the dockable revisions
                    // list. Accept/Reject live in Changes, mirroring Word's group geography.
                    g.MediumToggle("freew.track-changes", "Track Changes", RibbonCommandIconKind.History);
                    g.MediumToggle("freew.reviewing-pane", "Reviewing Pane", RibbonCommandIconKind.History);
                    g.RowBreak();
                    // Display for Review: dropdown with All Markup (default), Simple Markup, No Markup,
                    // and Original — Word's order. Simple Markup shows the final form (No Markup inline
                    // path) plus a left-margin change bar beside each changed paragraph.
                    g.Medium("freew.display-for-review", "All Markup", RibbonCommandIconKind.History, "D", menu: m =>
                    {
                        m.Item("freew.display-for-review-all-markup", "All Markup", "A");
                        m.Item("freew.display-for-review-simple-markup", "Simple Markup", "S");
                        m.Item("freew.display-for-review-no-markup", "No Markup", "N");
                        m.Item("freew.display-for-review-original", "Original", "O");
                    });
                    // Show Markup: per-category visibility toggles. Balloons mode renders comments and
                    // tracked-change revisions as right-margin callouts with leader lines, instead of
                    // inline highlights. The BalloonOverlay adorner/panel hosts the balloon strip.
                    g.Medium("freew.show-markup", "Show Markup", RibbonCommandIconKind.History, "M", menu: m =>
                    {
                        m.Item("freew.show-markup-insertions-deletions", "Insertions and Deletions", "I");
                        m.Item("freew.show-markup-comments", "Comments", "C");
                        m.Item("freew.show-markup-formatting", "Formatting", "F");
                        m.Separator();
                        // Balloons: toggle right-margin balloon display mode for comments and revisions.
                        m.Item("freew.show-markup-balloons", "Show Revisions in Balloons", "B");
                    });
                });
                // Changes group: Accept/Reject expose the current-change action plus the all-changes
                // variants through Word-style dropdowns, followed by Previous/Next navigation.
                tab.Group("changes", "Changes", "H", 88, g =>
                {
                    g.Medium("freew.accept-this", "Accept", RibbonCommandIconKind.AcceptChange, "A", menu: m =>
                    {
                        m.Item("freew.accept-this", "Accept This Change", "A");
                        m.Item("freew.accept-all", "Accept All Changes", "L");
                    });
                    g.Medium("freew.reject-this", "Reject", RibbonCommandIconKind.RejectChange, "J", menu: m =>
                    {
                        m.Item("freew.reject-this", "Reject This Change", "R");
                        m.Item("freew.reject-all", "Reject All Changes", "L");
                    });
                    g.RowBreak();
                    g.Medium("freew.previous-change", "Previous", RibbonCommandIconKind.History);
                    g.Medium("freew.next-change", "Next", RibbonCommandIconKind.History);
                });
                // Protect group: Word's Mark as Final (advisory read-only toggle) + Restrict Editing
                // (opens the restrict-editing pane; the toggle reflects whether protection is enforced).
                tab.Group("protect", "Protect", "T", 85, g =>
                {
                    g.MediumToggle("freew.mark-as-final", "Mark as Final", RibbonCommandIconKind.Protect);
                    g.MediumToggle("freew.restrict-editing", "Restrict Editing", RibbonCommandIconKind.Protect);
                });
                // Compare (legal blackline) + Combine (merge two reviewers' revisions).
                tab.Group("compare", "Compare", "M", 80, g =>
                {
                    g.Medium("freew.compare", "Compare", RibbonCommandIconKind.Compare);
                    g.Medium("freew.combine", "Combine", RibbonCommandIconKind.Compare);
                });
                tab.Group("inspect", "Inspect", "I", 75, g =>
                {
                    g.Medium("freew.inspect-document", "Inspect Document", RibbonCommandIconKind.Search);
                });
            })
            .Tab("developer", "Developer", "D", tab =>
            {
                tab.Group("controls", "Controls", "O", 100, g =>
                {
                    g.Medium("freew.cc-text", "Text Control", RibbonCommandIconKind.TextBox);
                    g.Medium("freew.cc-richtext", "Rich Text", RibbonCommandIconKind.QuickParts);
                    g.Medium("freew.cc-checkbox", "Check Box", RibbonCommandIconKind.CheckBox);
                    g.Medium("freew.cc-date", "Date Picker", RibbonCommandIconKind.Date);
                    g.Medium("freew.cc-dropdown", "Drop-Down List", RibbonCommandIconKind.List);
                    g.Medium("freew.cc-combo", "Combo Box", RibbonCommandIconKind.ChevronDown);
                });
            })
            // ── Contextual tabs (Word "Tools" tabs) ───────────────────────────────────────────────────
            // Declared individually here, but shown/hidden by the shared RibbonContextualTabController only
            // while their selection context is active: "picture" when an image is selected, "table" when the
            // caret is in a table. Contextual tabs reuse the same command ids but group them by active selection,
            // exactly like Word's Picture Format / Table Design tabs.
            // ── Drawing Format contextual tab — Shape Tools (shown when a shape/text-box/WordArt is selected) ──
            .ContextualTab("drawing-format", "Drawing Format",
                new RibbonTabContext("drawing", "Drawing Tools", RibbonContextColor.Purple), tab =>
            {
                tab.Group("drawing-insert", "Insert Shapes", "I", 110, g =>
                {
                    g.Medium("freew.shape-change", "Change Shape", RibbonCommandIconKind.Shapes, menu: m =>
                    {
                        m.Item("freew.shape-change-rectangle", "Rectangle", "R");
                        m.Item("freew.shape-change-rounded", "Rounded Rectangle", "U");
                        m.Item("freew.shape-change-ellipse", "Oval", "O");
                    });
                    // Edit Shape > Edit Points (W25): converts preset to freeform custom geometry.
                    g.Medium("freew.shape-edit-shape", "Edit Shape", RibbonCommandIconKind.Generic, menu: m =>
                    {
                        m.Item("freew.shape-convert-freeform", "Convert to Freeform", "F");
                        m.Item("freew.shape-edit-points",      "Edit Points",          "E");
                    });
                });
                tab.Group("drawing-styles", "Shape Styles", "H", 100, g =>
                {
                    // Shape Styles gallery — 40 theme-coloured presets (injected as live gallery at runtime)
                    g.Medium("freew.shape-styles-gallery", "Shape Styles", RibbonCommandIconKind.Styles);

                    g.Medium("freew.shape-fill", "Shape Fill", RibbonCommandIconKind.Fill, accent: RibbonCommandIconAccent.Fill, menu: m =>
                    {
                        m.Item("freew.shape-fill-no-fill", "No Fill", "N");
                        m.Separator();
                        m.Item("freew.shape-fill-gradient-blue", "Gradient Blue", "G");
                        m.Item("freew.shape-fill-gradient-orange", "Gradient Orange", "O");
                        m.Item("freew.shape-fill-pattern-diag", "Pattern: Diagonal Cross", "D");
                    });
                    g.Medium("freew.shape-outline", "Shape Outline", RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border, menu: m =>
                    {
                        m.Item("freew.shape-outline-no-outline", "No Outline", "N");
                        m.Item("freew.shape-outline-solid", "Solid", "S");
                        m.Item("freew.shape-outline-dash", "Dash", "D");
                        m.Item("freew.shape-outline-dot", "Dot", "O");
                    });
                    // Shape Effects submenu (W24)
                    g.Medium("freew.shape-effects", "Shape Effects", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.shape-effects-none", "No Effects", "N");
                        m.Separator();
                        m.Item("freew.shape-effect-shadow", "Shadow", "S");
                        m.Item("freew.shape-effect-glow", "Glow", "G");
                        m.Item("freew.shape-effect-soft-edge", "Soft Edges", "E");
                        m.Item("freew.shape-effect-reflection", "Reflection", "R");
                        m.Item("freew.shape-effect-bevel", "Bevel", "B");
                    });
                });
                tab.Group("drawing-text", "Text", "X", 90, g =>
                {
                    g.Medium("freew.shape-text-direction", "Text Direction", RibbonCommandIconKind.TextBox, menu: m =>
                    {
                        m.Item("freew.shape-text-horizontal", "Horizontal", "H");
                        m.Item("freew.shape-text-rotate90", "Rotate 90°", "R");
                        m.Item("freew.shape-text-rotate270", "Rotate 270°", "T");
                    });
                });
                tab.Group("drawing-wordart", "WordArt Styles", "W", 85, g =>
                {
                    g.Medium("freew.wordart-style", "WordArt Style", RibbonCommandIconKind.WordArt, menu: m =>
                    {
                        // Original four
                        m.Item("freew.wordart-style-fill-blue", "Fill: Blue", "B");
                        m.Item("freew.wordart-style-gradient", "Gradient Fill", "G");
                        m.Item("freew.wordart-style-outline", "Outline", "O");
                        m.Item("freew.wordart-style-shadow", "Shadow", "S");
                        m.Separator();
                        // Extended eleven (W24)
                        m.Item("freew.wordart-style-fill-gold", "Fill: Gold", "D");
                        m.Item("freew.wordart-style-fill-white", "Fill: White", "W");
                        m.Item("freew.wordart-style-grad-multi", "Gradient: Multicolour", "M");
                        m.Item("freew.wordart-style-chrome-one", "Outline Only", "L");
                        m.Item("freew.wordart-style-chrome-two", "White + Outline", "H");
                        m.Item("freew.wordart-style-shadow-orange", "Shadow: Orange", "A");
                        m.Item("freew.wordart-style-glow-blue", "Glow: Blue", "U");
                        m.Item("freew.wordart-style-glow-gold", "Glow: Gold", "I");
                        m.Item("freew.wordart-style-reflection", "Reflection", "F");
                        m.Item("freew.wordart-style-bevel", "Bevel", "V");
                        m.Item("freew.wordart-style-pattern", "Pattern Fill", "P");
                    });
                    // Text Effects > Transform (W24 — warp presets)
                    g.Medium("freew.wordart-transform", "Text Effects: Transform", RibbonCommandIconKind.WordArt, menu: m =>
                    {
                        m.Item("freew.wordart-warp-none", "No Transform", "N");
                        m.Separator();
                        m.Item("freew.wordart-warp-arch-up", "Arch Up", "A");
                        m.Item("freew.wordart-warp-arch-down", "Arch Down", "D");
                        m.Item("freew.wordart-warp-circle", "Circle", "C");
                        m.Item("freew.wordart-warp-wave1", "Wave 1", "W");
                        m.Item("freew.wordart-warp-wave2", "Wave 2", "V");
                        m.Item("freew.wordart-warp-inflate", "Inflate", "I");
                        m.Item("freew.wordart-warp-deflate", "Deflate", "E");
                        m.Item("freew.wordart-warp-chevron-up", "Chevron Up", "U");
                        m.Item("freew.wordart-warp-chevron-down", "Chevron Down", "H");
                        m.Item("freew.wordart-warp-fade-right", "Fade Right", "F");
                        m.Item("freew.wordart-warp-fade-left", "Fade Left", "L");
                        m.Item("freew.wordart-warp-slant-up", "Slant Up", "S");
                        m.Item("freew.wordart-warp-slant-down", "Slant Down", "T");
                    });
                });
                tab.Group("drawing-arrange", "Arrange", "A", 80, g =>
                {
                    g.Medium("freew.shape-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: m =>
                    {
                        m.Item("freew.shape-wrap-inline",     "In Line with Text", "I");
                        m.Item("freew.shape-wrap-square",     "Square",            "S");
                        m.Item("freew.shape-wrap-tight",      "Tight",             "T");
                        m.Item("freew.shape-wrap-top-bottom", "Top and Bottom",    "B");
                        m.Item("freew.shape-wrap-behind",     "Behind Text",       "H");
                        m.Item("freew.shape-wrap-front",      "In Front of Text",  "F");
                    });
                    g.Medium("freew.shape-position", "Position", RibbonCommandIconKind.Margins);
                    g.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.shape-rotate-right90",   "Rotate Right 90°",  "R");
                        m.Item("freew.shape-rotate-left90",    "Rotate Left 90°",   "L");
                        m.Item("freew.shape-flip-vertical",    "Flip Vertical",     "V");
                        m.Item("freew.shape-flip-horizontal",  "Flip Horizontal",   "H");
                    });
                    g.Medium("freew.shape-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.shape-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.shape-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    g.Medium("freew.shape-align-to-page", "Align to Page", RibbonCommandIconKind.Margins);
                    g.Medium("freew.shape-align-to-margin", "Align to Margin", RibbonCommandIconKind.Margins);
                    g.Medium("freew.shape-distribute-h", "Distribute Horizontally", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.shape-distribute-v", "Distribute Vertically", RibbonCommandIconKind.AlignCenter);
                    // Z-order commands for floating shapes (reuse same command ids as image z-order).
                    g.Medium("freew.image-bring-to-front",  "Bring to Front",  RibbonCommandIconKind.BringToFront);
                    g.Medium("freew.image-send-to-back",    "Send to Back",    RibbonCommandIconKind.SendToBack);
                    g.Medium("freew.image-bring-forward",   "Bring Forward",   RibbonCommandIconKind.BringForward);
                    g.Medium("freew.image-send-backward",   "Send Backward",   RibbonCommandIconKind.SendBackward);
                    // Group / Ungroup (Phase 4).
                    g.Medium("freew.object-group", "Group", RibbonCommandIconKind.Generic);
                    g.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Generic);
                });
                tab.Group("drawing-size", "Size", "S", 70, g =>
                {
                    g.Medium("freew.shape-size", "Size", RibbonCommandIconKind.Size);
                    g.Medium("freew.shape-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                });
            })
            .ContextualTab("picture-format", "Picture Format",
                new RibbonTabContext("picture", "Picture Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("picture-arrange", "Arrange", "A", 100, g =>
                {
                    g.Medium("freew.image-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: m =>
                    {
                        m.Item("freew.image-wrap-inline", "In Line with Text", "I");
                        m.Item("freew.image-wrap-square", "Square", "S");
                        m.Item("freew.image-wrap-tight", "Tight", "T");
                        m.Item("freew.image-wrap-top-bottom", "Top and Bottom", "B");
                        m.Item("freew.image-wrap-behind", "Behind Text", "H");
                        m.Item("freew.image-wrap-front", "In Front of Text", "F");
                    });
                    g.Medium("freew.image-position", "Position", RibbonCommandIconKind.Margins);
                    g.Medium("freew.image-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.image-rotate-right90", "Rotate Right 90°", "R");
                        m.Item("freew.image-rotate-left90",  "Rotate Left 90°",  "L");
                        m.Item("freew.image-flip-vertical",  "Flip Vertical",    "V");
                        m.Item("freew.image-flip-horizontal","Flip Horizontal",  "H");
                    });
                    g.Medium("freew.image-align-left", "Align Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.image-align-center", "Align Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.image-align-right", "Align Right", RibbonCommandIconKind.AlignRight);
                    g.Medium("freew.image-align-to-page", "Align to Page", RibbonCommandIconKind.Margins);
                    g.Medium("freew.image-align-to-margin", "Align to Margin", RibbonCommandIconKind.Margins);
                    g.Medium("freew.image-distribute-h", "Distribute Horizontally", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.image-distribute-v", "Distribute Vertically", RibbonCommandIconKind.AlignCenter);
                    // Z-order commands for floating images (Phase 2).
                    g.Medium("freew.image-bring-to-front",  "Bring to Front",  RibbonCommandIconKind.BringToFront);
                    g.Medium("freew.image-send-to-back",    "Send to Back",    RibbonCommandIconKind.SendToBack);
                    g.Medium("freew.image-bring-forward",   "Bring Forward",   RibbonCommandIconKind.BringForward);
                    g.Medium("freew.image-send-backward",   "Send Backward",   RibbonCommandIconKind.SendBackward);
                    // Group / Ungroup for floating images (Phase 4).
                    g.Medium("freew.object-group",   "Group",   RibbonCommandIconKind.Generic);
                    g.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Generic);
                });
                // ── Picture Styles gallery group ──────────────────────────────────────────────────────────
                // Gallery injection (MainWindow.InjectGallery) keys on group id "picture-styles".
                // Each style preset command sets bundled border+effect fields.
                tab.Group("picture-styles", "Picture Styles", "Y", 98, g =>
                {
                    foreach (var preset in PictureStyleCatalog.Catalog)
                        g.Medium($"freew.image-style-{preset.Id}", preset.Name, RibbonCommandIconKind.Border);
                });
                tab.Group("picture-adjust", "Adjust", "J", 95, g =>
                {
                    g.Medium("freew.image-corrections", "Corrections", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.image-brightness-plus20",   "Brightness: +20%",   "1");
                        m.Item("freew.image-brightness-plus40",   "Brightness: +40%",   "2");
                        m.Item("freew.image-brightness-minus20",  "Brightness: -20%",   "3");
                        m.Item("freew.image-brightness-minus40",  "Brightness: -40%",   "4");
                        m.Item("freew.image-contrast-plus20",     "Contrast: +20%",     "5");
                        m.Item("freew.image-contrast-minus20",    "Contrast: -20%",     "6");
                        m.Item("freew.image-adjust-dialog",       "Picture Corrections…","D");
                    });
                    g.Medium("freew.image-color", "Color", RibbonCommandIconKind.Color, menu: m =>
                    {
                        m.Item("freew.image-saturation-0",        "Saturation: 0% (Greyscale)",   "G");
                        m.Item("freew.image-saturation-50",       "Saturation: 50%",              "H");
                        m.Item("freew.image-saturation-200",      "Saturation: 200%",             "J");
                        m.Item("freew.image-color-dialog",        "Color…",                       "C");
                        m.Separator();
                        m.Item("freew.image-recolor-grayscale",   "Recolor: Grayscale",           "1");
                        m.Item("freew.image-recolor-sepia",       "Recolor: Sepia",               "2");
                        m.Item("freew.image-recolor-washout",     "Recolor: Washout",             "3");
                        m.Item("freew.image-recolor-blackwhite",  "Recolor: Black and White",     "4");
                        m.Item("freew.image-recolor-none",        "Recolor: No Recolor",          "N");
                        m.Separator();
                        m.Item("freew.image-colortemp-warm",      "Color Tone: Warm (3000K)",     "W");
                        m.Item("freew.image-colortemp-cool",      "Color Tone: Cool (8000K)",     "L");
                        m.Item("freew.image-colortemp-neutral",   "Color Tone: Neutral",          "T");
                    });
                    g.Medium("freew.image-transparency", "Transparency", RibbonCommandIconKind.View, menu: m =>
                    {
                        m.Item("freew.image-transparency-25",     "Transparency: 25%",    "A");
                        m.Item("freew.image-transparency-50",     "Transparency: 50%",    "B");
                        m.Item("freew.image-transparency-75",     "Transparency: 75%",    "C");
                        m.Item("freew.image-transparency-dialog", "Transparency…",        "D");
                    });
                    // Picture Effects sub-menus: Shadow, Reflection, Glow, Soft Edges, Bevel.
                    g.Medium("freew.image-effects", "Picture Effects", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.image-shadow-none",    "Shadow: No Shadow",              "N");
                        m.Item("freew.image-shadow-1",       "Shadow: Offset Diagonal",        "1");
                        m.Item("freew.image-shadow-2",       "Shadow: Offset Diagonal Medium", "2");
                        m.Item("freew.image-shadow-3",       "Shadow: Perspective",            "3");
                        m.Item("freew.image-shadow-4",       "Shadow: Offset Bottom",          "4");
                        m.Item("freew.image-shadow-5",       "Shadow: Large",                  "5");
                        m.Separator();
                        m.Item("freew.image-reflection-none","Reflection: No Reflection",      "R");
                        m.Item("freew.image-reflection-1",   "Reflection: Tight, Touching",    "A");
                        m.Item("freew.image-reflection-2",   "Reflection: Tight, 4pt",         "B");
                        m.Item("freew.image-reflection-3",   "Reflection: Tight, 8pt",         "C");
                        m.Item("freew.image-reflection-4",   "Reflection: Half, Touching",     "D");
                        m.Item("freew.image-reflection-5",   "Reflection: Half, 4pt",          "E");
                        m.Separator();
                        m.Item("freew.image-glow-none",      "Glow: No Glow",                  "G");
                        m.Item("freew.image-glow-5",         "Glow: 5 pt",                     "H");
                        m.Item("freew.image-glow-8",         "Glow: 8 pt",                     "I");
                        m.Item("freew.image-glow-11",        "Glow: 11 pt",                    "J");
                        m.Item("freew.image-glow-18",        "Glow: 18 pt",                    "K");
                        m.Separator();
                        m.Item("freew.image-softedge-none",  "Soft Edges: None",               "S");
                        m.Item("freew.image-softedge-1",     "Soft Edges: 1 pt",               "T");
                        m.Item("freew.image-softedge-2pt5",  "Soft Edges: 2.5 pt",             "U");
                        m.Item("freew.image-softedge-5",     "Soft Edges: 5 pt",               "V");
                        m.Item("freew.image-softedge-10",    "Soft Edges: 10 pt",              "X");
                        m.Separator();
                        m.Item("freew.image-bevel-none",     "Bevel: No Bevel",                "O");
                        m.Item("freew.image-bevel-1",        "Bevel: Circle",                  "P");
                        m.Item("freew.image-bevel-2",        "Bevel: Relaxed Inset",           "Q");
                        m.Item("freew.image-bevel-3",        "Bevel: Cross",                   "F");
                        m.Item("freew.image-bevel-4",        "Bevel: Cool Slant",              "M");
                    });
                    // Artistic Effects gallery (W25): named menu items, one per ImageArtisticEffect value.
                    g.Medium("freew.image-artistic", "Artistic Effects", RibbonCommandIconKind.Effects, menu: m =>
                    {
                        m.Item("freew.image-artistic-none",          "No Artistic Effect",   "N");
                        m.Item("freew.image-artistic-blur",          "Blur",                 "B");
                        m.Item("freew.image-artistic-glow-diffused", "Glow Diffused",        "G");
                        m.Item("freew.image-artistic-glow-edges",    "Glow Edges",           "E");
                        m.Item("freew.image-artistic-pencil-gray",   "Pencil Grayscale",     "A");
                        m.Item("freew.image-artistic-pencil-sketch", "Pencil Sketch",        "K");
                        m.Item("freew.image-artistic-line-drawing",  "Line Drawing",         "L");
                        m.Item("freew.image-artistic-paintbrush",    "Paint Brush",          "P");
                        m.Item("freew.image-artistic-paint-strokes", "Paint Strokes",        "T");
                        m.Item("freew.image-artistic-photocopy",     "Photocopy",            "H");
                        m.Item("freew.image-artistic-posterize",     "Posterize",            "O");
                        m.Item("freew.image-artistic-pastels",       "Pastels",              "S");
                        m.Item("freew.image-artistic-watercolor",    "Watercolor Sponge",    "W");
                        m.Item("freew.image-artistic-film-grain",    "Film Grain",           "F");
                        m.Item("freew.image-artistic-mosaic",        "Mosaic Bubbles",       "M");
                    });
                    g.Medium("freew.image-crop", "Crop", RibbonCommandIconKind.Scale);
                    g.Medium("freew.image-reset", "Reset Picture", RibbonCommandIconKind.Refresh);
                    g.Medium("freew.image-border", "Picture Border", RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border);
                });
                tab.Group("picture-size", "Size", "S", 90, g =>
                {
                    g.Medium("freew.image-size", "Size", RibbonCommandIconKind.Size);
                    g.Medium("freew.image-alt-text", "Alt Text", RibbonCommandIconKind.Info);
                });
            })
            // ── Chart contextual tabs — Chart Tools (shown when a chart is selected) ──────────────
            .ContextualTab("chart-design", "Chart Design",
                new RibbonTabContext("chart", "Chart Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("chart-type", "Type", "T", 100, g =>
                    g.Medium("freew.chart-type-column", "Column", RibbonCommandIconKind.ChartColumn, menu: m =>
                    {
                        m.Item("freew.chart-type-column", "Column", "C");
                        m.Item("freew.chart-type-bar", "Bar", "B");
                        m.Item("freew.chart-type-line", "Line", "L");
                        m.Item("freew.chart-type-pie", "Pie", "P");
                        m.Item("freew.chart-type-scatter", "Scatter", "X");
                        m.Item("freew.chart-type-area", "Area", "A");
                        m.Item("freew.chart-type-doughnut", "Doughnut", "D");
                    }));
                tab.Group("chart-data", "Data", "D", 90, g =>
                    g.Medium("freew.chart-edit-data", "Edit Data", RibbonCommandIconKind.Table));
                // ── Gallery groups — replaced by ChartDesignGallery live-preview controls at render time ──
                // The gallery injection (MainWindow.InjectGallery) keys on the group id: "chart-quick-layout",
                // "chart-style", "chart-colors". The placeholder Medium buttons below let the ribbon model and
                // command bus wire up backed commands; the MainWindow swaps them for gallery swatches.
                tab.Group("chart-quick-layout", "Quick Layout", "L", 85, g =>
                {
                    foreach (var layout in ChartQuickLayout.Catalog)
                        g.Medium($"freew.chart-quick-layout-{layout.Id}", layout.Name, RibbonCommandIconKind.Grid);
                });
                tab.Group("chart-style", "Chart Styles", "S", 80, g =>
                {
                    foreach (var style in ChartStyle.Catalog)
                        g.Medium($"freew.chart-style-{style.Id}", style.Name, RibbonCommandIconKind.ChartColumn);
                });
                tab.Group("chart-colors", "Change Colors", "C", 75, g =>
                {
                    foreach (var scheme in ChartColorScheme.Catalog)
                        g.Medium($"freew.chart-color-{scheme.Id}", scheme.Name, RibbonCommandIconKind.Fill);
                });
                tab.Group("chart-elements", "Chart Layouts", "E", 70, g =>
                {
                    g.Medium("freew.chart-title", "Chart Title", RibbonCommandIconKind.Header);
                    g.Medium("freew.chart-axis-titles", "Axis Titles", RibbonCommandIconKind.Ruler);
                    g.Medium("freew.chart-toggle-legend", "Legend", RibbonCommandIconKind.List);
                });
            })
            .ContextualTab("chart-format", "Chart Format",
                new RibbonTabContext("chart", "Chart Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("chart-arrange", "Arrange", "A", 100, g =>
                {
                    g.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.shape-rotate-right90", "Rotate Right 90°", "R");
                        m.Item("freew.shape-rotate-left90",  "Rotate Left 90°",  "L");
                        m.Item("freew.shape-flip-vertical",  "Flip Vertical",    "V");
                        m.Item("freew.shape-flip-horizontal", "Flip Horizontal",  "H");
                    });
                });
                tab.Group("chart-size", "Size", "S", 90, g =>
                {
                    g.Medium("freew.chart-size", "Size", RibbonCommandIconKind.Size);
                    g.Medium("freew.chart-size-dialog", "More Size Options...", RibbonCommandIconKind.Size);
                });
            })
            // ── SmartArt contextual tab — SmartArt Tools (shown when a SmartArt is selected) ─────
            .ContextualTab("smartart-design", "SmartArt Design",
                new RibbonTabContext("smartart", "SmartArt Tools", RibbonContextColor.Orange), tab =>
            {
                tab.Group("smartart-create-graphic", "Create Graphic", "G", 100, g =>
                {
                    g.Medium("freew.smartart-add-shape", "Add Shape", RibbonCommandIconKind.Insert);
                    g.Medium("freew.smartart-remove-shape", "Remove Shape", RibbonCommandIconKind.Delete);
                    g.RowBreak();
                    g.Medium("freew.smartart-promote", "Promote", RibbonCommandIconKind.IndentDecrease);
                    g.Medium("freew.smartart-demote", "Demote", RibbonCommandIconKind.IndentIncrease);
                    g.RowBreak();
                    g.Medium("freew.smartart-move-up", "Move Up", RibbonCommandIconKind.ArrowUp);
                    g.Medium("freew.smartart-move-down", "Move Down", RibbonCommandIconKind.ArrowDown);
                });
                tab.Group("smartart-edit", "Edit", "E", 90, g =>
                    g.Medium("freew.smartart-edit-text", "Edit Text", RibbonCommandIconKind.TextFunction));
                // Galleries: placeholder commands — galleries are injected by MainWindow via InjectGallery.
                tab.Group("smartart-layouts", "Layouts", "L", 80, g =>
                    g.Medium("freew.smartart-change-layout", "Change Layout", RibbonCommandIconKind.SmartArt));
                tab.Group("smartart-colors", "SmartArt Styles", "C", 70, g =>
                {
                    g.Medium("freew.smartart-change-colors", "Change Colors", RibbonCommandIconKind.Fill);
                    g.Medium("freew.smartart-change-style", "Styles", RibbonCommandIconKind.Font);
                });
                tab.Group("smartart-arrange", "Arrange", "A", 60, g =>
                {
                    g.Medium("freew.shape-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: m =>
                    {
                        m.Item("freew.shape-rotate-right90", "Rotate Right 90°", "R");
                        m.Item("freew.shape-rotate-left90",  "Rotate Left 90°",  "L");
                        m.Item("freew.shape-flip-vertical",  "Flip Vertical",    "V");
                        m.Item("freew.shape-flip-horizontal", "Flip Horizontal",  "H");
                    });
                });
            })
            .ContextualTab("table-design", "Table Design",
                new RibbonTabContext("table", "Table Tools", RibbonContextColor.Teal), tab =>
            {
                tab.Group("table-style-options", "Table Style Options", "O", 100, g =>
                {
                    g.Medium("freew.table-header-row", "Header Row", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-last-row", "Last Row", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-first-column", "First Column", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-last-column", "Last Column", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-banded-rows", "Banded Rows", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-banded-cols", "Banded Columns", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                });
                tab.Group("table-style", "Table Style", "Y", 80, g =>
                {
                    g.Medium("freew.cell-shading", "Shading", RibbonCommandIconKind.Fill, accent: RibbonCommandIconAccent.Fill);
                    g.Medium("freew.cell-borders", "Borders", RibbonCommandIconKind.Grid);
                });
                tab.Group("draw-borders", "Draw Borders", "D", 60, g =>
                {
                    g.Medium("freew.draw-table", "Draw Table", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Border);
                    g.Medium("freew.eraser", "Eraser", RibbonCommandIconKind.Clear);
                });
            })
            .ContextualTab("table-layout", "Table Layout",
                new RibbonTabContext("table", "Table Tools", RibbonContextColor.Teal), tab =>
            {
                tab.Group("table-table", "Table", "T", 70, g =>
                {
                    g.Medium("freew.table-select-table", "Select Table", RibbonCommandIconKind.Table);
                    g.Medium("freew.table-select-row", "Select Row", RibbonCommandIconKind.Table);
                    g.RowBreak();
                    g.Medium("freew.table-select-col", "Select Column", RibbonCommandIconKind.Table);
                    g.Medium("freew.table-select-cell", "Select Cell", RibbonCommandIconKind.Table);
                    g.RowBreak();
                    g.Medium("freew.table-view-gridlines", "View Gridlines", RibbonCommandIconKind.Grid);
                    g.Medium("freew.table-properties", "Properties", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                });
                tab.Group("table-rows-cols", "Rows & Columns", "R", 120, g =>
                {
                    g.Medium("freew.table-insert-above", "Insert Above", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-insert-row", "Insert Below", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-insert-col-left", "Insert Left", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-insert-col", "Insert Right", RibbonCommandIconKind.Insert, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.table-delete-row", "Delete Rows", RibbonCommandIconKind.Delete);
                    g.Medium("freew.table-delete-col", "Delete Columns", RibbonCommandIconKind.Delete);
                    g.RowBreak();
                    g.Medium("freew.table-delete", "Delete Table", RibbonCommandIconKind.Delete);
                });
                tab.Group("table-merge", "Merge", "M", 90, g =>
                {
                    g.Medium("freew.merge-cells", "Merge Cells", RibbonCommandIconKind.Merge);
                    g.Medium("freew.split-cell", "Split Cell", RibbonCommandIconKind.Grid);
                    g.RowBreak();
                    g.Medium("freew.split-table", "Split Table", RibbonCommandIconKind.Grid);
                });
                tab.Group("table-cell-size", "Cell Size", "Z", 100, g =>
                {
                    g.Medium("freew.table-row-height", "Row Height", RibbonCommandIconKind.Size);
                    g.Medium("freew.table-col-width", "Column Width", RibbonCommandIconKind.Size);
                    g.RowBreak();
                    g.Medium("freew.table-distribute-rows", "Distribute Rows", RibbonCommandIconKind.Grid);
                    g.Medium("freew.table-distribute-cols", "Distribute Columns", RibbonCommandIconKind.Grid);
                    g.RowBreak();
                    g.Medium("freew.table-autofit-contents", "AutoFit Contents", RibbonCommandIconKind.Scale);
                    g.Medium("freew.table-autofit-window", "AutoFit Window", RibbonCommandIconKind.Scale);
                    g.Medium("freew.table-autofit-fixed", "Fixed Column Width", RibbonCommandIconKind.Size);
                });
                tab.Group("table-alignment", "Alignment", "A", 110, g =>
                {
                    g.Medium("freew.cell-align-top-left", "Top Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-align-top-center", "Top Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.cell-align-top-right", "Top Right", RibbonCommandIconKind.AlignRight);
                    g.RowBreak();
                    g.Medium("freew.cell-align-middle-left", "Middle Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-align-middle-center", "Middle Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.cell-align-middle-right", "Middle Right", RibbonCommandIconKind.AlignRight);
                    g.RowBreak();
                    g.Medium("freew.cell-align-bottom-left", "Bottom Left", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-align-bottom-center", "Bottom Center", RibbonCommandIconKind.AlignCenter);
                    g.Medium("freew.cell-align-bottom-right", "Bottom Right", RibbonCommandIconKind.AlignRight);
                    g.RowBreak();
                    g.Medium("freew.table-cell-margins", "Cell Margins", RibbonCommandIconKind.Margins);
                    g.RowBreak();
                    g.Medium("freew.cell-text-direction-horizontal", "Horizontal", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-text-direction-rotate90", "Rotate Text Up", RibbonCommandIconKind.AlignLeft);
                    g.Medium("freew.cell-text-direction-rotate270", "Rotate Text Down", RibbonCommandIconKind.AlignLeft);
                });
                tab.Group("table-data", "Data", "D", 70, g =>
                {
                    g.Medium("freew.table-repeat-header", "Repeat Header Row", RibbonCommandIconKind.Table, accent: RibbonCommandIconAccent.Green);
                    g.Medium("freew.table-formula", "Formula", RibbonCommandIconKind.Sum, accent: RibbonCommandIconAccent.Green);
                    g.RowBreak();
                    g.Medium("freew.sort", "Sort", RibbonCommandIconKind.Sort);
                    g.Medium("freew.table-to-text", "Convert to Text", RibbonCommandIconKind.TextFunction);
                });
            })
            // ── Header & Footer Design contextual tab — Header & Footer Tools ────────────────────────
            // Activation model: dialog approach (not an in-document edit region). FreeW's FlowDocument
            // is a single continuous stream; there is no WYSIWYG header region. Every command writes
            // directly into FinalSectionHeadersFooters / PageSettings via ApplyPageSettings and
            // round-trips through DocxWriter. The contextual key "header-footer" can be activated from
            // Insert > Header / Footer commands via the ribbon controller.
            .ContextualTab("header-footer-design", "Design",
                new RibbonTabContext("header-footer", "Header & Footer Tools", RibbonContextColor.Purple), tab =>
            {
                // Header & Footer group — edit the six per-slot content areas.
                tab.Group("hf-header-footer", "Header & Footer", "H", 120, g =>
                {
                    g.Medium("freew.hf-edit-header",       "Edit Header",       RibbonCommandIconKind.Header, menu: m =>
                    {
                        m.Item("freew.hf-edit-header",       "Default Header",     "H");
                        m.Item("freew.hf-edit-first-header", "First-Page Header",  "F");
                        m.Item("freew.hf-edit-even-header",  "Even-Page Header",   "E");
                    });
                    g.Medium("freew.hf-edit-footer",       "Edit Footer",       RibbonCommandIconKind.Footer, menu: m =>
                    {
                        m.Item("freew.hf-edit-footer",       "Default Footer",     "O");
                        m.Item("freew.hf-edit-first-footer", "First-Page Footer",  "I");
                        m.Item("freew.hf-edit-even-footer",  "Even-Page Footer",   "V");
                    });
                });
                // Insert group — add page number, date/time, or document-info field into the default header.
                tab.Group("hf-insert", "Insert", "I", 110, g =>
                {
                    g.Medium("freew.hf-insert-page-number", "Page Number", RibbonCommandIconKind.PageNumber, menu: m =>
                    {
                        m.Item("freew.hf-insert-page-number",        "In Header", "H");
                        m.Item("freew.hf-insert-page-number-footer", "In Footer", "F");
                    });
                    g.Medium("freew.hf-insert-datetime", "Date && Time", RibbonCommandIconKind.Date);
                    g.Medium("freew.hf-insert-field",    "Document Info", RibbonCommandIconKind.Field);
                });
                // Navigation group — go to header/footer slot and close edit mode.
                tab.Group("hf-navigation", "Navigation", "N", 100, g =>
                {
                    g.Medium("freew.hf-go-to-header", "Go to Header", RibbonCommandIconKind.Header);
                    g.Medium("freew.hf-go-to-footer", "Go to Footer", RibbonCommandIconKind.Footer);
                });
                // Options group — layout toggles backed by PageSettings booleans.
                tab.Group("hf-options", "Options", "O", 90, g =>
                {
                    g.Medium("freew.hf-different-first-page", "Different First Page", RibbonCommandIconKind.CoverPage);
                    g.Medium("freew.hf-different-odd-even",   "Different Odd && Even Pages", RibbonCommandIconKind.OnePage);
                });
                // Position group — numeric header/footer distance spinboxes backed by PageSettings.
                tab.Group("hf-position", "Position", "P", 80, g =>
                {
                    g.ComboBox("freew.hf-header-from-top", "Header from Top", c => c with
                    {
                        Items = new[] { "0", "18", "36", "54", "72" },
                        Width = 80
                    });
                    g.ComboBox("freew.hf-footer-from-bottom", "Footer from Bottom", c => c with
                    {
                        Items = new[] { "0", "18", "36", "54", "72" },
                        Width = 80
                    });
                });
                // Close group — exit header/footer edit mode.
                tab.Group("hf-close", "Close", "C", 70, g =>
                {
                    g.Medium("freew.hf-close", "Close Header and Footer", RibbonCommandIconKind.WindowClose);
                });
            })
            .Build();

        return definition with { Tabs = OrderVisibleTabs(definition.Tabs) };
    }

    private static IReadOnlyList<RibbonTab> OrderVisibleTabs(IReadOnlyList<RibbonTab> tabs)
    {
        string[] wordOrder =
        [
            "home",
            "insert",
            "design",
            "layout",
            "references",
            "mailings",
            "review",
            "view",
            "help",
            "developer"
        ];

        var visibleOrder = wordOrder
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);

        var visible = tabs
            .Where(tab => !tab.IsContextual)
            .OrderBy(tab => visibleOrder.TryGetValue(tab.Id, out var index) ? index : int.MaxValue)
            .ThenBy(tab => visibleOrder.ContainsKey(tab.Id) ? 0 : 1)
            .ToArray();
        var contextual = tabs.Where(tab => tab.IsContextual).ToArray();

        return visible.Concat(contextual).ToArray();
    }
}
