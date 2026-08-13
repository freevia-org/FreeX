using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Backstage rail tests, de-brittled for the unification program (P1). The rail moved from a hand-rolled
/// <c>StartScreenSidebar</c> to the shared <see cref="Free.Shared.Shell.Wpf.BackstageFrame"/>, so the rail
/// assertions are now <b>behavioural / automation-tree</b> queries against a live window (open the backstage,
/// read the rail buttons' AutomationIds / KeyTips / localized names / tooltips and the pane-swap behaviour)
/// instead of literal XAML <c>x:Name</c> / handler-name lookups. Tests about the backstage <i>content panes</i>
/// (Recent/Pinned, search box, Info copy, progress footer, templates) are unchanged — those XAML subtrees were
/// not touched by this migration.
/// </summary>
public sealed partial class MainWindowXamlKeyTipTests
{
    // ── Rail (now on the shared BackstageFrame) — behavioural assertions ─────────────

    [Fact]
    public void BackstageSidebarButtons_RenderAccessKeyMarkersAsMnemonics()
    {
        // Save / Save As / Close labels carry mnemonic underscores; the frame renders labels through
        // AccessText, so the access key is recognised (not shown literally). Assert via the live control.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var save = harness.RailButton("BackstageSaveButton")!;
            var accessText = Descendants(save).OfType<AccessText>().Single();
            accessText.AccessKey.Should().Be('S', "the rail renders '_Save' as an access-key mnemonic");
        });
    }

    [Fact]
    public void BackstageInteractiveIcons_UseLargeReadableSlots()
    {
        // Rail glyphs: assert behaviourally that every rail button shows a sized icon. The Recent/Pinned
        // pin buttons live in a content pane (unchanged) and keep their XAML style assertions below.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var railIcons = harness.RailButtons()
                .SelectMany(button => Descendants(button).OfType<System.Windows.FrameworkElement>())
                .Where(element => element is not AccessText and not TextBlock)
                .Where(element => element.Width >= 20 || element.Height >= 20)
                .ToList();
            railIcons.Should().NotBeEmpty("every primary rail entry shows a leading glyph");
            railIcons.Should().OnlyContain(icon => icon.Width >= 20 || icon.Height >= 20, "rail glyphs use large readable slots");
        });

        var resources = DialogSourceTestSupport.LoadHostXamlDocument("Resources", "MainWindowResources.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var pinButtonStyle = StyleByKey(resources, presentation, x, "BackstageRecentPinCommandButton");
        SetterValue(pinButtonStyle, presentation, "Width").Should().Be("32");
        SetterValue(pinButtonStyle, presentation, "Height").Should().Be("32");

        var pinIconStyle = StyleByKey(resources, presentation, x, "BackstageRecentPinCommandIcon");
        SetterValue(pinIconStyle, presentation, "IconSize").Should().Be("24");
    }

    [Fact]
    public void BackstageSaveAsButton_UsesAccessKeyMatchingKeyTip()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var saveAs = harness.RailButton("BackstageSaveAsButton")!;
            var accessText = Descendants(saveAs).OfType<AccessText>().Single();
            accessText.AccessKey.Should().Be('A', "'Save _As' exposes A as its access key");
            harness.KeyTip(saveAs).Should().Be("A", "the keytip matches the access key");
        });
    }

    [Fact]
    public void BackstagePrintButton_ExposesPreviewAndNativePrintMetadata()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var print = harness.RailButton("BackstagePrintButton")!;
            harness.KeyTip(print).Should().Be("P");
            harness.AutomationName(print).Should().Be(UiText.Get("MainWindow_AutomationName_Print"));
            harness.AutomationHelpText(print).Should().Contain("native print access");
        });
    }

    [Fact]
    public void BackstagePrimarySidebarButtons_ExposeStableAutomationMetadata()
    {
        var expected = new (string AutomationId, string AutomationName, string HelpFragment)[]
        {
            ("BackstageBackButton", UiText.Get("MainWindow_TooltipTitle_Back"), "workbook"),
            ("BackstageHomeButton", UiText.Get("MainWindow_Text_Home"), "Home"),
            ("BackstageNewButton", UiText.Get("Common_New"), "new workbook"),
            ("BackstageOpenButton", UiText.Get("MainWindow_Text_Open"), "existing workbook"),
            ("BackstageSaveButton", UiText.Get("MainWindow_AutomationName_Save"), "Save the workbook"),
            ("BackstageSaveAsButton", UiText.Get("MainWindow_TooltipTitle_SaveAs"), "new name"),
            ("BackstageCloseButton", UiText.Get("MainWindow_AutomationName_Close"), "Close"),
        };

        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            foreach (var (automationId, automationName, helpFragment) in expected)
            {
                var button = harness.RailButton(automationId);
                button.Should().NotBeNull($"the rail must expose a stable '{automationId}' automation id");
                harness.AutomationName(button!).Should().Be(automationName);
                harness.AutomationHelpText(button!).Should().Contain(helpFragment);
            }
        });
    }

    [Fact]
    public void BackstageShareInfoAndExportButtons_ExposeStableAutomationMetadata()
    {
        var expected = new (string AutomationId, string AutomationName, string HelpFragment)[]
        {
            ("BackstageShareButton", UiText.Get("MainWindow_Text_Share"), "Windows Share"),
            ("BackstageInfoButton", UiText.Get("MainWindow_Text_Info"), "unsupported workbook feature warnings"),
            ("BackstageExportButton", UiText.Get("MainWindow_TooltipTitle_ExportPDFXPS"), "XPS"),
        };

        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            foreach (var (automationId, automationName, helpFragment) in expected)
            {
                var button = harness.RailButton(automationId);
                button.Should().NotBeNull($"the rail must expose a stable '{automationId}' automation id");
                harness.AutomationName(button!).Should().Be(automationName);
                harness.AutomationHelpText(button!).Should().Contain(helpFragment);
            }
        });
    }

    [Fact]
    public void BackstageAccountEntryPoint_DisclosesLocalAccountDecision()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var account = harness.RailButton("BackstageAccountButton")!;
            harness.AutomationName(account).Should().Be(UiText.Get("MainWindow_AutomationName_Account"));
            harness.AutomationHelpText(account).Should().Contain("Show local account information");
            harness.KeyTip(account).Should().Be("D");
            harness.TooltipTitle(account).Should().Contain("Local");
            harness.TooltipDescription(account).Should().Contain("Microsoft account");
            account.IsTabStop.Should().BeTrue();
        });
    }

    [Fact]
    public void BackstageOptionsEntryPoint_IsNamedCommandForUiAutomation()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var options = harness.RailButton("BackstageOptionsButton")!;
            harness.AutomationName(options).Should().Be(UiText.Get("MainWindow_AutomationName_Options"));
            harness.AutomationHelpText(options).Should().Contain("Open FreeX settings");
            options.IsTabStop.Should().BeTrue();
        });
    }

    [Fact]
    public void BackstageExportEntryPoint_DisclosesRealPdfAndXpsExport()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var export = harness.RailButton("BackstageExportButton")!;
            harness.TooltipTitle(export).Should().Be(UiText.Get("MainWindow_TooltipTitle_ExportPDFXPS"));
            var description = harness.TooltipDescription(export);
            description.Should().Contain("PDF");
            description.Should().Contain("XPS");
            description.Should().Contain("selection");
            description.Should().Contain("workbook");
            description.Should().NotContain("active sheet");
            description.Should().NotContain("PDF printer");
        });
    }

    [Fact]
    public void BackstageCommandButtons_HaveAltKeyTips()
    {
        // Every rail command button must be reachable through an Excel-style Alt keytip.
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var missing = harness.RailButtons()
                .Where(button => string.IsNullOrWhiteSpace(harness.KeyTip(button)))
                .Select(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button))
                .ToList();

            missing.Should().BeEmpty("File/Backstage rail commands should be reachable through Alt keytips");
        });
    }

    [Fact]
    public void BackstageCommandButtons_ExposeVisibleAccessKeysForSaveAndClose()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var accessKeys = harness.RailButtons()
                .SelectMany(button => Descendants(button).OfType<AccessText>())
                .Select(accessText => accessText.AccessKey)
                .ToList();

            accessKeys.Should().Contain('S', "Save exposes a visible access key");
            accessKeys.Should().Contain('C', "Close exposes a visible access key");
        });
    }

    [Fact]
    public void BackstageCommands_DoNotReuseKeyTips()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = BackstageRailHarness.Create();
            harness.OpenBackstage();

            var duplicates = harness.RailButtons()
                .Select(button => harness.KeyTip(button))
                .Where(keyTip => !string.IsNullOrWhiteSpace(keyTip))
                .GroupBy(keyTip => keyTip, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            duplicates.Should().BeEmpty("Backstage rail keytips should route deterministically without duplicates");
        });
    }

    // Backstage content panes and their shared presentation contracts.

    [Fact]
    public void BackstageAccountAndAbout_UseSharedAssemblyVersionPresentation()
    {
        var account = LocalAccountInfoPlanner.Build(typeof(MainWindow).Assembly);

        account.VersionText.Should().Be(AppInfo.ExactVersionText);
        AppInfo.AboutText.Should().Contain(AppInfo.VersionText);
    }

    [Fact]
    public void BackstageInfo_DoesNotAdvertiseCloudDocumentManagement()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cloudCopy = document
            .Descendants(presentation + "TextBlock")
            .Select(element => LocalizedAttribute(element, "Text") ?? element.Value)
            .Where(text =>
                text.Contains("check in", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("check out", StringComparison.OrdinalIgnoreCase))
            .ToList();

        cloudCopy.Should().BeEmpty("SharePoint-style check-in/out workflows are excluded from FreeX");
    }

    [Fact]
    public void BackstageInfo_DoesNotAdvertiseDocumentInspector()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var inspectorCopy = document
            .Descendants(presentation + "TextBlock")
            .Select(element => LocalizedAttribute(element, "Text") ?? element.Value)
            .Where(text =>
                text.Contains("hidden properties", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("personal information", StringComparison.OrdinalIgnoreCase))
            .ToList();

        inspectorCopy.Should().BeEmpty("FreeX currently implements an accessibility checker, not Excel's full Document Inspector");
    }

    [Fact]
    public void BackstageInfo_ShowsFormulaErrorSummary()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        document
            .Descendants(presentation + "TextBlock")
            .Select(element => LocalizedAttribute(element, "Text"))
            .Should()
            .Contain("Formula errors");

        var hasFormulaSummary = document
            .Descendants(presentation + "TextBlock")
            .Any(element => element.Attribute(xaml + "Name")?.Value == "InfoFormulaErrorSummary");

        hasFormulaSummary.Should().BeTrue();
    }

    [Fact]
    public void BackstageRecentList_ProvidesVisiblePinAndUnpinButtons()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var visibleButtons = document
            .Descendants(presentation + "Button")
            .Select(element => element.Attribute("Click")?.Value)
            .ToList();

        visibleButtons.Should().Contain("SsPinItem_Click", "pinning should not be hidden behind a context menu");
        visibleButtons.Should().Contain("SsUnpinItem_Click", "pinned files need a visible unpin affordance");
    }

    [Fact]
    public void BackstageRecentAndPinnedItems_ExposeStableUiAutomationAndContextKeyTips()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "SsRecentItem_Click" or "SsPinItem_Click" or "SsUnpinItem_Click")
            .Select(button => button.ToString())
            .ToList();

        buttons.Should().OnlyContain(markup => markup.Contains("AutomationProperties.Name="));
        buttons.Should().OnlyContain(markup => markup.Contains("AutomationProperties.HelpText="));

        var xamlSource = DialogSourceTestSupport.ReadHostSources("MainWindow.xaml");
        xamlSource.Should().Contain("Loaded=\"SsRecentFileItem_Loaded\"");
        xamlSource.Should().Contain("Loaded=\"SsPinnedFileItem_Loaded\"");
        xamlSource.Should().Contain("Loaded=\"SsRecentPinCommandButton_Loaded\"");
        xamlSource.Should().Contain("Loaded=\"SsPinnedUnpinCommandButton_Loaded\"");
        xamlSource.Should().NotContain("AutomationProperties.AutomationId=\"BackstageRecentFileItem\"");
        xamlSource.Should().NotContain("AutomationProperties.AutomationId=\"BackstagePinnedFileItem\"");
        xamlSource.Should().NotContain("AutomationProperties.AutomationId=\"BackstageRecentPinButton\"");
        xamlSource.Should().NotContain("AutomationProperties.AutomationId=\"BackstagePinnedUnpinButton\"");

        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var contextMenuSource = DialogSourceTestSupport.ReadHostSources("MainWindow.ContextMenus.cs");
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources("Backstage", "FreeXBackstageHomePanePlanner.cs");
        backstageSource.Should().Contain("ApplyBackstageRecentFileRowDescriptor(");
        backstageSource.Should().Contain("ConfigureBackstageRecentFileCommandButton(");
        contextMenuSource.Should().Contain("ApplyBackstageRecentFileRowDescriptor(element, FreeXBackstageRecentFileRowKind.Recent)");
        contextMenuSource.Should().Contain("ApplyBackstageRecentFileRowDescriptor(element, FreeXBackstageRecentFileRowKind.Pinned)");
        plannerSource.Should().Contain("\"BackstageRecentFileItem\"");
        plannerSource.Should().Contain("\"BackstagePinnedFileItem\"");
        plannerSource.Should().Contain("\"BackstageRecentPinButton\"");
        plannerSource.Should().Contain("\"BackstagePinnedUnpinButton\"");

        var contextMenuItems = BackstageRecentFileContextMenuPlanner.BuildRecentFileCommands()
            .Concat(BackstageRecentFileContextMenuPlanner.BuildPinnedFileCommands())
            .Select(command => new
            {
                Header = UiText.Get(command.ResourceKey),
                command.KeyTip,
                AutomationId = command.AutomationId,
                AutomationNamePath = command.AutomationNamePath,
                AutomationHelpTextPath = command.AutomationHelpTextPath
            })
            .ToList();

        contextMenuItems.Should().Contain(item => item.Header == "Pin to list" && item.KeyTip == "P");
        contextMenuItems.Should().Contain(item => item.Header == "Unpin from list" && item.KeyTip == "U");
        contextMenuItems.Should().Contain(item => item.Header == "Remove from list" && item.KeyTip == "R");
        contextMenuItems.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.AutomationId));
        contextMenuItems.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.AutomationNamePath));
        contextMenuItems.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.AutomationHelpTextPath));
    }

    [Fact]
    public void EscapeFromVisibleBackstage_ReturnsToWorkbookBeforeTransientCancellation()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");

        source.Should().Contain("IsStartScreenVisible()");
        source.Should().Contain("HideStartScreen();");
        source.IndexOf("HideStartScreen();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf("CancelCopyAndTransientModes();", StringComparison.Ordinal));
    }

    [Fact]
    public void BackstageRecentPinnedTabs_AreKeyboardReachableCommands()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        document
            .Descendants()
            .Where(element => element.Attribute("MouseDown")?.Value is "SsRecentTab_MouseDown" or "SsPinnedTab_MouseDown")
            .Should()
            .BeEmpty("Recent/Pinned Backstage tab selectors should be command buttons, not mouse-only elements");

        document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "SsRecentTab_Click" or "SsPinnedTab_Click")
            .Select(button => button.Attribute(x + "Name")?.Value)
            .Should()
            .BeEquivalentTo("SsRecentTabButton", "SsPinnedTabButton");

        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources("Backstage", "FreeXBackstageHomePanePlanner.cs");

        source.Should().Contain("ConfigureBackstageRecentTab(plan.RecentTab, SsRecentTabButton, SsRecentTabText)");
        source.Should().Contain("ConfigureBackstageRecentTab(plan.PinnedTab, SsPinnedTabButton, SsPinnedTabText)");
        source.Should().Contain("RibbonTooltip.SetKeyTip(button, descriptor.KeyTip);");
        plannerSource.Should().Contain("\"RC\"");
        plannerSource.Should().Contain("\"PN\"");
    }

    [Fact]
    public void BackstageSearchBox_HasAccessibleNameAndHelpText()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "SsSearchBox");

        searchBox.Attribute("AutomationProperties.Name")
            .Should()
            .BeNull("Backstage search descriptor ownership lives in the Presentation planner");
        searchBox.Attribute("AutomationProperties.HelpText")
            .Should()
            .BeNull("Backstage search descriptor ownership lives in the Presentation planner");

        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var plannerSource = DialogSourceTestSupport.ReadPresentationSources("Backstage", "FreeXBackstageHomePanePlanner.cs");

        source.Should().Contain("System.Windows.Automation.AutomationProperties.SetName(");
        source.Should().Contain("System.Windows.Automation.AutomationProperties.SetHelpText(");
        source.Should().Contain("UiText.Get(plan.Search.AutomationNameKey)");
        source.Should().Contain("UiText.Get(plan.Search.AutomationHelpTextKey)");
        plannerSource.Should().Contain("\"MainWindow_AutomationName_SearchRecentFiles\"");
        plannerSource.Should().Contain("\"MainWindow_AutomationHelpText_FilterRecentAndPinnedFiles\"");
    }

    [Fact]
    public void BackstageOperationProgress_ExposesAccessibleStatusTextInFooter()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var overlay = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value == "OpenProgressOverlay");

        LocalizedAttribute(overlay, "AutomationProperties.Name").Should().Be("Opening workbook");
        overlay.Attribute("Panel.ZIndex")?.Value.Should().Be("260");
        overlay.Attribute("Background")?.Value.Should().Be("Transparent");

        var progressBar = document
            .Descendants(presentation + "ProgressBar")
            .Single(element => element.Attribute(x + "Name")?.Value == "StatusSaveProgressBar");

        progressBar.Attribute("Minimum")?.Value.Should().Be("0");
        progressBar.Attribute("Maximum")?.Value.Should().Be("100");

        var progressText = document
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute(x + "Name")?.Value == "StatusSaveProgressText");

        LocalizedAttribute(progressText, "AutomationProperties.Name").Should().Be("Open progress detail");
        progressText.Attribute("AutomationProperties.LiveSetting")?.Value
            .Should().Be("Assertive");

        var cancelButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "StatusSaveProgressCancelButton");

        cancelButton.Attribute("Click")?.Value.Should().Be("CancelFileOperation_Click");
        cancelButton.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be("StatusSaveProgressCancelButton");
        cancelButton.Attribute("Visibility")?.Value.Should().Be("Collapsed");
    }

    [Fact]
    public void ExternalTemplateEntryPoint_DisclosesExcludedStatusBeforeClick()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace ribbonWpf = "clr-namespace:Free.Shared.Ribbon.Wpf;assembly=Free.Shared.Ribbon.Wpf";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "SsMoreTemplatesBtn_Click")
            .Where(element =>
                !ContainsExcludedStatus(LocalizedAttribute(element, "Content")) &&
                !ContainsExcludedStatus(LocalizedAttribute(element, ribbonWpf + "RibbonTooltip.Title")) &&
                !ContainsExcludedStatus(LocalizedAttribute(element, ribbonWpf + "RibbonTooltip.Description")))
            .Select(element => LocalizedAttribute(element, "Content") ?? element.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("online template discovery depends on an external Microsoft service and should not look like a normal local command");

        var button = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SsMoreTemplatesBtn_Click");

        button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be("MoreTemplatesExcludedButton");
        LocalizedAttribute(button, "AutomationProperties.Name").Should().Be("More templates unavailable");
        LocalizedAttribute(button, "AutomationProperties.HelpText")
            .Should()
            .Contain("external Microsoft template service");

        document
            .Descendants()
            .Any(element => element.Attribute("MouseDown")?.Value == "SsMoreTemplates_MouseDown")
            .Should().BeFalse("online template discovery should be a normal command button, not a mouse-only text element");
    }

    // Visual-tree descendant walk shared by the behavioural rail tests above.
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
