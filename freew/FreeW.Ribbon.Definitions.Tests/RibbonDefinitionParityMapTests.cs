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
    public void Canonical_tabs_have_one_topology_source()
    {
        var canonical = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWCanonicalRibbonTabs.cs");
        var wpf = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWRibbon.cs");
        var avalonia = ReadRepositoryFile(
            "freew", "FreeW.Ribbon.Definitions", "FreeWAvaloniaRibbonDefinition.cs");

        foreach (var tabId in new[] { "layout", "design", "view", "mailings", "help", "developer" })
        {
            canonical.Should().Contain($"builder.Tab(\"{tabId}\"");
            wpf.Should().NotContain($".Tab(\"{tabId}\"");
            avalonia.Should().NotContain($".Tab(\"{tabId}\"");
        }

        canonical.Should().Contain("builder.ContextualTab(\"header-footer-design\"");
        wpf.Should().NotContain(".ContextualTab(\"header-footer-design\"");
        avalonia.Should().NotContain(".ContextualTab(\"header-footer-design\"");

        foreach (var method in new[]
                 {
                     "AddLayoutTab(capabilities)",
                     "AddDesignTab(capabilities)",
                     "AddViewTab(capabilities)",
                     "AddMailingsTab(capabilities)",
                     "AddHelpTab(capabilities)",
                     "AddDeveloperTab(capabilities)",
                     "AddHeaderFooterDesignTab(capabilities)",
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
}
