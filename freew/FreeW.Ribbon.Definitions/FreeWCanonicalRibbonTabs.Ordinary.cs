using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

internal static partial class FreeWCanonicalRibbonTabs
{
    internal static RibbonDefinitionBuilder AddHomeTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
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
        return builder.Tab("home", homeTab.Label, homeTab.KeyTip, tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "home.clipboard",
                tab => tab.Group("clipboard", clipboardGroup.Label, clipboardGroup.KeyTip, 100, g =>
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
                    }),
                tab => tab.Group("clipboard", FreeWRibbonText.ClipboardGroup.Label, FreeWRibbonText.ClipboardGroup.KeyTip, 100, g =>
                    {
                        g.Button("freew.cut", FreeWRibbonText.CutCommand.Label, b => b with
                        {
                            KeyTip = FreeWRibbonText.CutCommand.KeyTip
                        });
                        g.Button("freew.copy", FreeWRibbonText.CopyCommand.Label, b => b with
                        {
                            KeyTip = FreeWRibbonText.CopyCommand.KeyTip
                        });
                        g.Button("freew.paste", FreeWRibbonText.PasteCommand.Label, b => b with
                        {
                            KeyTip = FreeWRibbonText.PasteCommand.KeyTip
                        });
                        g.Button("freew.format-painter", FreeWRibbonText.FormatPainterCommand.Label, b => b with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormatPainter),
                            KeyTip = FreeWRibbonText.FormatPainterCommand.KeyTip
                        });
                        g.Icon("freew.paste-plain", FreeWRibbonText.PasteTextOnlyCommand.Label, RibbonCommandIconKind.Paste);
                        g.Icon("freew.paste-merge", FreeWRibbonText.PasteMergeFormattingCommand.Label, RibbonCommandIconKind.Paste);
                        g.Icon("freew.paste-special", FreeWRibbonText.PasteSpecialCommand.Label, RibbonCommandIconKind.Paste);
                    }));

            topology.Section(
                "home.font",
                tab => tab.Group("font", fontGroup.Label, fontGroup.KeyTip, 90, g =>
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
                    }),
                tab => tab.Group("font", FreeWRibbonText.FontGroup.Label, FreeWRibbonText.FontGroup.KeyTip, 90, g =>
                    {
                        g.ComboBox("freew.font-family", FreeWRibbonText.FontFamilyCommand.Label, c => c with { Items = FontFamilies, Width = 128 });
                        g.ComboBox("freew.font-size", FreeWRibbonText.FontSizeCommand.Label, c => c with { Items = FontSizes, Width = 64 });
                        g.Toggle("freew.bold", FreeWRibbonText.BoldCommand.Label, b => b with { KeyTip = FreeWRibbonText.BoldCommand.KeyTip });
                        g.Toggle("freew.italic", FreeWRibbonText.ItalicCommand.Label, b => b with { KeyTip = FreeWRibbonText.ItalicCommand.KeyTip });
                        g.Toggle("freew.underline", FreeWRibbonText.UnderlineCommand.Label, b => b with { KeyTip = FreeWRibbonText.UnderlineCommand.KeyTip });
                        g.Toggle("freew.strikethrough", FreeWRibbonText.StrikethroughCommand.Label);
                        g.Toggle("freew.superscript", FreeWRibbonText.SuperscriptCompactCommand.Label);
                        g.Toggle("freew.subscript", FreeWRibbonText.SubscriptCompactCommand.Label);
                        g.Toggle("freew.smallcaps", FreeWRibbonText.SmallCapsCommand.Label);
                        g.Toggle("freew.allcaps", FreeWRibbonText.AllCapsCommand.Label);
                        g.Dropdown("freew.highlight", FreeWRibbonText.HighlightCompactCommand.Label, BuildHighlightMenu(), d => d with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Highlight)
                        });
                        g.Dropdown("freew.char-border", FreeWRibbonText.CharacterBorderCommand.Label, BuildCharacterBorderMenu(), d => d with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border)
                        });
                        g.Dropdown("freew.char-shading", FreeWRibbonText.CharacterShadingCommand.Label, BuildCharacterShadingMenu(), d => d with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill)
                        });
                        g.Button("freew.grow-font", FreeWRibbonText.GrowFontCompactCommand.Label);
                        g.Button("freew.shrink-font", FreeWRibbonText.ShrinkFontCompactCommand.Label);
                        g.Button("freew.clear-formatting", FreeWRibbonText.ClearFormattingCompactCommand.Label);
                        g.Dropdown("freew.font-color", FreeWRibbonText.FontColorDropdownCommand.Label, BuildFontColorMenu());
                        g.Button("freew.change-case", FreeWRibbonText.ChangeCaseCompactCommand.Label);
                        g.Button("freew.font-dialog", FreeWRibbonText.FontDialogCommand.Label);
                    }));

            topology.Section(
                "home.paragraph",
                tab => tab.Group("paragraph", paragraphGroup.Label, paragraphGroup.KeyTip, 80, g =>
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
                    }),
                tab => tab.Group("paragraph", FreeWRibbonText.ParagraphGroup.Label, null, 80, g =>
                    {
                        g.Toggle("freew.bullets", FreeWRibbonText.BulletsCommand.Label);
                        g.Toggle("freew.numbering", FreeWRibbonText.NumberingCommand.Label);
                        g.Dropdown("freew.multilevel-list", FreeWRibbonText.MultilevelListCommand.Label, BuildMultilevelListMenu(), d => d with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.MultilevelList)
                        });
                        g.Button("freew.indent-decrease", "Decrease Indent", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentDecrease)
                        });
                        g.Button("freew.indent-increase", "Increase Indent", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentIncrease)
                        });
                        g.Button("freew.align-left", "Left");
                        g.Button("freew.align-center", "Center");
                        g.Button("freew.align-right", "Right");
                        g.Button("freew.align-justify", "Justify");
                        g.Button("freew.sort", "Sort", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sort)
                        });
                        g.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", c => c with
                        {
                            Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.LineSpacing),
                            Width = 52
                        });
                        g.Dropdown("freew.para-shading", "Shading", BuildParagraphShadingMenu(), d => d with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill)
                        });
                        g.Button("freew.para-border", "Borders", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border)
                        });
                        g.Button("freew.borders-shading", "Borders and Shading...", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border)
                        });
                        g.Button("freew.space-before-toggle", "Add Space Before Paragraph", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceBefore)
                        });
                        g.Button("freew.space-after-toggle", "Add Space After Paragraph", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceAfter)
                        });
                        g.Button("freew.keep-with-next", "Keep with Next", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction)
                        });
                        g.Button("freew.keep-lines", "Keep Lines Together", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction)
                        });
                        g.Button("freew.widow-control", "Widow/Orphan Control", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextFunction)
                        });
                        g.Button("freew.tabs-dialog", "Tabs", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Ruler)
                        });
                        g.Toggle("freew.formatting-marks", "Show Formatting Marks", t => t with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormattingMarks)
                        });
                        g.Button("freew.paragraph-dialog", "Paragraph…");
                    }));

            topology.Section(
                "home.styles",
                tab => tab.Group("styles", "Styles", "S", 65, g =>
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
                    }),
                tab => tab.Group("styles", "Styles", null, 75, g =>
                    {
                        // Quick-style buttons (kept from the A1 wave; now model-backed via ApplyNamedStyle).
                        g.Button("freew.style-normal", "Normal");
                        g.Button("freew.style-heading1", "Heading 1");
                        g.Button("freew.style-heading2", "Heading 2");
                        g.Button("freew.style-heading3", "Heading 3");
                        g.Button("freew.style-title", "Title");
                        // AV-STYLES: full built-in style gallery dropdown + clear-style.
                        g.Dropdown("freew.styles-gallery", "Styles", BuildStylesMenu());
                        g.Button("freew.style-clear", "Clear Style");
                        g.Button("freew.new-style", "New Style", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Insert)
                        });
                        g.Button("freew.manage-styles", "Manage Styles", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects)
                        });
                    }));

            topology.Section(
                "home.formatting",
                tab => tab.Group("formatting", "Formatting", "M", 70, g =>
                    {
                        g.MediumToggle("freew.reveal-formatting", "Reveal Formatting", RibbonCommandIconKind.Info);
                    }));

            topology.Section(
                "home.editing",
                tab => tab.Group("editing", "Editing", "E", 75, g =>
                    {
                        g.Medium("freew.undo", "Undo", RibbonCommandIconKind.Undo);
                        g.Medium("freew.redo", "Redo", RibbonCommandIconKind.Redo);
                        g.Medium("freew.find", "Find", RibbonCommandIconKind.Search, "F");
                        g.Medium("freew.replace", "Replace", RibbonCommandIconKind.Search, "R");
                        g.Medium("freew.select", "Select", RibbonCommandIconKind.Search, "SL");
                    }),
                tab => tab.Group("editing", "Editing", null, 70, g =>
                    {
                        g.Button("freew.undo", "Undo");
                        g.Button("freew.redo", "Redo");
                        g.Button("freew.find", "Find");
                        g.Button("freew.replace", "Replace");
                        g.Button("freew.select", "Select");
                    }));

            topology.Build();
        });
    }

    internal static RibbonDefinitionBuilder AddInsertTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var symbolsGroup = FreeWRibbonText.SymbolsGroup;
        var symbolCommand = FreeWRibbonText.SymbolCommand;
        return builder.Tab("insert", "Insert", (capabilities.UsesPortableControls ? "I" : "N"), tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "insert.pages",
                tab => tab.Group("pages", "Pages", "P", 100, g =>
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
                    }),
                tab => tab.Group("pages", "Pages", null, 100, g =>
                    {
                        // AV-INSERT2: Cover Page (gallery of presets) + Page Break.
                        g.Dropdown("freew.cover-page", "Cover Page", BuildCoverPageMenu());
                        g.Button("freew.blank-page", "Blank Page");
                        g.Button("freew.page-break", "Page Break");
                        g.Button("freew.horizontal-rule", "Horizontal Rule");
                    }));

            topology.Section(
                "insert.tables",
                tab => tab.Group("tables", "Tables", "T", 90, g => g.Large("freew.table", "Table", RibbonCommandIconKind.Table, dropdown: true)),
                tab => tab.Group("tables", "Tables", null, 98, g =>
                    {
                        g.Button("freew.insert-table", "Table");
                        g.Dropdown("freew.table", "Table…", BuildTableSizeMenu());
                    }));

            topology.Section(
                "insert.illustrations",
                tab => tab.Group("illustrations", "Illustrations", "I", 88, g =>
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
                    }),
                tab => tab.Group("illustrations", "Illustrations", null, 96, g =>
                    {
                        g.Button("freew.picture", "Picture");
                        g.Button("freew.shape", "Shape");
                        g.Button("freew.smartart", "SmartArt");
                        g.Button("freew.chart", "Chart");
                        g.Dropdown("freew.screenshot", "Screenshot", new RibbonMenu(new[]
                        {
                            new RibbonMenuItem("Screen Clipping", new RibbonCommandId("freew.screen-clipping")),
                        }));
                        g.Button("freew.insert-icon", "Icons");
                        g.Button("freew.text-box", "Text Box");
                    }));

            topology.Section(
                "insert.links",
                tab => tab.Group("links", "Links", "K", 70, g =>
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
                    }),
                tab => tab.Group("links", "Links", null, 95, g =>
                    {
                        g.Button("freew.hyperlink", "Hyperlink");
                        g.Button("freew.insert-hyperlink", "Hyperlink");
                        g.Button("freew.edit-hyperlink", "Edit Hyperlink");
                        g.Button("freew.remove-hyperlink", "Remove Hyperlink");
                        g.Button("freew.hyperlink-tooltip", "ScreenTip");
                        g.Button("freew.bookmark", "Bookmark");
                        g.Button("freew.insert-bookmark", "Bookmark");
                        g.Button("freew.link-bookmark", "Link to Bookmark");
                        g.Button("freew.bookmark-manager", "Bookmark Manager");
                    }));

            topology.Section(
                "insert.header-footer",
                tab => tab.Group("header-footer", "Header & Footer", "H", 60, g =>
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
                    }),
                tab => tab.Group("header-footer", "Header & Footer", null, 94, g =>
                    {
                        g.Button("freew.header", "Header");
                        g.Button("freew.footer", "Footer");
                        g.Dropdown("freew.page-number", "Page Number", new RibbonMenu(new[]
                        {
                            new RibbonMenuItem("Top of Page", new RibbonCommandId("freew.page-number-top")),
                            new RibbonMenuItem("Bottom of Page", new RibbonCommandId("freew.page-number-bottom")),
                            new RibbonMenuItem("Current Position", new RibbonCommandId("freew.page-number-current")),
                            RibbonMenuItem.Separator(),
                            new RibbonMenuItem("Format Page Numbers...", new RibbonCommandId("freew.page-number-format")),
                        }));
                    }));

            topology.Section(
                "insert.text",
                tab => tab.Group("text", "Text", "X", 74, g =>
                    {
                        // Text Box gallery: Simple (plain), Sidebar/Banded (accent fill), and Quote (indented
                        // italic) presets — each inserts a pre-styled Shape.TextBox at the caret. The top-level
                        // id falls through to Simple (same as the existing plain text-box insert).
                        g.Icon("freew.shape-textbox", "Text Box", RibbonCommandIconKind.TextBox, menu: m =>
                        {
                            m.Item("freew.textbox-simple", "Simple Text Box", "S");
                            m.Item("freew.textbox-sidebar", "Sidebar (Banded)", "B");
                            m.Item("freew.textbox-quote", "Quote", "Q");
                        });
                        // Quick Parts: a dropdown with Document Property sub-items + the existing AutoText entry.
                        g.Icon("freew.insert-quickpart", "Quick Parts", RibbonCommandIconKind.QuickParts, menu: m =>
                        {
                            m.Item("freew.docprop-title", "Document Property: Title", "T");
                            m.Item("freew.docprop-subject", "Document Property: Subject", "S");
                            m.Item("freew.docprop-author", "Document Property: Author", "A");
                            m.Item("freew.docprop-keywords", "Document Property: Keywords", "K");
                            m.Item("freew.docprop-comments", "Document Property: Comments", "C");
                            m.Separator();
                            m.Item("freew.field", "Field…", "F");
                            m.Separator();
                            m.Item("freew.save-quickpart", "Save Selection to Quick Part Gallery…", "V");
                            m.Item("freew.building-blocks-organizer", "Building Blocks Organizer…", "B");
                        });
                        g.Icon("freew.insert-file", "Text from File", RibbonCommandIconKind.TextFromFile);
                        g.Icon("freew.wordart", "WordArt", RibbonCommandIconKind.WordArt);
                        g.RowBreak();
                        // Drop Cap: top-level applies the default drop cap; dropdown opens the options dialog.
                        g.Icon("freew.drop-cap", "Drop Cap", RibbonCommandIconKind.DropCap, menu: m =>
                        {
                            m.Item("freew.drop-cap-dropped", "Dropped", "D");
                            m.Item("freew.drop-cap-in-margin", "In Margin", "M");
                            m.Item("freew.drop-cap-none", "None (Remove)", "N");
                            m.Separator();
                            m.Item("freew.drop-cap-options", "Drop Cap Options…", "O");
                        });
                        g.Icon("freew.datetime", "Date & Time", RibbonCommandIconKind.Date);
                        g.Icon("freew.field", "Field", RibbonCommandIconKind.Field);
                        g.Icon("freew.update-fields", "Update Fields", RibbonCommandIconKind.Refresh);
                        g.Icon("freew.toggle-field-codes", "Toggle Field Codes", RibbonCommandIconKind.Field);
                        g.Icon("freew.object", "Object", RibbonCommandIconKind.Object);
                        g.Icon("freew.save-quickpart", "Save Selection", RibbonCommandIconKind.QuickParts);
                        g.Icon("freew.building-blocks-organizer", "Building Blocks Organizer", RibbonCommandIconKind.QuickParts);
                    }),
                tab => tab.Group("text", "Text", null, 93, g =>
                    {
                        g.Dropdown("freew.quick-parts", "Quick Parts", BuildQuickPartsMenu());
                        g.Dropdown("freew.drop-cap", "Drop Cap", BuildDropCapMenu());
                        g.Button("freew.insert-file", "Text from File");
                        g.Button("freew.wordart", "WordArt");
                        g.Button("freew.datetime", "Date & Time");
                        g.Button("freew.field", "Field", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Field)
                        });
                        g.Button("freew.update-fields", "Update Fields");
                        g.Button("freew.toggle-field-codes", "Toggle Field Codes");
                        g.Button("freew.object", "Object");
                        g.Button("freew.save-quickpart", "Save Selection", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.QuickParts)
                        });
                        g.Button("freew.building-blocks-organizer", "Building Blocks Organizer", b => b with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.QuickParts)
                        });
                    }));

            topology.Section(
                "insert.symbols",
                tab => tab.Group("symbols", symbolsGroup.Label, symbolsGroup.KeyTip, 50, g =>
                    {
                        // Equation gallery: the top-level id inserts the default sample equation (E = mc^2),
                        // and the dropdown offers Word's common structure presets.
                        g.Medium("freew.equation", "Equation", RibbonCommandIconKind.Equation, menu: m =>
                        {
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Fraction).CommandId, "Fraction", "F");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Script).CommandId, "Subscript / Superscript", "S");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Radical).CommandId, "Radical (Square Root)", "R");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.NthRoot).CommandId, "Radical (nth Root)", "N");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Integral).CommandId, "Integral", "I");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Summation).CommandId, "Summation", "U");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Product).CommandId, "Product", "P");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Accent).CommandId, "Accent (Hat)", "A");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Bar).CommandId, "Overbar", "O");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Bracket).CommandId, "Bracket", "B");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Matrix).CommandId, "Matrix (2x2)", "M");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.Function).CommandId, "Function (sin)", "C");
                            m.Item(EquationPresetCatalog.Get(EquationPresetKind.GroupCharacter).CommandId, "Group (brace)", "G");
                        });
                        g.Medium("freew.symbol", symbolCommand.Label, RibbonCommandIconKind.Symbol);
                    }),
                tab => tab.Group("symbols", FreeWRibbonText.SymbolsGroup.Label, null, 92, g =>
                    {
                        g.Button("freew.symbol", FreeWRibbonText.SymbolCommand.Label);
                        // AV-INSERT2: Equation — default (E=mc²) opener + a few common OMML presets.
                        g.Dropdown("freew.equation", "Equation", BuildEquationMenu());
                    }));

            topology.Build();
        });
    }

    internal static RibbonDefinitionBuilder AddReferencesTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        return builder.Tab("references", "References", (capabilities.UsesPortableControls ? "S" : "R"), tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "references.contents",
                tab => tab.Group("table-of-contents", "Table of Contents", "T", 100, g =>
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
                    }),
                tab => tab.Group("toc", "Table of Contents", null, 110, g =>
                    {
                        g.Button("freew.toc", "Table of Contents");
                        g.Button("freew.toc-refresh", "Update Table");
                    }));

            topology.Section(
                "references.footnotes",
                tab => tab.Group("footnotes", "Footnotes", "F", 92, g =>
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
                    }),
                tab => tab.Group("footnotes", "Footnotes", null, 100, g =>
                    {
                        g.Button("freew.footnote", "Insert Footnote");
                        g.Button("freew.endnote", "Insert Endnote");
                        g.Button("freew.next-footnote", "Next Footnote");
                        g.Button("freew.previous-footnote", "Previous Footnote");
                        g.Button("freew.next-endnote", "Next Endnote");
                        g.Button("freew.previous-endnote", "Previous Endnote");
                        g.Button("freew.show-notes", "Show Notes");
                        g.Button("freew.footnote-endnote-options", "Footnote/Endnote Options...");
                    }));

            topology.Section(
                "references.citations",
                tab => tab.Group("citations", "Citations & Bibliography", "C", 84, g =>
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
                    }),
                tab => tab.Group("citations", "Citations & Bibliography", null, 90, g =>
                    {
                        g.Button("freew.citation", "Insert Citation");
                        g.Button("freew.manage-sources", "Manage Sources");
                        g.ComboBox("freew.citation-style", "Style", c => c with
                        {
                            Items = FreeWRibbonDefinitionData.CitationStyleNames,
                            Width = 90
                        });
                        g.Button("freew.bibliography", "Bibliography");
                    }));

            topology.Section(
                "references.captions",
                tab => tab.Group("captions", "Captions", "P", 78, g =>
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
                    }),
                tab => tab.Group("captions", "Captions", null, 80, g =>
                    {
                        g.Dropdown("freew.caption", "Insert Caption", BuildCaptionMenu());
                        g.Dropdown("freew.tof", "Insert Table of Figures", BuildTableOfFiguresMenu());
                        g.Button("freew.tof-refresh", "Update Table");
                        g.Button("freew.cross-reference", "Cross-reference");
                    }));

            topology.Section(
                "references.index",
                tab => tab.Group("index", "Index", "I", 72, g =>
                    {
                        g.Medium("freew.index-mark", "Mark Entry", RibbonCommandIconKind.Index);
                        g.Medium("freew.index-insert", "Insert Index", RibbonCommandIconKind.Index);
                        g.Medium("freew.index-refresh", "Update Index", RibbonCommandIconKind.Refresh);
                    }),
                tab => tab.Group("index", "Index", null, 70, g =>
                    {
                        g.Button("freew.index-mark", "Mark Entry");
                        g.Button("freew.index-insert", "Insert Index");
                        g.Button("freew.index-refresh", "Update Index");
                    }));

            topology.Section(
                "references.authorities",
                tab => tab.Group("authorities", "Table of Authorities", "A", 66, g =>
                    {
                        g.Medium("freew.mark-citation", "Mark Citation", RibbonCommandIconKind.Citation);
                        g.Medium("freew.table-of-authorities", "Insert Table of Authorities", RibbonCommandIconKind.Bibliography);
                        g.Medium("freew.table-of-authorities-refresh", "Update Table", RibbonCommandIconKind.Refresh);
                    }),
                tab => tab.Group("authorities", "Table of Authorities", null, 60, g =>
                    {
                        g.Button("freew.mark-citation", "Mark Citation");
                        g.Button("freew.table-of-authorities", "Insert Table of Authorities");
                        g.Button("freew.table-of-authorities-refresh", "Update Table");
                    }));

            topology.Build();
        });
    }

    internal static RibbonDefinitionBuilder AddReviewTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        return builder.Tab("review", "Review", "R", tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "review.proofing",
                tab => tab.Group("proofing", "Proofing", "P", 100, g =>
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
                    }),
                tab => tab.Group("proofing", "Proofing", null, 110, g =>
                    {
                        g.Button("freew.statistics", "Word Count");
                        g.Toggle("freew.spellcheck-toggle", "Spelling & Grammar");
                        g.Button("freew.add-to-dictionary", "Add to Dictionary");
                        g.Button("freew.thesaurus", "Thesaurus");
                        g.Button("freew.set-proofing-language", "Set Proofing Language");
                    }));

            topology.Section(
                "review.speech",
                tab => tab.Group("speech", "Speech", "S", 97, g =>
                    {
                        g.MediumToggle("freew.read-aloud", "Read Aloud", RibbonCommandIconKind.ReadAloud);
                    }),
                tab => tab.Group("speech", "Speech", null, 105, g =>
                    {
                        g.Toggle("freew.read-aloud", "Read Aloud");
                    }));

            topology.Section(
                "review.accessibility",
                tab => tab.Group("accessibility", "Accessibility", "A", 92, g =>
                    {
                        g.Medium("freew.check-accessibility", "Check Accessibility", RibbonCommandIconKind.Accessibility);
                    }),
                tab => tab.Group("accessibility", "Accessibility", null, 92, g =>
                    {
                        g.Button("freew.check-accessibility", "Check Accessibility");
                    }));

            topology.Section(
                "review.comments",
                tab => tab.Group("comments", "Comments", "C", 95, g =>
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
                    }),
                tab => tab.Group("comments", "Comments", null, 100, g =>
                    {
                        g.Button("freew.new-comment", "New Comment");
                        g.Button("freew.delete-comment", "Delete");
                        g.Button("freew.previous-comment", "Previous");
                        g.Button("freew.next-comment", "Next");
                        g.Button("freew.reply-comment", "Reply");
                        g.Button("freew.resolve-comment", "Resolve");
                        g.Button("freew.show-comments", "Show Comments");
                    }));

            topology.Section(
                "review.tracking",
                tab => tab.Group("tracking", "Tracking", "G", 90, g =>
                    {
                        // Track Changes is the big toggle; the Reviewing Pane toggle opens the dockable revisions
                        // list. Accept/Reject live in Changes, mirroring Word's group geography.
                        g.MediumToggle("freew.track-changes", "Track Changes", RibbonCommandIconKind.History);
                        g.MediumToggle("freew.track-formatting", "Track Formatting", RibbonCommandIconKind.History);
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
                    }),
                tab => tab.Group("tracking", "Tracking", null, 90, g =>
                    {
                        g.Toggle("freew.track-changes", "Track Changes");
                        g.Toggle("freew.track-formatting", "Track Formatting");
                        g.Toggle("freew.reviewing-pane", "Reviewing Pane");
                        g.Dropdown("freew.display-for-review", "All Markup", BuildDisplayForReviewMenu());
                        g.Dropdown("freew.show-markup", "Show Markup", BuildShowMarkupMenu());
                    }));

            topology.Section(
                "review.changes",
                tab => tab.Group("changes", "Changes", "H", 88, g =>
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
                    }),
                tab => tab.Group("changes", "Changes", null, 80, g =>
                    {
                        g.Button("freew.accept-this", "Accept");
                        g.Button("freew.accept-all", "Accept All");
                        g.Button("freew.reject-this", "Reject");
                        g.Button("freew.reject-all", "Reject All");
                        g.Button("freew.previous-change", "Previous");
                        g.Button("freew.next-change", "Next");
                    }));

            topology.Section(
                "review.protect",
                tab => tab.Group("protect", "Protect", "T", 85, g =>
                    {
                        g.MediumToggle("freew.mark-as-final", "Mark as Final", RibbonCommandIconKind.Protect);
                        g.MediumToggle("freew.restrict-editing", "Restrict Editing", RibbonCommandIconKind.Protect);
                    }),
                tab => tab.Group("compare", "Compare", null, 78, g =>
                    {
                        g.Button("freew.compare", "Compare");
                        g.Button("freew.combine", "Combine");
                    }));

            topology.Section(
                "review.compare",
                tab => tab.Group("compare", "Compare", "M", 80, g =>
                    {
                        g.Medium("freew.compare", "Compare", RibbonCommandIconKind.Compare);
                        g.Medium("freew.combine", "Combine", RibbonCommandIconKind.Compare);
                    }),
                tab => tab.Group("protect", "Protect", null, 85, g =>
                    {
                        g.Toggle("freew.mark-as-final", "Mark as Final");
                        g.Toggle("freew.restrict-editing", "Restrict Editing");
                    }));

            topology.Section(
                "review.inspect",
                tab => tab.Group("inspect", "Inspect", "I", 75, g =>
                    {
                        g.Medium("freew.inspect-document", "Inspect Document", RibbonCommandIconKind.Search);
                    }),
                tab => tab.Group("inspect", "Inspect", null, 75, g =>
                    {
                        g.Button("freew.inspect-document", "Inspect Document");
                    }));

            topology.Build();
        });
    }

    private static readonly string[] FontSizes = FreeWRibbonDefinitionData.FontSizes;
    private static readonly string[] FontFamilies = FreeWRibbonDefinitionData.FontFamilies;

    private static RibbonButton Icon(
        RibbonButton button,
        RibbonCommandIconKind kind,
        RibbonCommandIconAccent accent = RibbonCommandIconAccent.None) =>
        button with { Icon = new RibbonCommandIcon(kind, accent) };

    private static RibbonMenu BuildFontColorMenu() =>
        new(FreeWRibbonDefinitionData.FontColors
            .Select(fc => new RibbonMenuItem(fc.Label, new RibbonCommandId(fc.CommandId)))
            .ToArray());

    private static RibbonMenu BuildParagraphShadingMenu() =>
        BuildPaletteMenu(FreeWRibbonPaletteCatalog.ParagraphShading);

    private static RibbonMenu BuildCharacterShadingMenu() =>
        BuildPaletteMenu(FreeWRibbonPaletteCatalog.CharacterShading);

    private static RibbonMenu BuildCharacterBorderMenu() =>
        BuildPaletteMenu(FreeWRibbonPaletteCatalog.CharacterBorders);

    private static RibbonMenu BuildHighlightMenu() =>
        BuildPaletteMenu(FreeWRibbonPaletteCatalog.Highlights);

    private static RibbonMenu BuildPaletteMenu(IReadOnlyList<FreeWRibbonPaletteChoice> choices)
    {
        var items = new List<RibbonMenuItem>(choices.Count + 1);
        foreach (var choice in choices)
        {
            if (choice.StartsNewGroup)
                items.Add(RibbonMenuItem.Separator());
            items.Add(new RibbonMenuItem(choice.Label, new RibbonCommandId(choice.CommandId)));
        }

        return new RibbonMenu(items);
    }

    private static RibbonMenu BuildDisplayForReviewMenu() =>
        new(new RibbonMenuItem[]
        {
            new("All Markup", new RibbonCommandId("freew.display-for-review-all-markup")),
            new("Simple Markup", new RibbonCommandId("freew.display-for-review-simple-markup")),
            new("No Markup", new RibbonCommandId("freew.display-for-review-no-markup")),
            new("Original", new RibbonCommandId("freew.display-for-review-original")),
        });

    private static RibbonMenu BuildShowMarkupMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Insertions and Deletions", new RibbonCommandId("freew.show-markup-insertions-deletions")),
            new("Comments", new RibbonCommandId("freew.show-markup-comments")),
            new("Formatting", new RibbonCommandId("freew.show-markup-formatting")),
            RibbonMenuItem.Separator(),
            new("Show Revisions in Balloons", new RibbonCommandId("freew.show-markup-balloons")),
        });

    /// <summary>AV-INSERT: Insert &gt; Table dropdown — common row×column size presets.</summary>
    private static RibbonMenu BuildTableSizeMenu() =>
        new(new RibbonMenuItem[]
        {
            new("2 × 2 Table",       new RibbonCommandId("freew.table-2x2")),
            new("3 × 3 Table",       new RibbonCommandId("freew.table-3x3")),
            new("4 × 4 Table",       new RibbonCommandId("freew.table-4x4")),
            new("5 × 2 Table",       new RibbonCommandId("freew.table-5x2")),
        });

    /// <summary>AV-REF: References &gt; Insert Caption dropdown — Figure / Table caption labels.</summary>
    private static RibbonMenu BuildCaptionMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Figure", new RibbonCommandId("freew.insert-caption.figure")),
            new("Table",  new RibbonCommandId("freew.insert-caption.table")),
            new("Equation", new RibbonCommandId("freew.insert-caption.equation")),
        });

    private static RibbonMenu BuildTableOfFiguresMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Figure", new RibbonCommandId("freew.tof.figure")),
            new("Table", new RibbonCommandId("freew.tof.table")),
            new("Equation", new RibbonCommandId("freew.tof.equation")),
        });

    private static RibbonMenu BuildMultilevelListMenu() =>
        new(new RibbonMenuItem[]
        {
            new(FreeWRibbonText.MultilevelPromoteCommand.Label, new RibbonCommandId("freew.multilevel-promote")),
            new(FreeWRibbonText.MultilevelDemoteCommand.Label, new RibbonCommandId("freew.multilevel-demote")),
            new(FreeWRibbonDefinitionData.MultilevelListPresetNames[0], new RibbonCommandId("freew.multilevel-preset-0")),
            new(FreeWRibbonDefinitionData.MultilevelListPresetNames[1], new RibbonCommandId("freew.multilevel-preset-1")),
            new(FreeWRibbonDefinitionData.MultilevelListPresetNames[2], new RibbonCommandId("freew.multilevel-preset-2")),
            new(FreeWRibbonText.MultilevelDefineCommand.Label, new RibbonCommandId("freew.multilevel-define")),
        });

    /// <summary>
    /// AV-STYLES: Home &gt; Styles gallery dropdown — the full built-in style set (paragraph and character
    /// styles), one item per <see cref="BuiltInStyles.Gallery"/> entry. Each item's command id is
    /// <c>freew.style.&lt;id&gt;</c> (matching <see cref="FreeWRibbonDefinitionData.StyleCommandId"/>).
    /// </summary>
    private static RibbonMenu BuildStylesMenu() =>
        new(BuiltInStyles.Gallery
            .Select(d => new RibbonMenuItem(
                d.Type == StyleType.Character ? $"{d.Name}  (a)" : d.Name,
                new RibbonCommandId(FreeWRibbonDefinitionData.StyleCommandId(d.Id))))
            .ToArray());
    /// <summary>AV-INSERT2: Insert &gt; Cover Page gallery — the three built-in cover-page presets.</summary>
    private static RibbonMenu BuildCoverPageMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Default", new RibbonCommandId("freew.cover-page.default")),
            new("Banded",  new RibbonCommandId("freew.cover-page.banded")),
            new("Motion",  new RibbonCommandId("freew.cover-page.motion")),
        });

    /// <summary>AV-INSERT2: Insert &gt; Drop Cap menu matching the WPF host routes.</summary>
    private static RibbonMenu BuildDropCapMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Dropped",   new RibbonCommandId("freew.drop-cap-dropped")),
            new("In Margin", new RibbonCommandId("freew.drop-cap-in-margin")),
            new("None (Remove)", new RibbonCommandId("freew.drop-cap-none")),
            RibbonMenuItem.Separator(),
            new("Drop Cap Options...", new RibbonCommandId("freew.drop-cap-options")),
        });

    /// <summary>
    /// AV-INSERT2: Insert &gt; Quick Parts menu — Word document-property fields, a Date field, and a
    /// free-text snippet (opens a dialog). Command ids match the registry wiring.
    /// </summary>
    private static RibbonMenu BuildQuickPartsMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Document Property — Title",   new RibbonCommandId("freew.quick-parts.title")),
            new("Document Property — Author",  new RibbonCommandId("freew.quick-parts.author")),
            new("Document Property — Subject", new RibbonCommandId("freew.quick-parts.subject")),
            new("Document Property — Keywords", new RibbonCommandId("freew.quick-parts.keywords")),
            new("Document Property — Comments", new RibbonCommandId("freew.quick-parts.comments")),
            new("Field — Date",                new RibbonCommandId("freew.quick-parts.date")),
            RibbonMenuItem.Separator(),
            new("Insert Snippet…",             new RibbonCommandId("freew.quick-parts.snippet")),
            new("Field…",                      new RibbonCommandId("freew.field")),
            RibbonMenuItem.Separator(),
            new("Save Selection to Quick Part Gallery…", new RibbonCommandId("freew.save-quickpart")),
            new("Building Blocks Organizer…",  new RibbonCommandId("freew.building-blocks-organizer")),
        });

    /// <summary>
    /// AV-INSERT2: Insert &gt; Equation menu — a default sample (E=mc²) plus a few common OMML structures.
    /// Each preset maps to the canonical command id owned by <see cref="EquationPresetCatalog"/>.
    /// </summary>
    private static RibbonMenu BuildEquationMenu() =>
        new(new RibbonMenuItem[]
        {
            new("Insert New Equation", new RibbonCommandId(EquationPresetCatalog.DefaultCommandId)),
            RibbonMenuItem.Separator(),
            new("Fraction  a/b",       new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Fraction).CommandId)),
            new("Script  xⁿ",          new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Script).CommandId)),
            new("Radical  √x",         new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Radical).CommandId)),
            new("Nth Root",            new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.NthRoot).CommandId)),
            new("Integral  ∫",         new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Integral).CommandId)),
            new("Summation  ∑",        new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Summation).CommandId)),
            new("Product",             new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Product).CommandId)),
            RibbonMenuItem.Separator(),
            new("Accent",              new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Accent).CommandId)),
            new("Bar",                 new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Bar).CommandId)),
            new("Bracket",             new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Bracket).CommandId)),
            new("Matrix",              new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Matrix).CommandId)),
            new("Function",            new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.Function).CommandId)),
            new("Group Character",     new RibbonCommandId(EquationPresetCatalog.Get(EquationPresetKind.GroupCharacter).CommandId)),
        });
}
