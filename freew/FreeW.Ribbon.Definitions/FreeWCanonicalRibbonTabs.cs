using Free.Shared.Ribbon;

namespace FreeW.Ribbon.Definitions;

/// <summary>
/// Canonical FreeW tab topology shared by both renderers. Capability checks select only the
/// presentation shape that each host already supports; command ownership and ordering live here.
/// </summary>
internal static class FreeWCanonicalRibbonTabs
{
    internal static RibbonDefinitionBuilder AddMailingsTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UseAvaloniaBackedSurface;

        return builder.Tab("mailings", "Mailings", "M", tab =>
        {
            tab.Group("create", "Create", WpfOnly(avalonia, "C"), avalonia ? 110 : 130, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-envelopes", "Envelopes",
                    RibbonCommandIconKind.Envelope, wpfKeyTip: "E");
                AddProfiledButton(group, avalonia, "freew.merge-labels", "Labels",
                    RibbonCommandIconKind.MergeField, wpfKeyTip: "L");
            });
            tab.Group("merge-data", "Start Mail Merge", WpfOnly(avalonia, "D"), avalonia ? 120 : 155, group =>
            {
                group.Dropdown("freew.start-mail-merge", "Start Mail Merge", BuildStartMailMergeMenu(avalonia), control => control with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Envelope),
                    KeyTip = WpfOnly(avalonia, "S"),
                    PreferredLayout = RibbonCommandLayoutKind.Medium,
                });
                AddProfiledButton(group, avalonia, "freew.merge-data", "Select Recipients",
                    RibbonCommandIconKind.Recipients);
                AddProfiledButton(group, avalonia, "freew.merge-edit-recipients", "Edit Recipient List",
                    RibbonCommandIconKind.Recipients);
                AddProfiledButton(group, avalonia, "freew.merge-filter-sort", "Filter & Sort Recipients",
                    RibbonCommandIconKind.Recipients);
            });
            tab.Group("merge-write", "Write & Insert Fields", WpfOnly(avalonia, "W"), avalonia ? 100 : 145, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-address-block", "Address Block",
                    RibbonCommandIconKind.Recipients, wpfKeyTip: "A");
                AddProfiledButton(group, avalonia, "freew.merge-greeting-line", "Greeting Line",
                    RibbonCommandIconKind.GreetingLine, wpfKeyTip: "G");
                AddProfiledButton(group, avalonia, "freew.merge-field", "Insert Merge Field",
                    RibbonCommandIconKind.MergeField, wpfKeyTip: "F");
                AddProfiledButton(group, avalonia, "freew.merge-match-fields", "Match Fields",
                    RibbonCommandIconKind.MergeField, wpfKeyTip: "H");
                group.Dropdown("freew.merge-rules", "Rules", BuildMergeRulesMenu(avalonia), control => control with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Field),
                    KeyTip = WpfOnly(avalonia, "U"),
                    PreferredLayout = RibbonCommandLayoutKind.Medium,
                });
            });
            tab.Group("merge-preview", "Preview Results", WpfOnly(avalonia, "P"), avalonia ? 80 : 120, group =>
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
            tab.Group("merge-finish", "Finish", WpfOnly(avalonia, "F"), avalonia ? 70 : 110, group =>
            {
                AddProfiledButton(group, avalonia, "freew.merge-finish", "Finish & Merge",
                    RibbonCommandIconKind.FinishMerge);
                AddProfiledButton(group, avalonia, "freew.merge-email", "Send E-mail Messages",
                    RibbonCommandIconKind.Envelope, wpfKeyTip: "E");
            });
        });
    }

    internal static RibbonDefinitionBuilder AddHelpTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UseAvaloniaBackedSurface;

        return builder.Tab("help", "Help", "Y", tab =>
        {
            tab.Group("help", "Help", WpfOnly(avalonia, "H"), 100, group =>
            {
                AddHelpButton(group, avalonia, "freew.help-online", "Help Online",
                    RibbonCommandIconKind.Help, "H", "H");
                AddHelpButton(group, avalonia, "freew.feedback", "Feedback",
                    RibbonCommandIconKind.Feedback, "F", "F");
                AddHelpButton(group, avalonia, "freew.copy-diagnostics", "Copy Diagnostics",
                    RibbonCommandIconKind.Info, "D", "D");
            });
            tab.Group("product", "Product", WpfOnly(avalonia, "P"), 90, group =>
            {
                AddHelpButton(group, avalonia, "freew.check-updates", "Check for Updates",
                    RibbonCommandIconKind.Refresh, "U", "U");
                AddHelpButton(group, avalonia, "freew.about", "About FreeW",
                    RibbonCommandIconKind.Info, "A", avaloniaKeyTip: null);
                AddHelpButton(group, avalonia, "freew.legal-notices", "Legal Notices",
                    RibbonCommandIconKind.Book, "L", avaloniaKeyTip: null);
            });
        });
    }

    internal static RibbonDefinitionBuilder AddDeveloperTab(
        this RibbonDefinitionBuilder builder,
        FreeWRibbonCapabilities capabilities)
    {
        var avalonia = capabilities.UseAvaloniaBackedSurface;

        return builder.Tab("developer", "Developer", "D", tab =>
            tab.Group("controls", "Controls", WpfOnly(avalonia, "O"), 100, group =>
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
        var avalonia = capabilities.UseAvaloniaBackedSurface;

        return builder.ContextualTab("header-footer-design", "Design",
            new RibbonTabContext("header-footer", "Header & Footer Tools", RibbonContextColor.Purple), tab =>
            {
                tab.Group("hf-header-footer", "Header & Footer", WpfOnly(avalonia, "H"), 120, group =>
                {
                    group.Dropdown("freew.hf-edit-header", "Edit Header", BuildHeaderMenu(avalonia), control => control with
                    {
                        Icon = WpfIcon(avalonia, RibbonCommandIconKind.Header),
                    });
                    group.Dropdown("freew.hf-edit-footer", "Edit Footer", BuildFooterMenu(avalonia), control => control with
                    {
                        Icon = WpfIcon(avalonia, RibbonCommandIconKind.Footer),
                    });
                });
                tab.Group("hf-insert", "Insert", WpfOnly(avalonia, "I"), 110, group =>
                {
                    group.Dropdown("freew.hf-insert-page-number", "Page Number", BuildPageNumberMenu(avalonia), control => control with
                    {
                        Icon = WpfIcon(avalonia, RibbonCommandIconKind.PageNumber),
                    });
                    AddProfiledButton(group, avalonia, "freew.hf-insert-datetime", "Date && Time",
                        RibbonCommandIconKind.Date);
                    AddProfiledButton(group, avalonia, "freew.hf-insert-field", "Document Info",
                        RibbonCommandIconKind.Field);
                });
                tab.Group("hf-navigation", "Navigation", WpfOnly(avalonia, "N"), 100, group =>
                {
                    AddProfiledButton(group, avalonia, "freew.hf-go-to-header", "Go to Header",
                        RibbonCommandIconKind.Header);
                    AddProfiledButton(group, avalonia, "freew.hf-go-to-footer", "Go to Footer",
                        RibbonCommandIconKind.Footer);
                });
                tab.Group("hf-options", "Options", WpfOnly(avalonia, "O"), 90, group =>
                {
                    AddHeaderFooterOption(group, avalonia, "freew.hf-different-first-page",
                        "Different First Page", RibbonCommandIconKind.CoverPage);
                    AddHeaderFooterOption(group, avalonia, "freew.hf-different-odd-even",
                        "Different Odd && Even Pages", RibbonCommandIconKind.OnePage);
                });
                tab.Group("hf-position", "Position", WpfOnly(avalonia, "P"), 80, group =>
                {
                    AddPositionCombo(group, "freew.hf-header-from-top", "Header from Top");
                    AddPositionCombo(group, "freew.hf-footer-from-bottom", "Footer from Bottom");
                });
                tab.Group("hf-close", "Close", WpfOnly(avalonia, "C"), 70, group =>
                    AddProfiledButton(group, avalonia, "freew.hf-close", "Close Header and Footer",
                        RibbonCommandIconKind.WindowClose));
            });
    }

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
            KeyTip = WpfOnly(avalonia, wpfKeyTip),
            PreferredLayout = avalonia ? RibbonCommandLayoutKind.Medium : wpfLayout,
        });
    }

    private static void AddHelpButton(
        RibbonGroupBuilder group,
        bool avalonia,
        string commandId,
        string label,
        RibbonCommandIconKind icon,
        string wpfKeyTip,
        string? avaloniaKeyTip)
    {
        group.Button(commandId, label, control => control with
        {
            Icon = new RibbonCommandIcon(icon, RibbonCommandIconAccent.Help),
            KeyTip = avalonia ? avaloniaKeyTip : wpfKeyTip,
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

    private static RibbonMenu BuildStartMailMergeMenu(bool avalonia) => new(
    [
        Item(avalonia, "Letters", "freew.start-mail-merge-letters", "L"),
        Item(avalonia, "Directory", "freew.start-mail-merge-directory", "D"),
        RibbonMenuItem.Separator(),
        Item(avalonia, "Normal Word Document", "freew.start-mail-merge-normal", "N"),
    ]);

    private static RibbonMenu BuildMergeRulesMenu(bool avalonia) => new(
    [
        Item(avalonia, "If\u2026Then\u2026Else", "freew.merge-rule-if", "I"),
        RibbonMenuItem.Separator(),
        Item(avalonia, "Skip Record If", "freew.merge-rule-skip-record-if", "K"),
        Item(avalonia, "Next Record If", "freew.merge-rule-next-record-if", "X"),
        RibbonMenuItem.Separator(),
        Item(avalonia, "Next Record", "freew.merge-next-record", "N"),
        Item(avalonia, "Merge Record #", "freew.merge-record-number", "R"),
        Item(avalonia, "Merge Sequence #", "freew.merge-sequence-number", "Q"),
        RibbonMenuItem.Separator(),
        Item(avalonia, "Fill-in", "freew.merge-rule-fill-in", "L"),
        Item(avalonia, "Ask", "freew.merge-rule-ask", "A"),
        RibbonMenuItem.Separator(),
        Item(avalonia, "Set Bookmark", "freew.merge-rule-set", "B"),
        Item(avalonia, "Ref Bookmark", "freew.merge-rule-ref", "E"),
    ]);

    private static RibbonMenu BuildHeaderMenu(bool avalonia) => new(
    [
        Item(avalonia, "Default Header", "freew.hf-edit-header", "H"),
        Item(avalonia, "First-Page Header", "freew.hf-edit-first-header", "F"),
        Item(avalonia, "Even-Page Header", "freew.hf-edit-even-header", "E"),
    ]);

    private static RibbonMenu BuildFooterMenu(bool avalonia) => new(
    [
        Item(avalonia, "Default Footer", "freew.hf-edit-footer", "O"),
        Item(avalonia, "First-Page Footer", "freew.hf-edit-first-footer", "I"),
        Item(avalonia, "Even-Page Footer", "freew.hf-edit-even-footer", "V"),
    ]);

    private static RibbonMenu BuildPageNumberMenu(bool avalonia) => new(
    [
        Item(avalonia, "In Header", "freew.hf-insert-page-number", "H"),
        Item(avalonia, "In Footer", "freew.hf-insert-page-number-footer", "F"),
    ]);

    private static RibbonMenuItem Item(
        bool avalonia,
        string header,
        string commandId,
        string wpfKeyTip) =>
        new(header, new RibbonCommandId(commandId), WpfOnly(avalonia, wpfKeyTip));

    private static RibbonCommandIcon? WpfIcon(
        bool avalonia,
        RibbonCommandIconKind kind,
        RibbonCommandIconAccent accent = RibbonCommandIconAccent.None) =>
        avalonia ? null : new RibbonCommandIcon(kind, accent);

    private static string? WpfOnly(bool avalonia, string? value) => avalonia ? null : value;
}
