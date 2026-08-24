using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Canonical FreeW tab topology shared by both renderers. Capability checks select only the
/// presentation shape that each host already supports; command ownership and ordering live here.
/// </summary>
internal static partial class FreeWCanonicalRibbonTabs
{
    internal static RibbonDefinitionBuilder AddLayoutTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.Tab("layout", "Layout", "L", tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "layout.page-setup",
                tab => tab.Group("page-setup", "Page Setup", "P", 100, group =>
                    {
                        group.Large("freew.margins", "Margins", RibbonCommandIconKind.Margins, "M", menu: menu =>
                        {
                            menu.Item("freew.margins", "Normal / Narrow (toggle)", "N");
                            menu.Item("freew.custom-margins", "Custom Margins\u2026", RibbonCommandIconKind.Margins, "A");
                        });
                        group.Medium("freew.orientation", "Orientation", RibbonCommandIconKind.Orientation, dropdown: true);
                        group.Medium("freew.size", "Size", RibbonCommandIconKind.OnePage, "Z", menu: menu =>
                        {
                            menu.Item("freew.size", "Letter / A4 (toggle)", "L");
                            menu.Item("freew.more-paper-sizes", "More Paper Sizes\u2026", RibbonCommandIconKind.OnePage, "M");
                        });
                        group.Medium("freew.columns", "Columns", RibbonCommandIconKind.TextColumns, menu: menu =>
                        {
                            menu.Item("freew.columns-one", "One", RibbonCommandIconKind.TextColumns, "O");
                            menu.Item("freew.columns-two", "Two", RibbonCommandIconKind.TextColumns, "T");
                            menu.Item("freew.columns-three", "Three", RibbonCommandIconKind.TextColumns, "H");
                            menu.Item("freew.columns-left", "Left", RibbonCommandIconKind.TextColumns, "L");
                            menu.Item("freew.columns-right", "Right", RibbonCommandIconKind.TextColumns, "R");
                            menu.Item("freew.columns-more", "More Columns...", RibbonCommandIconKind.TextColumns, "M");
                        });
                        group.Medium("freew.breaks", "Breaks", RibbonCommandIconKind.PageBreak, "B", menu: menu =>
                        {
                            menu.Item("freew.page-break", "Page Break", "P");
                            menu.Item("freew.column-break", "Column Break", "C");
                            menu.Separator();
                            menu.Item("freew.section-break-next-page", "Next Page", "N");
                            menu.Item("freew.section-break-continuous", "Continuous", "O");
                            menu.Item("freew.section-break-even-page", "Even Page", "E");
                            menu.Item("freew.section-break-odd-page", "Odd Page", "D");
                        });
                        group.RowBreak();
                        group.Icon("freew.page-setup", "Page Setup", RibbonCommandIconKind.Margins, "G");
                        group.Icon("freew.line-numbers", "Line Numbers", RibbonCommandIconKind.Number, menu: menu =>
                        {
                            menu.Item("freew.line-numbers-none", "None", "N");
                            menu.Item("freew.line-numbers-continuous", "Continuous", "C");
                            menu.Item("freew.line-numbers-restart-page", "Restart Each Page", "P");
                            menu.Item("freew.line-numbers-restart-section", "Restart Each Section", "S");
                            menu.Item("freew.line-numbers-options", "Line Numbering Options...", "O");
                        });
                        group.Icon("freew.hyphenation", "Hyphenation", RibbonCommandIconKind.Hyphenation, "HY", menu: menu =>
                        {
                            menu.Item("freew.hyphenation-none", "None", RibbonCommandIconKind.Hyphenation, "N");
                            menu.Item("freew.hyphenation-auto", "Automatic", RibbonCommandIconKind.Hyphenation, "A");
                            menu.Item("freew.hyphenation-manual", "Manual", RibbonCommandIconKind.Hyphenation, "M");
                            menu.Item("freew.hyphenation-options", "Hyphenation Options\u2026", RibbonCommandIconKind.Hyphenation, "H");
                        });
                        group.Icon("freew.page-valign", "Vertical Align", RibbonCommandIconKind.AlignJustify);
                        group.Icon("freew.different-first-page", "Different First Page", RibbonCommandIconKind.CoverPage);
                    }),
                tab => tab.Group("page-setup", "Page Setup", null, 100, group =>
                    {
                        group.Dropdown("freew.margins", "Margins", BuildAvaloniaMarginsMenu());
                        group.Button("freew.orientation", "Orientation");
                        group.Dropdown("freew.size", "Size", BuildAvaloniaPageSizeMenu());
                        group.Dropdown("freew.columns", "Columns", BuildAvaloniaColumnsMenu());
                        group.Dropdown("freew.breaks", "Breaks", BuildAvaloniaBreaksMenu());
                        group.Dropdown("freew.line-numbers", "Line Numbers", BuildAvaloniaLineNumbersMenu());
                        group.Dropdown("freew.hyphenation", "Hyphenation", BuildAvaloniaHyphenationMenu());
                        group.Toggle("freew.different-first-page", "Different First Page");
                        group.Button("freew.page-valign", "Vertical Align");
                        group.Button("freew.page-setup", "Page Setup...");
                    }));

