using System.Globalization;
using Free.Shared.Ribbon;
using FreeW.App.Localization;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class FreeWRibbonDefinitionProfileTests
{
    private static readonly string[] WpfOnlyTabIds =
    [
        "developer",
        "header-footer-design",
        "help",
    ];

    private static readonly string[] AvaloniaOnlyTabIds =
    [
        "file",
    ];

    private static readonly DivergenceRule[] WpfOnlyCommandRules =
    [
        new("WPF-only tabs", entry => WpfOnlyTabIds.Contains(entry.TabId, StringComparer.Ordinal)),
        new("WPF gallery injection placeholders", entry => entry.GroupId is
            "chart-colors" or
            "chart-quick-layout" or
            "chart-style" or
            "picture-adjust" or
            "picture-size" or
            "picture-styles" or
            "smartart-colors" or
            "smartart-create-graphic" or
            "smartart-edit" or
            "smartart-layouts"),
        new("WPF desktop dialog and custom surfaces", entry => entry.CommandId.StartsWith("freew.custom", StringComparison.Ordinal) ||
            entry.CommandId.Contains("dialog", StringComparison.Ordinal) ||
            entry.CommandId.Contains("options", StringComparison.Ordinal) ||
            entry.CommandId.Contains("organizer", StringComparison.Ordinal) ||
            entry.CommandId.Contains("manager", StringComparison.Ordinal)),
        new("WPF richer Word surface not yet exposed by Avalonia", entry => entry.TabId is
            "home" or
            "insert" or
            "design" or
            "layout" or
            "references" or
            "mailings" or
            "review" or
            "view" or
            "picture-format" or
            "drawing-format" or
            "chart-design" or
            "chart-format" or
            "smartart-design" or
            "table-design" or
            "table-layout"),
    ];

    private static readonly DivergenceRule[] AvaloniaOnlyCommandRules =
    [
        new("Avalonia-only File tab shell commands", entry => entry.TabId == "file"),
        new("Avalonia portable command registry aliases", entry => entry.CommandId is
            "freew.find-replace-dialog" or
            "freew.insert-bookmark" or
            "freew.insert-hyperlink" or
            "freew.insert-table" or
            "freew.shape" or
            "freew.show-hide-para" or
            "freew.text-box"),
        new("Avalonia menu-backed portable palettes", entry => entry.CommandId.StartsWith("freew.font-color.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.page-color.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.para-spacing.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.quick-parts.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.symbol.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.table-borders.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.theme.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.theme-colors.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.theme-fonts.", StringComparison.Ordinal) ||
            entry.CommandId.StartsWith("freew.watermark.", StringComparison.Ordinal)),
        new("Avalonia backed subset commands with different ids from WPF", entry => entry.TabId is
            "home" or
            "insert" or
            "design" or
            "layout" or
            "references" or
            "mailings" or
            "review" or
            "view" or
            "picture-format" or
            "drawing-format" or
            "chart-design" or
            "chart-format" or
            "smartart-design" or
            "table-design" or
            "table-layout"),
    ];

    [Fact]
    public void Shared_factory_builds_wpf_and_avalonia_profiles()
    {
        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf);
        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);

        wpf.VisibleTabs.Select(tab => tab.Id)
            .Should()
            .Equal("home", "insert", "design", "layout", "references", "mailings", "review", "view", "help", "developer");
        avalonia.Tabs.Select(tab => tab.Id)
            .Should()
            .Contain(new[] { "file", "home", "insert", "design", "layout", "references", "mailings", "review", "view" });
    }

    [Fact]
    public void Profile_tab_ids_match_except_named_capability_deltas()
    {
        var wpfTabIds = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf).Tabs.Select(tab => tab.Id).ToArray();
        var avaloniaTabIds = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).Tabs.Select(tab => tab.Id).ToArray();

        wpfTabIds.Except(avaloniaTabIds, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(WpfOnlyTabIds);
        avaloniaTabIds.Except(wpfTabIds, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(AvaloniaOnlyTabIds);
    }

    [Fact]
    public void Profile_context_keys_match_for_shared_contextual_tabs()
    {
        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf).ContextualTabs
            .ToDictionary(tab => tab.Id, tab => tab.Context!.ActivationKey, StringComparer.Ordinal);
        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).ContextualTabs
            .ToDictionary(tab => tab.Id, tab => tab.Context!.ActivationKey, StringComparer.Ordinal);

        foreach (var tabId in wpf.Keys.Intersect(avalonia.Keys, StringComparer.Ordinal))
            avalonia[tabId].Should().Be(wpf[tabId], $"{tabId} uses the same activation key across profiles");
    }

    [Fact]
    public void Profile_command_id_differences_are_named_capability_deltas()
    {
        var wpf = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf)).ToArray();
        var avalonia = CommandEntries(FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia)).ToArray();
        var wpfIds = wpf.Select(entry => entry.CommandId).ToHashSet(StringComparer.Ordinal);
        var avaloniaIds = avalonia.Select(entry => entry.CommandId).ToHashSet(StringComparer.Ordinal);

        var unexpectedWpfOnly = wpf
            .Where(entry => !avaloniaIds.Contains(entry.CommandId))
            .Where(entry => !IsAllowed(entry, WpfOnlyCommandRules))
            .Select(entry => entry.Display)
            .ToArray();
        var unexpectedAvaloniaOnly = avalonia
            .Where(entry => !wpfIds.Contains(entry.CommandId))
            .Where(entry => !IsAllowed(entry, AvaloniaOnlyCommandRules))
            .Select(entry => entry.Display)
            .ToArray();

        unexpectedWpfOnly.Should().BeEmpty("every WPF-only ribbon id must have an explicit capability rule");
        unexpectedAvaloniaOnly.Should().BeEmpty("every Avalonia-only ribbon id must have an explicit capability rule");
    }

    [Fact]
    public void Home_clipboard_text_is_resource_backed_for_wpf_and_avalonia_profiles()
    {
        var neutral = WithUiCulture("en-US", () => new[]
        {
            ClipboardSurface(FreeWRibbonCapabilities.Wpf),
            ClipboardSurface(FreeWRibbonCapabilities.Avalonia),
        });

        foreach (var surface in neutral)
        {
            surface.HomeHeader.Should().Be("Home");
            surface.HomeKeyTip.Should().Be("H");
            surface.ClipboardHeader.Should().Be("Clipboard");
            surface.ClipboardKeyTip.Should().Be("C");
            surface.PasteLabel.Should().Be("Paste");
            surface.PasteKeyTip.Should().Be("V");
            surface.CutLabel.Should().Be("Cut");
            surface.CutKeyTip.Should().Be("X");
            surface.CopyLabel.Should().Be("Copy");
            surface.CopyKeyTip.Should().Be("C");
        }

        var pseudo = WithUiCulture(Loc.PseudoLocalizationCultureName, () => new[]
        {
            ClipboardSurface(FreeWRibbonCapabilities.Wpf),
            ClipboardSurface(FreeWRibbonCapabilities.Avalonia),
        });

        foreach (var surface in pseudo)
        {
            surface.HomeHeader.Should().Be("[[HHoommee]]");
            surface.HomeKeyTip.Should().Be("H");
            surface.ClipboardHeader.Should().Be("[[CClliippbbooaarrdd]]");
            surface.ClipboardKeyTip.Should().Be("C");
            surface.PasteLabel.Should().Be("[[PPaassttee]]");
            surface.PasteKeyTip.Should().Be("V");
            surface.CutLabel.Should().Be("[[CCuutt]]");
            surface.CutKeyTip.Should().Be("X");
            surface.CopyLabel.Should().Be("[[CCooppyy]]");
            surface.CopyKeyTip.Should().Be("C");
        }
    }

    [Fact]
    public void Home_clipboard_profile_sources_use_resource_descriptors()
    {
        var wpfSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avaloniaSource = ReadRepositoryFile("freew", "FreeW.Ribbon.Definitions", "FreeWAvaloniaRibbonDefinition.cs");

        wpfSource.Should().NotContain(".Tab(\"home\", \"Home\"");
        wpfSource.Should().NotContain("tab.Group(\"clipboard\", \"Clipboard\"");
        wpfSource.Should().NotContain("g.Large(\"freew.paste\", \"Paste\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.cut\", \"Cut\"");
        wpfSource.Should().NotContain("g.Medium(\"freew.copy\", \"Copy\"");
        wpfSource.Should().Contain("FreeWRibbonText.HomeTab");
        wpfSource.Should().Contain("FreeWRibbonText.ClipboardGroup");
        wpfSource.Should().Contain("FreeWRibbonText.PasteCommand");
        wpfSource.Should().Contain("FreeWRibbonText.CutCommand");
        wpfSource.Should().Contain("FreeWRibbonText.CopyCommand");

        avaloniaSource.Should().NotContain(".Tab(\"home\", \"Home\"");
        avaloniaSource.Should().NotContain("tab.Group(\"clipboard\", \"Clipboard\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.cut\",   \"Cut\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.copy\",  \"Copy\"");
        avaloniaSource.Should().NotContain("g.Button(\"freew.paste\", \"Paste\"");
        avaloniaSource.Should().Contain("FreeWRibbonText.HomeTab");
        avaloniaSource.Should().Contain("FreeWRibbonText.ClipboardGroup");
        avaloniaSource.Should().Contain("FreeWRibbonText.PasteCommand");
        avaloniaSource.Should().Contain("FreeWRibbonText.CutCommand");
        avaloniaSource.Should().Contain("FreeWRibbonText.CopyCommand");
    }

    private static bool IsAllowed(CommandEntry entry, IReadOnlyList<DivergenceRule> rules) =>
        rules.Any(rule => rule.IsAllowed(entry));

    private static T WithUiCulture<T>(string cultureName, Func<T> action)
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static ClipboardRibbonSurface ClipboardSurface(FreeWRibbonCapabilities capabilities)
    {
        var definition = FreeWRibbon.Build(capabilities);
        var home = definition.FindTab("home");
        home.Should().NotBeNull();
        var clipboard = home!.FindGroup("clipboard");
        clipboard.Should().NotBeNull();

        var paste = RequiredControl(clipboard!, "freew.paste");
        var cut = RequiredControl(clipboard!, "freew.cut");
        var copy = RequiredControl(clipboard!, "freew.copy");

        return new ClipboardRibbonSurface(
            home.Header,
            home.KeyTip,
            clipboard.Header,
            clipboard.KeyTip,
            paste.Label,
            paste.KeyTip,
            cut.Label,
            cut.KeyTip,
            copy.Label,
            copy.KeyTip);
    }

    private static RibbonControl RequiredControl(RibbonGroup group, string commandId) =>
        group.Controls.Single(control => control.CommandId.Value == commandId);

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(
            $"Could not locate {Path.Combine(relativeParts)} from {AppContext.BaseDirectory}.");
    }

    private static IEnumerable<CommandEntry> CommandEntries(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                foreach (var control in group.Controls)
                {
                    foreach (var commandId in CommandIds(control))
                        yield return new CommandEntry(tab.Id, group.Id, commandId);
                }
            }
        }
    }

    private static IEnumerable<string> CommandIds(RibbonControl control)
    {
        var commandId = control switch
        {
            RibbonButton b => b.CommandId.Value,
            RibbonToggleButton t => t.CommandId.Value,
            RibbonComboBox c => c.CommandId.Value,
            RibbonCheckBox cb => cb.CommandId.Value,
            RibbonSplitButton sb => sb.CommandId.Value,
            RibbonDropdown d => d.CommandId.Value,
            RibbonGallery g => g.CommandId.Value,
            _ => null,
        };

        if (commandId is not null)
            yield return commandId;

        var menu = control switch
        {
            RibbonSplitButton sb => sb.Menu,
            RibbonDropdown d => d.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in CommandIds(menu.Items))
            yield return item;
    }

    private static IEnumerable<string> CommandIds(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } commandId)
                yield return commandId.Value;

            foreach (var childId in CommandIds(item.Children))
                yield return childId;
        }
    }

    private sealed record DivergenceRule(string Reason, Func<CommandEntry, bool> IsAllowed);

    private sealed record ClipboardRibbonSurface(
        string HomeHeader,
        string? HomeKeyTip,
        string ClipboardHeader,
        string? ClipboardKeyTip,
        string PasteLabel,
        string? PasteKeyTip,
        string CutLabel,
        string? CutKeyTip,
        string CopyLabel,
        string? CopyKeyTip);

    private sealed record CommandEntry(string TabId, string GroupId, string CommandId)
    {
        public string Display => $"{TabId}/{GroupId}/{CommandId}";
    }
}
