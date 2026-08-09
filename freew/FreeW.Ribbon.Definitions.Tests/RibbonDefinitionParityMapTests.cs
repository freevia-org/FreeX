using Free.Shared.Ribbon;
using System.Security.Cryptography;
using System.Text;

namespace FreeW.Ribbon.Definitions.Tests;

public sealed class FreeWRibbonCanonicalOwnershipTests
{
    [Theory]
    [InlineData(
        "mailings",
        "F6D547A341EFBF177E91E7DD22A217825B38B5F7B79D51927069AE5DAD06362A",
        "F0BC3AAF9EA7E8EBCC5874CD92802EF8A6FE756614B177172BF9F089AB090101")]
    [InlineData(
        "help",
        "4ABD4339DBA4600878BFBCF618FBA6E4661793ED695225EDBF8F5DDA9E226E15",
        "41FC2000DC1A8EFCBC53FB00A44BEF8C6F14A94637D0EF3A4824522B2919FC2B")]
    [InlineData(
        "developer",
        "4AE872EBB61731CD74CE71F42D422E5C94A626D90D2B5EFFA41B31F26C8351B5",
        "61C80E954A1CAB0693B4B502D3E1F149644A29E7788188D42AFD5351F3A34E1C")]
    [InlineData(
        "header-footer-design",
        "73F64F4CB1B827C725BB0B7D859AEE64C0B4334790C7775CB26DE6BDD7B4F85D",
        "192200D0E46A349DFC699AB8F4C61C258E48E14BC227D14CB865862BD0FD4FA1")]
    [InlineData(
        "layout",
        "ABB78A14EAD7FE7C4DB07C1641A959452B591378344EC41D59168446BEF469E5",
        "AC664FAD5AB943B6B9B44C58C7E61F7B6E3C62EA65F3D3FAF5058A8D5B5EC809")]
    [InlineData(
        "design",
        "F5FD66D827A1307E1DA12499C801B6935A8DC99E719BD9AD81EA45173E762E1D",
        "052B10D319879EB041E658D90160F3377C1C8505F42AEF7785E76ECDC750A1DB")]
    [InlineData(
        "view",
        "CB9249AAD3A6B6A5BB00C86AE16F2FF13697EAE694B5F54E0F33911D9D5A63BF",
        "4B41DCA044EF9FF14EB2A0B3239D4722C5EC13D41B1C127D1AEF1602C9D648D1")]
    [InlineData("home", "03022E585BA534F49C0A1E21D0CE739BE3B4E07B8EBCA5166A4502C3E5ED7292", "BF7FB5D3B559CB4730B3738AC6E241329BB542546BAFF11E0136D27E6FCCBE39")]
    [InlineData("insert", "CDE6CB1B0682F56F84BCBB2F3775F71CE67D5BAB9E2B9245A7B0AA0DE93AD149", "4C2CA0C7C39E93EF1DB3BAD06B7EA3388F6BB88952B3C015079849A765734169")]
    [InlineData("references", "E45FFB1DFBE9D61F7D883E43AFB232E82F8F8465C6F8E54B15AA5E902BEB504A", "CFE21A6FCDC9FAB33072CB6D7158FA02F36050AA404C55699F95F54E21CE20FB")]
    [InlineData("review", "0C8DB7D034E83F9B916B024D09E3FCFBF335788384DDC513EE1CFDE3736F1229", "99C0BDD4F259BB161D2A4C472E599E9F95BD2FB60709C2628462EB9DDC4C098A")]
    [InlineData("picture-format", "41DB277D9F8020B6D2D5F7C3219277E1B4AB237534E9D017CEB43B2A57616C36", "6A870A4B03BDE0BD8FC621733A2C5F6E9285DC0FCC41A97DBB0C9CEFA12A6AC2")]
    [InlineData("drawing-format", "8AFC55553834293FEE94A4F2533397D06185ADDF5CC0142FCD8C181DFA18001B", "8676F2ADE3B3D40F44C37D744AB22EC8F01A4ED7AB3D60FADA115EDA192F7596")]
    [InlineData("chart-design", "DF95A211B1E9347C05BC69E46FAAE61ACD7C0F0C8AF973DC5E8DD605E42F8481", "D8D06B5AD20E7754723A65015BF47EEF98EFE81F86732F6EE9CD0FCA949F6302")]
    [InlineData("chart-format", "507941161285E53E4C35D78207EEE011D7183F03FE64D096166D684E18EF6627", "853C21868195CFE51E6552BF28561CE28E4ED4B038F56B769619DFC2F06AE702")]
    [InlineData("smartart-design", "F7D06880C58251873E9F6D8AD0847D193C5FB7089E8359D6B295BB58D14F4E57", "81673F9766834D02E289D8790817976BD4F15D481E5C2815B00E5D7AB99FDA1C")]
    [InlineData("table-design", "BFB4E5C19244C2BA166E24B59292F5E545134E456FE0C0EED697D0A49B4E63AC", "148AFFF6A81095FF1EC4B50EF153174585A3D136870E348407DD92F7A98AD83D")]
    [InlineData("table-layout", "3898F1D24BEED766E973B825FAB541D482927949F287704B27F536BF75BC7B02", "5CACC32C46561EA56796672E78073057B02D78EA9631E106E1F8EE41BDB7E8A4")]
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
}