            topology.Section(
                "layout.paragraph",
                tab => tab.Group("paragraph", FreeWRibbonText.ParagraphGroup.Label, "A", 76, group =>
                    {
                        group.Icon("freew.indent-decrease", "Decrease Indent", RibbonCommandIconKind.IndentDecrease);
                        group.Icon("freew.indent-increase", "Increase Indent", RibbonCommandIconKind.IndentIncrease);
                        group.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", control => control with
                        {
                            Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.LineSpacing),
                            Width = 52,
                        });
                        group.ComboBox("freew.indent-left", "Indent Left", control => control with
                        {
                            Items = new[] { "0", "18", "36", "54", "72" },
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentIncrease),
                            Width = 52,
                        });
                        group.ComboBox("freew.indent-right", "Indent Right", control => control with
                        {
                            Items = new[] { "0", "18", "36", "54", "72" },
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.IndentDecrease),
                            Width = 52,
                        });
                        group.RowBreak();
                        group.Icon("freew.space-before-toggle", "Add Space Before Paragraph", RibbonCommandIconKind.SpaceBefore);
                        group.Icon("freew.space-after-toggle", "Add Space After Paragraph", RibbonCommandIconKind.SpaceAfter);
                        group.ComboBox("freew.space-before", "Spacing Before", control => control with
                        {
                            Items = new[] { "0", "6", "12", "18", "24" },
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceBefore),
                            Width = 52,
                        });
                        group.ComboBox("freew.space-after", "Spacing After", control => control with
                        {
                            Items = new[] { "0", "6", "8", "12", "18", "24" },
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.SpaceAfter),
                            Width = 52,
                        });
                        group.Icon("freew.paragraph-dialog", "Paragraph Settings", RibbonCommandIconKind.TextFunction);
                        group.Icon("freew.tabs-dialog", "Tabs", RibbonCommandIconKind.Ruler);
                    }),
                tab => tab.Group("paragraph", FreeWRibbonText.ParagraphGroup.Label, null, 92, group =>
                    {
                        group.Button("freew.indent-decrease", "Decrease Indent");
                        group.Button("freew.indent-increase", "Increase Indent");
                        group.ComboBox("freew.line-spacing", "Line and Paragraph Spacing", control => control with
                        {
                            Items = new[] { "1.0", "1.15", "1.5", "2.0" },
                            Width = 52,
                        });
                        group.ComboBox("freew.indent-left", "Indent Left", control => control with
                        {
                            Items = new[] { "0", "18", "36", "54", "72" },
                            Width = 52,
                        });
                        group.ComboBox("freew.indent-right", "Indent Right", control => control with
                        {
                            Items = new[] { "0", "18", "36", "54", "72" },
                            Width = 52,
                        });
                        group.Button("freew.space-before-toggle", "Add Space Before Paragraph");
                        group.Button("freew.space-after-toggle", "Add Space After Paragraph");
                        group.ComboBox("freew.space-before", "Spacing Before", control => control with
                        {
                            Items = new[] { "0", "6", "12", "18", "24" },
                            Width = 52,
                        });
                        group.ComboBox("freew.space-after", "Spacing After", control => control with
                        {
                            Items = new[] { "0", "6", "8", "12", "18", "24" },
                            Width = 52,
                        });
                        group.Button("freew.paragraph-dialog", "Paragraph Settings");
                        group.Button("freew.tabs-dialog", "Tabs");
                    }));

            topology.Section(
                "layout.preview",
                tab => tab.Group("preview", "Preview", "V", 90, group =>
                    group.Large("freew.print-preview", "Print Preview", RibbonCommandIconKind.Print)),
                tab => tab.Group("preview", "Preview", null, 90, group =>
                    group.Button("freew.print-preview", "Print Preview")),
                portableOrder: 3);

            topology.Section(
                "layout.arrange",
                tab => tab.Group("arrange", "Arrange", "A", 75, group =>
                    {
                        group.Medium("freew.layout-wrap", "Wrap Text", RibbonCommandIconKind.Wrap, menu: menu =>
                        {
                            menu.Item("freew.layout-wrap-inline", "In Line with Text", "I");
                            menu.Item("freew.layout-wrap-square", "Square", "S");
                            menu.Item("freew.layout-wrap-tight", "Tight", "T");
                            menu.Item("freew.layout-wrap-top-bottom", "Top and Bottom", "B");
                            menu.Item("freew.layout-wrap-behind", "Behind Text", "H");
                            menu.Item("freew.layout-wrap-front", "In Front of Text", "F");
                        });
                        group.Medium("freew.layout-bring-forward", "Bring Forward", RibbonCommandIconKind.BringForward);
                        group.Medium("freew.layout-send-backward", "Send Backward", RibbonCommandIconKind.SendBackward);
                        group.Medium("freew.layout-rotate", "Rotate", RibbonCommandIconKind.Rotate, menu: menu =>
                        {
                            menu.Item("freew.layout-rotate-right90", "Rotate Right 90°", "R");
                            menu.Item("freew.layout-rotate-left90", "Rotate Left 90°", "L");
                            menu.Item("freew.layout-flip-vertical", "Flip Vertical", "V");
                            menu.Item("freew.layout-flip-horizontal", "Flip Horizontal", "H");
                        });
                        group.Medium("freew.object-group", "Group", RibbonCommandIconKind.Group);
                        group.Medium("freew.object-ungroup", "Ungroup", RibbonCommandIconKind.Ungroup);
                    }),
                tab => tab.Group("arrange", "Arrange", null, 75, group =>
                    {
                        group.Dropdown("freew.layout-wrap", "Wrap Text", BuildWrapMenu("layout"));
                        group.Button("freew.layout-bring-forward", "Bring Forward");
                        group.Button("freew.layout-send-backward", "Send Backward");
                        group.Dropdown("freew.layout-rotate", "Rotate", BuildRotateMenu("layout"));
                        group.Button("freew.object-group", "Group");
                        group.Button("freew.object-ungroup", "Ungroup");
                    }),
                portableOrder: 2);

            topology.Section(
                "layout.data",
                tab => tab.Group("data", "Data", "D", 88, group =>
                    {
                        group.Medium("freew.text-to-table", "Text to Table", RibbonCommandIconKind.Table,
                            accent: RibbonCommandIconAccent.Green);
                        group.Medium("freew.table-to-text", "Table to Text", RibbonCommandIconKind.TextFunction);
                    }),
                tab => tab.Group("data", "Data", null, 95, group =>
                    {
                        group.Button("freew.text-to-table", "Text to Table");
                        group.Button("freew.table-to-text", "Table to Text");
                    }),
                portableOrder: 2);

            topology.Build();
        });

    internal static RibbonDefinitionBuilder AddDesignTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.Tab("design", "Design", "G", tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "design.formatting",
                tab => tab.Group("themes", "Document Formatting", "T", 100, group =>
                    {
                        group.ComboBox("freew.theme", "Themes", control => control with
                        {
                            Items = DocumentTheme.Catalog.Select(theme => theme.Name).ToArray(),
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme, RibbonCommandIconAccent.Theme),
                            Width = 140,
                        });
                        group.ComboBox("freew.style-set", "Style Sets", control => control with
                        {
                            Items = DocumentStyleSet.Catalog.Select(style => style.Name).ToArray(),
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font, RibbonCommandIconAccent.Theme),
                            Width = 140,
                        });
                        group.Icon("freew.reset-style-set", "Reset to Default Style Set", RibbonCommandIconKind.Refresh);
                        group.Medium("freew.theme-colors", "Colors", RibbonCommandIconKind.Color, "C",
                            menu: menu => BuildWpfThemeMenu("freew.theme-colors", menu),
                            accent: RibbonCommandIconAccent.Color);
                        group.Medium("freew.theme-fonts", "Fonts", RibbonCommandIconKind.Font, "F",
                            menu: menu => BuildWpfFontSetMenu("freew.theme-fonts", menu),
                            accent: RibbonCommandIconAccent.Theme);
                        group.Medium("freew.paragraph-spacing", "Paragraph Spacing", RibbonCommandIconKind.LineSpacing, "P",
                            menu: menu => BuildWpfParagraphSpacingMenu("freew.paragraph-spacing", menu),
                            accent: RibbonCommandIconAccent.Theme);
                        group.Medium("freew.theme-effects", "Effects", RibbonCommandIconKind.Effects, "E",
                            menu: menu => BuildWpfEffectsMenu("freew.theme-effects", menu),
                            accent: RibbonCommandIconAccent.Theme);
                    }),
                tab =>
                {
                    tab.Group("themes", "Themes", null, 110, group =>
                    group.Dropdown("freew.theme", "Themes", BuildAvaloniaThemeMenu()));

                    tab.Group("document-formatting", "Document Formatting", null, 100, group =>
                    {
                        group.Dropdown("freew.theme-colors", "Colors", BuildAvaloniaThemeColorsMenu());
                        group.Dropdown("freew.style-set", "Style Sets", BuildAvaloniaStyleSetsMenu());
                        group.Button("freew.reset-style-set", "Reset to Default Style Set", button => button with
                        {
                            PreferredLayout = RibbonCommandLayoutKind.Small,
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh),
                        });
                        group.Dropdown("freew.theme-fonts", "Fonts", BuildAvaloniaThemeFontsMenu());
                        group.Dropdown("freew.para-spacing", "Paragraph Spacing", BuildAvaloniaParagraphSpacingMenu());
                        group.Dropdown("freew.theme-effects", "Effects", FreeWContextMenuPlanner.BuildEffects());
                    });
                });

            topology.Section(
                "design.page-background",
                tab => tab.Group("page-background", FreeWRibbonText.PageBackgroundGroup.Label,
                        FreeWRibbonText.PageBackgroundGroup.KeyTip, 90, group =>
                    {
                        group.Medium("freew.watermark", FreeWRibbonText.WatermarkCommand.Label,
                            RibbonCommandIconKind.Watermark);
                        group.Medium("freew.page-color", FreeWRibbonText.PageColorCommand.Label,
                            RibbonCommandIconKind.Fill, accent: RibbonCommandIconAccent.Fill, dropdown: true);
                        group.Medium("freew.page-border", FreeWRibbonText.PageBordersCommand.Label,
                            RibbonCommandIconKind.Border, accent: RibbonCommandIconAccent.Border);
                    }),
                tab => tab.Group("page-background", FreeWRibbonText.PageBackgroundGroup.Label, null, 90, group =>
                    {
                        group.Dropdown("freew.watermark", FreeWRibbonText.WatermarkCommand.Label,
                            BuildAvaloniaWatermarkMenu());
                        group.Dropdown("freew.page-color", FreeWRibbonText.PageColorCommand.Label,
                            BuildAvaloniaPageColorMenu());
                        group.Button("freew.page-border", FreeWRibbonText.PageBordersCommand.Label);
                    }));

            topology.Build();
        });

    internal static RibbonDefinitionBuilder AddViewTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities) =>
        builder.Tab("view", "View", "W", tab =>
        {
            var topology = new FreeWRibbonTabTopology(tab, capabilities);

            topology.Section(
                "view.views",
                tab => tab.Group("views", "Views", "V", 100, group =>
                    {
                        group.Medium("freew.read-mode", "Read Mode", RibbonCommandIconKind.ReadMode, menu: menu =>
                        {
                            menu.Item("freew.read-mode-column-narrow", "Narrow Column Width", "N");
                            menu.Item("freew.read-mode-column-default", "Default Column Width", "D");
                            menu.Item("freew.read-mode-column-wide", "Wide Column Width", "W");
                            menu.Separator();
                            menu.Item("freew.read-mode-color-none", FreeWRibbonText.PageColorNoColorOption, "O");
                            menu.Item("freew.read-mode-color-sepia", "Sepia", "S");
                            menu.Item("freew.read-mode-color-inverse", "Inverse (Dark Mode)", "I");
                        });
                        group.MediumToggle("freew.print-layout", "Print Layout", RibbonCommandIconKind.PrintLayout);
                        group.MediumToggle("freew.web-layout", "Web Layout", RibbonCommandIconKind.WebLayout);
                        group.MediumToggle("freew.outline-view", "Outline", RibbonCommandIconKind.MultilevelList);
                        group.MediumToggle("freew.draft-view", "Draft", RibbonCommandIconKind.Draft);
                        group.MediumToggle("freew.paged-edit-view", "Page Edit", RibbonCommandIconKind.PrintLayout);
                    }),
                tab => tab.Group("views", "Views", null, 110, group =>
                    {
                        group.Dropdown("freew.read-mode", "Read Mode", BuildAvaloniaReadModeMenu(), dropdown => dropdown with
                        {
                            Icon = new RibbonCommandIcon(RibbonCommandIconKind.ReadMode),
                        });
                        group.Toggle("freew.print-layout", "Print Layout");
                        group.Toggle("freew.web-layout", "Web Layout");
                        group.Toggle("freew.outline-view", "Outline");
                        group.Toggle("freew.draft-view", "Draft");
                        group.Toggle("freew.paged-edit-view", "Page Edit");
                    }));

            topology.Section(
                "view.show",
                tab => tab.Group("show", "Show", "S", 90, group =>
                    {
                        group.MediumToggle("freew.ruler", "Ruler", RibbonCommandIconKind.Ruler);
                        group.MediumToggle("freew.nav-pane", "Navigation Pane", RibbonCommandIconKind.NavigationPane);
                        group.MediumToggle("freew.gridlines", "Gridlines", RibbonCommandIconKind.Grid);
                    }),
                tab => tab.Group("show", "Show", null, 100, group =>
                    {
                        group.Toggle("freew.ruler", "Ruler");
                        group.Toggle("freew.gridlines", "Gridlines");
                        group.Toggle("freew.nav-pane", "Navigation Pane");
                        group.Toggle("freew.reviewing-pane", "Reviewing Pane");
                        group.Toggle("freew.reveal-formatting", "Reveal Formatting");
                    }));

            topology.Section(
                "view.zoom",
                tab => tab.Group("zoom", "Zoom", "Z", 80, group =>
                    {
                        group.Large("freew.zoom-dialog", "Zoom", RibbonCommandIconKind.Zoom);
                        group.Medium("freew.zoom-100", "100%", RibbonCommandIconKind.Zoom);
                        group.Medium("freew.zoom-one-page", "One Page", RibbonCommandIconKind.OnePage);
                        group.Medium("freew.zoom-page-width", "Page Width", RibbonCommandIconKind.Scale);
                        group.MediumToggle("freew.zoom-multiple-pages", "Multiple Pages", RibbonCommandIconKind.PreviewResults);
                        group.MediumToggle("freew.zoom-side-to-side", "Side to Side", RibbonCommandIconKind.OnePage);
                    }),
                tab => tab.Group("zoom", "Zoom", null, 90, group =>
                    {
                        group.Button("freew.zoom-dialog", "Zoom");
                        group.Button("freew.zoom-100", "100%");
                        group.Button("freew.zoom-one-page", "One Page");
                        group.Button("freew.zoom-page-width", "Page Width");
                        group.Toggle("freew.zoom-multiple-pages", "Multiple Pages");
                        group.Toggle("freew.zoom-side-to-side", "Side to Side");
                    }));

            topology.Section(
                "view.window",
                tab => tab.Group("window", "Window", "N", 70, group =>
                    {
                        group.MediumToggle("freew.split-window", "Split", RibbonCommandIconKind.Scale);
                        group.Medium("freew.new-window", "New Window", RibbonCommandIconKind.Page);
                        group.Medium("freew.arrange-all", "Arrange All", RibbonCommandIconKind.Grid);
                    }),
                tab => tab.Group("window", "Window", null, 80, group =>
                    {
                        group.Button("freew.new-window", "New Window");
                        group.Button("freew.arrange-all", "Arrange All");
                        group.Toggle("freew.split-window", "Split");
                    }));

            topology.Build();
        });
    internal static RibbonDefinitionBuilder AddFileTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        if (!capabilities.IncludesSection(FreeWRibbonTopologySection.File))
            return builder;

        return builder.Tab("file", "File", "F", tab =>
            tab.Group("document", "Document", null, 100, group =>
            {
                group.Button("freew.backstage", "File...");
                group.Button("freew.new", "New");
                group.Button("freew.open", "Open");
                group.Button("freew.import-pdf-text", "Import PDF (text only)");
                group.Button("freew.save", "Save");
            }));
    }

    internal static RibbonDefinitionBuilder AddMailingsTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UsesPortableControls;

        return builder.Tab("mailings", "Mailings", "M", tab =>
        {
            tab.Group("create", "Create", "C", avalonia ? 110 : 130, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-envelopes", "Envelopes",
                    RibbonCommandIconKind.Envelope, wpfKeyTip: "E");
                AddProfiledButton(group, avalonia, "freew.merge-labels", "Labels",
                    RibbonCommandIconKind.MergeField, wpfKeyTip: "L");
            });
            tab.Group("merge-data", "Start Mail Merge", "D", avalonia ? 120 : 155, group =>
            {
                group.Dropdown("freew.start-mail-merge", "Start Mail Merge", BuildStartMailMergeMenu(), control => control with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Envelope),
                    KeyTip = "S",
                    PreferredLayout = RibbonCommandLayoutKind.Medium,
                });
                AddProfiledButton(group, avalonia, "freew.merge-data", "Select Recipients",
                    RibbonCommandIconKind.Recipients);
                AddProfiledButton(group, avalonia, "freew.merge-edit-recipients", "Edit Recipient List",
                    RibbonCommandIconKind.Recipients);
                AddProfiledButton(group, avalonia, "freew.merge-filter-sort", "Filter & Sort Recipients",
                    RibbonCommandIconKind.Recipients);
            });
            tab.Group("merge-write", "Write & Insert Fields", "W", avalonia ? 100 : 145, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-address-block", "Address Block",
                    RibbonCommandIconKind.Recipients, wpfKeyTip: "A");
                AddProfiledButton(group, avalonia, "freew.merge-greeting-line", "Greeting Line",
                    RibbonCommandIconKind.GreetingLine, wpfKeyTip: "G");
                AddProfiledButton(group, avalonia, "freew.merge-field", "Insert Merge Field",
                    RibbonCommandIconKind.MergeField, wpfKeyTip: "F");
                AddProfiledButton(group, avalonia, "freew.merge-match-fields", "Match Fields",
                    RibbonCommandIconKind.MergeField, wpfKeyTip: "H");
                group.Dropdown("freew.merge-rules", "Rules", BuildMergeRulesMenu(), control => control with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Field),
                    KeyTip = "U",
                    PreferredLayout = RibbonCommandLayoutKind.Medium,
                });
            });
            tab.Group("merge-preview", "Preview Results", "P", avalonia ? 80 : 120, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-preview", "Preview Results",
                    RibbonCommandIconKind.PreviewResults);
                AddProfiledButton(group, avalonia, "freew.merge-preview-first", "First Record",
                    RibbonCommandIconKind.Previous, wpfLayout: RibbonCommandLayoutKind.Small);
                AddProfiledButton(group, avalonia, "freew.merge-preview-previous", "Previous Record",
                    RibbonCommandIconKind.Previous, wpfLayout: RibbonCommandLayoutKind.Small,
                    avaloniaLabel: "\u25C0 Previous");
                AddProfiledButton(group, avalonia, "freew.merge-preview-next", "Next Record",
                    RibbonCommandIconKind.Next, wpfLayout: RibbonCommandLayoutKind.Small,
                    avaloniaLabel: "Next \u25B6");
                AddProfiledButton(group, avalonia, "freew.merge-preview-last", "Last Record",
                    RibbonCommandIconKind.Next, wpfLayout: RibbonCommandLayoutKind.Small);
                AddProfiledButton(group, avalonia, "freew.merge-find-recipient", "Find Recipient",
                    RibbonCommandIconKind.Search);
                AddProfiledButton(group, avalonia, "freew.merge-check-errors", "Check for Errors",
                    RibbonCommandIconKind.Warning, wpfAccent: RibbonCommandIconAccent.Warning);
            });
            tab.Group("merge-finish", "Finish", "F", avalonia ? 70 : 110, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-finish", "Finish & Merge",
                    RibbonCommandIconKind.FinishMerge);
                AddProfiledButton(group, avalonia, "freew.merge-email", "Send E-mail Messages",
                    RibbonCommandIconKind.Envelope, wpfKeyTip: "M");
            });
        });
    }

    internal static RibbonDefinitionBuilder AddHelpTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UsesPortableControls;

        return builder.Tab("help", "Help", "Y", tab =>
        {
            tab.Group("help", "Help", "H", 100, group =>
            {
                AddHelpButton(group, avalonia, "freew.help-online", "Help Online",
                    RibbonCommandIconKind.Help, "H");
                AddHelpButton(group, avalonia, "freew.feedback", "Feedback",
                    RibbonCommandIconKind.Feedback, "F");
                AddHelpButton(group, avalonia, "freew.copy-diagnostics", "Copy Diagnostics",
                    RibbonCommandIconKind.Info, "D");
                AddHelpButton(group, avalonia, "freew.test-crash-reporting", "Test Crash Reporting",
                    RibbonCommandIconKind.Info, "T");
            });
            tab.Group("product", "Product", "P", 90, group =>
            {
                AddHelpButton(group, avalonia, "freew.check-updates", "Check for Updates",
                    RibbonCommandIconKind.Refresh, "U");
                AddHelpButton(group, avalonia, "freew.about", "About FreeW",
                    RibbonCommandIconKind.Info, "A");
                AddHelpButton(group, avalonia, "freew.legal-notices", "Legal Notices",
                    RibbonCommandIconKind.Book, "L");
            });
        });
    }

    internal static RibbonDefinitionBuilder AddDeveloperTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UsesPortableControls;

        return builder.Tab("developer", "Developer", "D", tab =>
            tab.Group("controls", "Controls", "O", 100, group =>
            {
                AddSharedIconButton(group, "freew.cc-text", "Text Control", RibbonCommandIconKind.TextBox);
                AddSharedIconButton(group, "freew.cc-richtext", "Rich Text", RibbonCommandIconKind.QuickParts);
                AddSharedIconButton(group, "freew.cc-checkbox", "Check Box", RibbonCommandIconKind.CheckBox);
                AddSharedIconButton(group, "freew.cc-date", "Date Picker", RibbonCommandIconKind.Date);
                AddSharedIconButton(group, "freew.cc-dropdown", "Drop-Down List", RibbonCommandIconKind.List);
                AddSharedIconButton(group, "freew.cc-combo", "Combo Box", RibbonCommandIconKind.ChevronDown);
            }));
    }

    internal static RibbonDefinitionBuilder AddHeaderFooterDesignTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UsesPortableControls;

        return builder.ContextualTab("header-footer-design", "Design",
            new RibbonTabContext("header-footer", "Header & Footer Tools", RibbonContextColor.Purple), tab =>
            {
                tab.Group("hf-header-footer", "Header & Footer", "H", 120, group =>
                {
                    group.Dropdown("freew.hf-edit-header", "Edit Header", BuildHeaderMenu(), control => control with
                    {
                        Icon = WpfIcon(avalonia, RibbonCommandIconKind.Header),
                    });
                    group.Dropdown("freew.hf-edit-footer", "Edit Footer", BuildFooterMenu(), control => control with
                    {
                        Icon = WpfIcon(avalonia, RibbonCommandIconKind.Footer),
                    });
                });
                tab.Group("hf-insert", "Insert", "I", 110, group =>
                {
                    group.Dropdown("freew.hf-insert-page-number", "Page Number", BuildPageNumberMenu(), control => control with
                    {
                        Icon = WpfIcon(avalonia, RibbonCommandIconKind.PageNumber),
                    });
                    AddProfiledButton(group, avalonia, "freew.hf-insert-datetime", "Date && Time",
                        RibbonCommandIconKind.Date);
                    AddProfiledButton(group, avalonia, "freew.hf-insert-field", "Document Info",
                        RibbonCommandIconKind.Field);
                });
                tab.Group("hf-navigation", "Navigation", "N", 100, group =>
                {
                    AddProfiledButton(group, avalonia, "freew.hf-go-to-header", "Go to Header",
                        RibbonCommandIconKind.Header);
                    AddProfiledButton(group, avalonia, "freew.hf-go-to-footer", "Go to Footer",
                        RibbonCommandIconKind.Footer);
                });
                tab.Group("hf-options", "Options", "O", 90, group =>
                {
                    AddHeaderFooterOption(group, avalonia, "freew.hf-different-first-page",
                        "Different First Page", RibbonCommandIconKind.CoverPage);
                    AddHeaderFooterOption(group, avalonia, "freew.hf-different-odd-even",
                        "Different Odd && Even Pages", RibbonCommandIconKind.OnePage);
                });
                tab.Group("hf-position", "Position", "P", 80, group =>
                {
                    AddPositionCombo(group, "freew.hf-header-from-top", "Header from Top");
                    AddPositionCombo(group, "freew.hf-footer-from-bottom", "Footer from Bottom");
                });
                tab.Group("hf-close", "Close", "C", 70, group =>
                AddProfiledButton(group, avalonia, "freew.hf-close", "Close Header and Footer",
                    RibbonCommandIconKind.WindowClose));
            });
    }

    private static void BuildWpfThemeMenu(string commandId, RibbonMenuBuilder menu)
    {
        foreach (var theme in DocumentTheme.Catalog)
            menu.Item(commandId, theme.Name, theme.Name[0].ToString());
        menu.Separator();
        menu.Item("freew.customize-colors", "Customize Colors\u2026", "Z");
    }

    private static void BuildWpfFontSetMenu(string commandId, RibbonMenuBuilder menu)
    {
        foreach (var fontSet in DocumentFontSet.Catalog)
            menu.Item(commandId, fontSet.Name, fontSet.Name[0].ToString());
        menu.Separator();
        menu.Item("freew.customize-fonts", "Customize Fonts\u2026", "Z");
    }

    private static void BuildWpfParagraphSpacingMenu(string commandId, RibbonMenuBuilder menu)
    {
        foreach (var spacingSet in DocumentParagraphSpacingSet.Catalog)
            menu.Item(commandId, spacingSet.Name, spacingSet.Name[0].ToString());
        menu.Separator();
        menu.Item("freew.custom-paragraph-spacing", "Custom Paragraph Spacing\u2026", "U");
    }

    private static void BuildWpfEffectsMenu(string commandId, RibbonMenuBuilder menu)
    {
        foreach (var effectSet in DocumentEffectSet.Catalog)
            menu.Item(commandId, effectSet.Name, effectSet.Name[0].ToString());
    }

    private static RibbonMenu BuildAvaloniaMarginsMenu() => new(
    [
        new("Normal", new RibbonCommandId("freew.page-margins-normal")),
        new("Narrow", new RibbonCommandId("freew.page-margins-narrow")),
        new("Wide", new RibbonCommandId("freew.page-margins-wide")),
        RibbonMenuItem.Separator(),
        new("Custom Margins...", new RibbonCommandId("freew.custom-margins")),
    ]);

    private static RibbonMenu BuildAvaloniaPageSizeMenu() => new(
    [
        new("Letter", new RibbonCommandId("freew.page-size-letter")),
        new("A4", new RibbonCommandId("freew.page-size-a4")),
        RibbonMenuItem.Separator(),
        new("More Paper Sizes...", new RibbonCommandId("freew.more-paper-sizes")),
    ]);

    private static RibbonMenu BuildAvaloniaColumnsMenu() => new(
    [
        new("One", new RibbonCommandId("freew.columns-one")),
        new("Two", new RibbonCommandId("freew.columns-two")),
        new("Three", new RibbonCommandId("freew.columns-three")),
        new("Left", new RibbonCommandId("freew.columns-left")),
        new("Right", new RibbonCommandId("freew.columns-right")),
        RibbonMenuItem.Separator(),
        new("More Columns...", new RibbonCommandId("freew.columns-more")),
    ]);

    private static RibbonMenu BuildAvaloniaBreaksMenu() => new(
    [
        new("Page Break", new RibbonCommandId("freew.page-break")),
        new("Column Break", new RibbonCommandId("freew.column-break")),
        RibbonMenuItem.Separator(),
        new("Next Page", new RibbonCommandId("freew.section-break-next-page")),
        new("Continuous", new RibbonCommandId("freew.section-break-continuous")),
        new("Even Page", new RibbonCommandId("freew.section-break-even-page")),
        new("Odd Page", new RibbonCommandId("freew.section-break-odd-page")),
    ]);

    private static RibbonMenu BuildAvaloniaLineNumbersMenu() => new(
    [
        new("None", new RibbonCommandId("freew.line-numbers-none")),
        new("Continuous", new RibbonCommandId("freew.line-numbers-continuous")),
        new("Restart Each Page", new RibbonCommandId("freew.line-numbers-restart-page")),
        new("Restart Each Section", new RibbonCommandId("freew.line-numbers-restart-section")),
        RibbonMenuItem.Separator(),
        new("Line Numbering Options...", new RibbonCommandId("freew.line-numbers-options")),
    ]);

    private static RibbonMenu BuildAvaloniaHyphenationMenu() => new(
    [
        new("None", new RibbonCommandId("freew.hyphenation-none")),
        new("Automatic", new RibbonCommandId("freew.hyphenation-auto")),
        new("Manual", new RibbonCommandId("freew.hyphenation-manual")),
        RibbonMenuItem.Separator(),
        new("Hyphenation Options...", new RibbonCommandId("freew.hyphenation-options")),
    ]);

    private static RibbonMenu BuildAvaloniaThemeMenu() => new(
        DocumentTheme.Catalog
            .Select(theme => new RibbonMenuItem(theme.Name,
                new RibbonCommandId($"freew.theme.{theme.Name.ToLowerInvariant()}")))
            .ToArray());

    private static RibbonMenu BuildAvaloniaThemeColorsMenu() => new(
        DocumentTheme.Catalog
            .Select(theme => new RibbonMenuItem(theme.Name,
                new RibbonCommandId($"freew.theme-colors.{theme.Name.ToLowerInvariant()}")))
            .Concat([RibbonMenuItem.Separator(),
                new RibbonMenuItem("Customize Colors...", new RibbonCommandId("freew.customize-colors"))])
            .ToArray());

    private static RibbonMenu BuildAvaloniaStyleSetsMenu() => new(
        DocumentStyleSet.Catalog
            .Select(styleSet => new RibbonMenuItem(
                styleSet.Name,
                new RibbonCommandId(DesignRibbonWorkflow.StyleSetCommandId(styleSet.Name))))
            .ToArray());

    private static RibbonMenu BuildAvaloniaThemeFontsMenu() => new(
        DocumentFontSet.Catalog
            .Select(fontSet => new RibbonMenuItem(
                $"{fontSet.Name}  ({fontSet.HeadingFont} / {fontSet.BodyFont})",
                new RibbonCommandId($"freew.theme-fonts.{fontSet.Name.ToLowerInvariant()}")))
            .Concat([RibbonMenuItem.Separator(),
                new RibbonMenuItem("Customize Fonts...", new RibbonCommandId("freew.customize-fonts"))])
            .ToArray());

    private static RibbonMenu BuildAvaloniaParagraphSpacingMenu() => new(
        DocumentParagraphSpacingSet.Catalog
            .Select(spacingSet => new RibbonMenuItem(spacingSet.Name,
                new RibbonCommandId(DesignRibbonWorkflow.ParagraphSpacingCommandId(spacingSet.Name))))
            .Concat([
                RibbonMenuItem.Separator(),
                new RibbonMenuItem("Custom Paragraph Spacing...",
                    new RibbonCommandId("freew.custom-paragraph-spacing")),
            ])
            .ToArray());

    private static RibbonMenu BuildAvaloniaPageColorMenu() => new(
        FreeWRibbonDefinitionData.PageColors
            .Select(color => new RibbonMenuItem(color.Label, new RibbonCommandId(color.CommandId)))
            .ToArray());

    private static RibbonMenu BuildAvaloniaWatermarkMenu() => new(
    [
        new("CONFIDENTIAL", new RibbonCommandId("freew.watermark.confidential")),
        new("DO NOT COPY", new RibbonCommandId("freew.watermark.do-not-copy")),
        new("DRAFT", new RibbonCommandId("freew.watermark.draft")),
        new("URGENT", new RibbonCommandId("freew.watermark.urgent")),
        RibbonMenuItem.Separator(),
        new("Custom Watermark\u2026", new RibbonCommandId("freew.watermark.custom")),
        new("Remove Watermark", new RibbonCommandId("freew.watermark.none")),
    ]);

    private static RibbonMenu BuildAvaloniaReadModeMenu() => new(
    [
        new("Narrow Column Width", new RibbonCommandId("freew.read-mode-column-narrow")),
        new("Default Column Width", new RibbonCommandId("freew.read-mode-column-default")),
        new("Wide Column Width", new RibbonCommandId("freew.read-mode-column-wide")),
        RibbonMenuItem.Separator(),
        new("No Color", new RibbonCommandId("freew.read-mode-color-none")),
        new("Sepia", new RibbonCommandId("freew.read-mode-color-sepia")),
        new("Inverse (Dark Mode)", new RibbonCommandId("freew.read-mode-color-inverse")),
    ]);

    private static void AddProfiledButton(
        RibbonGroupBuilder group,
        bool avalonia,
        string commandId,
        string label,
        RibbonCommandIconKind wpfIcon,
        string? wpfKeyTip = null,
        RibbonCommandLayoutKind wpfLayout = RibbonCommandLayoutKind.Medium,
        string? avaloniaLabel = null,
        RibbonCommandIconAccent wpfAccent = RibbonCommandIconAccent.None)
    {
        group.Button(commandId, avalonia ? avaloniaLabel ?? label : label, control => control with
        {
            Icon = WpfIcon(avalonia, wpfIcon, wpfAccent),
            KeyTip = wpfKeyTip,
            PreferredLayout = avalonia ? RibbonCommandLayoutKind.Medium : wpfLayout,
        });
    }

    private static void AddHelpButton(
        RibbonGroupBuilder group,
        bool avalonia,
        string commandId,
        string label,
        RibbonCommandIconKind icon,
        string keyTip)
    {
        group.Button(commandId, label, control => control with
        {
            Icon = new RibbonCommandIcon(icon, RibbonCommandIconAccent.Help),
            KeyTip = keyTip,
            PreferredLayout = avalonia ? RibbonCommandLayoutKind.Medium : RibbonCommandLayoutKind.Large,
        });
    }

    private static void AddSharedIconButton(
        RibbonGroupBuilder group,
        string commandId,
        string label,
        RibbonCommandIconKind icon)
    {
        group.Button(commandId, label, control => control with
        {
            Icon = new RibbonCommandIcon(icon),
            PreferredLayout = RibbonCommandLayoutKind.Medium,
        });
    }

    private static void AddHeaderFooterOption(
        RibbonGroupBuilder group,
        bool avalonia,
        string commandId,
        string label,
        RibbonCommandIconKind wpfIcon)
    {
        if (avalonia)
        {
            group.Toggle(commandId, label);
            return;
        }

        AddProfiledButton(group, avalonia: false, commandId, label, wpfIcon);
    }

    private static void AddPositionCombo(RibbonGroupBuilder group, string commandId, string label)
    {
        group.ComboBox(commandId, label, control => control with
        {
            Items = ["0", "18", "36", "54", "72"],
            Width = 80,
        });
    }

    private static RibbonMenu BuildStartMailMergeMenu() => new(
    [
        Item("Letters", "freew.start-mail-merge-letters", "L"),
        Item("Directory", "freew.start-mail-merge-directory", "D"),
        RibbonMenuItem.Separator(),
        Item("Normal Word Document", "freew.start-mail-merge-normal", "N"),
    ]);

    private static RibbonMenu BuildMergeRulesMenu() => new(
    [
        Item("If\u2026Then\u2026Else", "freew.merge-rule-if", "I"),
        RibbonMenuItem.Separator(),
        Item("Skip Record If", "freew.merge-rule-skip-record-if", "K"),
        Item("Next Record If", "freew.merge-rule-next-record-if", "X"),
        RibbonMenuItem.Separator(),
        Item("Next Record", "freew.merge-next-record", "N"),
        Item("Merge Record #", "freew.merge-record-number", "R"),
        Item("Merge Sequence #", "freew.merge-sequence-number", "Q"),
        RibbonMenuItem.Separator(),
        Item("Fill-in", "freew.merge-rule-fill-in", "L"),
        Item("Ask", "freew.merge-rule-ask", "A"),
        RibbonMenuItem.Separator(),
        Item("Set Bookmark", "freew.merge-rule-set", "B"),
        Item("Ref Bookmark", "freew.merge-rule-ref", "E"),
    ]);

    private static RibbonMenu BuildHeaderMenu() => new(
    [
        Item("Default Header", "freew.hf-edit-header", "H"),
        Item("First-Page Header", "freew.hf-edit-first-header", "F"),
        Item("Even-Page Header", "freew.hf-edit-even-header", "E"),
    ]);

    private static RibbonMenu BuildFooterMenu() => new(
    [
        Item("Default Footer", "freew.hf-edit-footer", "O"),
        Item("First-Page Footer", "freew.hf-edit-first-footer", "I"),
        Item("Even-Page Footer", "freew.hf-edit-even-footer", "V"),
    ]);

    private static RibbonMenu BuildPageNumberMenu() => new(
    [
        Item("In Header", "freew.hf-insert-page-number", "H"),
        Item("In Footer", "freew.hf-insert-page-number-footer", "F"),
    ]);

    private static RibbonMenuItem Item(
        string header,
        string commandId,
        string wpfKeyTip) =>
        new(header, new RibbonCommandId(commandId), wpfKeyTip);

    private static RibbonCommandIcon? WpfIcon(
        bool avalonia,
        RibbonCommandIconKind kind,
        RibbonCommandIconAccent accent = RibbonCommandIconAccent.None) =>
        avalonia ? null : new RibbonCommandIcon(kind, accent);

}
