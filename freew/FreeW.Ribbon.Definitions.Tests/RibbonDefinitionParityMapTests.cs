using Free.Shared.Ribbon;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class FreeWRibbonCanonicalOwnershipTests
{
    [Theory]
    [MemberData(nameof(CanonicalTabProfiles))]
    public void Canonical_tab_profiles_preserve_existing_structure(
        string tabId,
        string expectedWpfHash,
        string expectedAvaloniaHash)
    {
        var wpf = FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf);
        var avalonia = FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);

        Hash(TabSignature(wpf.FindTab(tabId)!)).Should().Be(expectedWpfHash);
        Hash(TabSignature(avalonia.FindTab(tabId)!)).Should().Be(expectedAvaloniaHash);
    }

    [Fact]
    public void Canonical_tab_profile_evidence_is_generated_and_complete()
    {
        using var document = ReadCanonicalEvidence();
        var root = document.RootElement;

        root.GetProperty("schema").GetString().Should().Be("freew.canonical-ribbon-profiles.v1");
        root.GetProperty("generatedBy").GetString().Should().Be(
            "freew/FreeW.Ribbon.Definitions.Tests/Generate-FreeWCanonicalRibbonEvidence.ps1");
        root.GetProperty("topologySource").GetString().Should().Contain(
            "FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf/Avalonia)");
        root.GetProperty("tabs").EnumerateArray()
            .Select(tab => tab.GetProperty("tabId").GetString())
            .Should().Equal(
                "chart-design", "chart-format", "design", "developer", "drawing-format", "header-footer-design",
                "help", "home", "insert", "layout", "mailings", "picture-format", "references", "review",
                "smartart-design", "table-design", "table-layout", "view");
    }

    public static IEnumerable<object[]> CanonicalTabProfiles()
    {
        using var document = ReadCanonicalEvidence();
        foreach (var tab in document.RootElement.GetProperty("tabs").EnumerateArray())
        {
            yield return
            [
                tab.GetProperty("tabId").GetString()!,
                tab.GetProperty("wpfSha256").GetString()!,
                tab.GetProperty("avaloniaSha256").GetString()!,
            ];
        }
    }

    [Fact]
    public void Contextual_tab_profile_order_is_preserved()
    {
        FreeWRibbon.Build(FreeWRibbonCapabilities.Wpf).Tabs
            .Where(tab => tab.IsContextual)
            .Select(tab => tab.Id)
            .Should().Equal(
                "drawing-format",
                "picture-format",
                "chart-design",
                "chart-format",
                "smartart-design",
                "table-design",
                "table-layout",
                "header-footer-design");

        FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia).Tabs
            .Where(tab => tab.IsContextual)
            .Select(tab => tab.Id)
            .Should().Equal(
                "table-design",
                "table-layout",
                "header-footer-design",
                "picture-format",
                "drawing-format",
                "chart-design",
                "chart-format",
                "smartart-design");
    }

    [Fact]
    public void Canonical_tabs_have_one_topology_source()
    {
        var canonical = ReadRepositoryFile(
                "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.cs")
            + ReadRepositoryFile(
                "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Ordinary.cs")
            + ReadRepositoryFile(
                "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.Contextual.cs");
        var wpf = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avalonia = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWAvaloniaRibbonDefinition.cs");

        foreach (var tabId in new[]
                 {
                     "home", "insert", "references", "review",
                     "layout", "design", "view", "mailings", "help", "developer",
                 })
        {
            canonical.Should().Contain($".Tab(\"{tabId}\"");
            wpf.Should().NotContain($".Tab(\"{tabId}\"");
            avalonia.Should().NotContain($".Tab(\"{tabId}\"");
        }

        canonical.Should().Contain("builder.ContextualTab(\"header-footer-design\"");
        wpf.Should().NotContain(".ContextualTab(\"header-footer-design\"");
        avalonia.Should().NotContain(".ContextualTab(\"header-footer-design\"");

        foreach (var tabId in new[]
                 {
                     "picture-format", "drawing-format", "chart-design", "chart-format",
                     "smartart-design", "table-design", "table-layout",
                 })
        {
            canonical.Should().Contain($".ContextualTab(\"{tabId}\"");
            wpf.Should().NotContain($".ContextualTab(\"{tabId}\"");
            avalonia.Should().NotContain($".ContextualTab(\"{tabId}\"");
        }

        foreach (var method in new[]
                 {
                     "AddHomeTab(capabilities)",
                     "AddInsertTab(capabilities)",
                     "AddReferencesTab(capabilities)",
                     "AddReviewTab(capabilities)",
                     "AddLayoutTab(capabilities)",
                     "AddDesignTab(capabilities)",
                     "AddViewTab(capabilities)",
                     "AddMailingsTab(capabilities)",
                     "AddHelpTab(capabilities)",
                     "AddDeveloperTab(capabilities)",
                     "AddHeaderFooterDesignTab(capabilities)",
                     "AddPictureContextualTab(capabilities)",
                     "AddDrawingContextualTab(capabilities)",
                     "AddChartContextualTabs(capabilities)",
                     "AddSmartArtContextualTab(capabilities)",
                     "AddTableContextualTabs(capabilities)",
                 })
        {
            wpf.Should().Contain(method);
            avalonia.Should().Contain(method);
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string TabSignature(RibbonTab tab) =>
        $"{tab.Id}|{tab.Header}|{tab.KeyTip}|{tab.Context}|" +
        string.Join(";;", tab.Groups.Select(GroupSignature));

    private static string GroupSignature(RibbonGroup group) =>
        $"{group.Id}|{group.Header}|{group.KeyTip}|{group.Priority}|" +
        $"{string.Join(',', group.Sizing.SupportedVariants)}|{group.Sizing.Hints}|" +
        string.Join(';', group.Controls.Select(ControlSignature));

    private static string ControlSignature(RibbonControl control) =>
        $"{control.GetType().Name}|{control.CommandId.Value}|{control.Label}|{control.KeyTip}|" +
        $"{control.Icon}|{control.PreferredLayout}|{control.TooltipTitle}|{control.TooltipDescription}|{ControlExtra(control)}";

    private static string ControlExtra(RibbonControl control) => control switch
    {
        RibbonComboBox combo => $"{combo.Width}|{string.Join(',', combo.Items)}",
        RibbonDropdown dropdown => MenuSignature(dropdown.Menu),
        RibbonSplitButton splitButton => MenuSignature(splitButton.Menu),
        _ => string.Empty,
    };

    private static string MenuSignature(RibbonMenu menu) =>
        string.Join(',', menu.Items.Select(MenuItemSignature));

    private static string MenuItemSignature(RibbonMenuItem item) =>
        $"{item.Header}|{item.CommandId?.Value}|{item.KeyTip}|{item.InputGesture}|{item.Kind}|{item.IsEnabled}|{item.IsChecked}|" +
        string.Join(';', item.Children.Select(MenuItemSignature));

    private static string ReadRepositoryFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);

    private static JsonDocument ReadCanonicalEvidence() =>
        JsonDocument.Parse(ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions.Tests", "freew-canonical-ribbon-evidence.json"));
}
