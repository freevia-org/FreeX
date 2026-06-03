using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void BackstageSidebarButtons_RenderAccessKeyMarkersAsMnemonics()
    {
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var sidebarButtonStyle = resources
            .Descendants(presentation + "Style")
            .Single(element => element.Attribute(x + "Key")?.Value == "SsNavBtn");

        sidebarButtonStyle
            .Descendants(presentation + "ContentPresenter")
            .Single()
            .Attribute("RecognizesAccessKey")
            ?.Value
            .Should()
            .Be("True");
    }

    [Fact]
    public void BackstageInteractiveIcons_UseLargeReadableSlots()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var sidebarStyle = StyleByKey(resources, presentation, x, "BackstageSidebarIcon");
        SetterValue(sidebarStyle, presentation, "IconSize").Should().Be("24");
        SetterValue(sidebarStyle, presentation, "Margin").Should().Be("0,0,12,0");

        var pinButtonStyle = StyleByKey(resources, presentation, x, "BackstageRecentPinCommandButton");
        SetterValue(pinButtonStyle, presentation, "Width").Should().Be("32");
        SetterValue(pinButtonStyle, presentation, "Height").Should().Be("32");

        var pinIconStyle = StyleByKey(resources, presentation, x, "BackstageRecentPinCommandIcon");
        SetterValue(pinIconStyle, presentation, "IconSize").Should().Be("24");

        var sidebar = document
            .Descendants(presentation + "DockPanel")
            .Single(element => element.Attribute(x + "Name")?.Value == "StartScreenSidebar");

        var sidebarIcons = sidebar
            .Descendants(local + "RibbonIcon")
            .ToList();

        sidebarIcons.Should().NotBeEmpty();
        sidebarIcons.Should().OnlyContain(icon => icon.Attribute("IconSize") == null);
        sidebarIcons
            .Select(icon => icon.Attribute("Style")?.Value)
            .Should()
            .OnlyContain(style =>
                string.Equals(style, "{StaticResource BackstageSidebarIcon}", StringComparison.Ordinal) ||
                string.Equals(style, "{StaticResource BackstageSidebarBackIcon}", StringComparison.Ordinal));

        var pinButtons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("AutomationProperties.AutomationId")?.Value is
                "BackstageRecentPinButton" or "BackstagePinnedUnpinButton")
            .ToList();

        pinButtons.Should().HaveCount(2);
        pinButtons.Select(button => button.Attribute("Style")?.Value)
            .Should()
            .OnlyContain(style => string.Equals(style, "{StaticResource BackstageRecentPinCommandButton}", StringComparison.Ordinal));
        pinButtons
            .SelectMany(button => button.Descendants(local + "RibbonIcon"))
            .Select(icon => icon.Attribute("Style")?.Value)
            .Should()
            .OnlyContain(style => string.Equals(style, "{StaticResource BackstageRecentPinCommandIcon}", StringComparison.Ordinal));
    }

    [Fact]
    public void BackstageSaveAsButton_UsesAccessKeyMatchingKeyTip()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var saveAsButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SaveAsButton_Click");

        GetButtonText(saveAsButton, presentation).Should().Be("Save _As");
        saveAsButton.Descendants(local + "RibbonIcon")
            .Single()
            .Attribute("CommandName")?.Value.Should().Be("Save As");
        saveAsButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("A");
    }

    [Fact]
    public void BackstagePrintButton_ExposesPreviewAndNativePrintMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var printButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == "SsPrintNavBtn");

        printButton.Attribute("Click")?.Value.Should().Be("PrintButton_Click");
        printButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("P");
        printButton.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be("BackstagePrintButton");
        LocalizedAttribute(printButton, "AutomationProperties.Name").Should().Be("Print");
        LocalizedAttribute(printButton, "AutomationProperties.HelpText")
            .Should()
            .Contain("native print access");
    }

    [Fact]
    public void BackstagePrimarySidebarButtons_ExposeStableAutomationMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var expectedButtons = new[]
        {
            (
                Name: "SsBackBtn",
                Click: "SsBackBtn_Click",
                AutomationId: "BackstageBackButton",
                AutomationName: "Back",
                HelpTextFragment: "workbook"),
            (
                Name: "SsHomeNavBtn",
                Click: "SsHomeNavBtn_Click",
                AutomationId: "BackstageHomeButton",
                AutomationName: "Home",
                HelpTextFragment: "Home"),
            (
                Name: "SsNewNavBtn",
                Click: "SsNewBtn_Click",
                AutomationId: "BackstageNewButton",
                AutomationName: "New",
                HelpTextFragment: "new workbook"),
            (
                Name: "SsOpenNavBtn",
                Click: "SsOpenBtn_Click",
                AutomationId: "BackstageOpenButton",
                AutomationName: "Open",
                HelpTextFragment: "existing workbook"),
            (
                Name: "SsSaveNavBtn",
                Click: "SaveButton_Click",
                AutomationId: "BackstageSaveButton",
                AutomationName: "Save",
                HelpTextFragment: "Save the workbook"),
            (
                Name: "SsSaveAsNavBtn",
                Click: "SaveAsButton_Click",
                AutomationId: "BackstageSaveAsButton",
                AutomationName: "Save As",
                HelpTextFragment: "new name"),
            (
                Name: "SsCloseNavBtn",
                Click: "SsCloseBtn_Click",
                AutomationId: "BackstageCloseButton",
                AutomationName: "Close",
                HelpTextFragment: "Close")
        };

        foreach (var expected in expectedButtons)
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute(x + "Name")?.Value == expected.Name);

            button.Attribute("Click")?.Value.Should().Be(expected.Click);
            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(expected.AutomationId);
            LocalizedAttribute(button, "AutomationProperties.Name").Should().Be(expected.AutomationName);
            LocalizedAttribute(button, "AutomationProperties.HelpText")
                .Should()
                .Contain(expected.HelpTextFragment);
        }
    }

    [Fact]
    public void BackstageShareInfoAndExportButtons_ExposeStableAutomationMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var expectedButtons = new[]
        {
            (
                Name: "SsShareNavBtn",
                Click: "SsShareBtn_Click",
                AutomationId: "BackstageShareButton",
                AutomationName: "Share",
                HelpTextFragment: "Windows Share"),
            (
                Name: "SsInfoNavBtn",
                Click: "SsInfoBtn_Click",
                AutomationId: "BackstageInfoButton",
                AutomationName: "Info",
                HelpTextFragment: "unsupported workbook feature warnings"),
            (
                Name: "SsExportNavBtn",
                Click: "ExportPdfButton_Click",
                AutomationId: "BackstageExportButton",
                AutomationName: "Export PDF/XPS",
                HelpTextFragment: "XPS")
        };

        foreach (var expected in expectedButtons)
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute(x + "Name")?.Value == expected.Name);

            button.Attribute("Click")?.Value.Should().Be(expected.Click);
            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(expected.AutomationId);
            LocalizedAttribute(button, "AutomationProperties.Name").Should().Be(expected.AutomationName);
            LocalizedAttribute(button, "AutomationProperties.HelpText")
                .Should()
                .Contain(expected.HelpTextFragment);
        }
    }

    [Fact]
    public void BackstageInfoVersion_MatchesAboutDialogVersion()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "TextBlock")
            .Where(element => LocalizedAttribute(element, "Text") == AppInfo.VersionText)
            .Should()
            .ContainSingle("Backstage Info and About should show the same FreeX version");
    }

    [Fact]
    public void BackstageInfo_DoesNotAdvertiseCloudDocumentManagement()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "SsRecentItem_Click" or "SsPinItem_Click" or "SsUnpinItem_Click")
            .Select(button => button.ToString())
            .ToList();

        buttons.Should().Contain(markup => markup.Contains("AutomationProperties.AutomationId=\"BackstageRecentFileItem\""));
        buttons.Should().Contain(markup => markup.Contains("AutomationProperties.AutomationId=\"BackstagePinnedFileItem\""));
        buttons.Should().Contain(markup => markup.Contains("AutomationProperties.AutomationId=\"BackstageRecentPinButton\""));
        buttons.Should().Contain(markup => markup.Contains("AutomationProperties.AutomationId=\"BackstagePinnedUnpinButton\""));
        buttons.Should().OnlyContain(markup => markup.Contains("AutomationProperties.Name="));
        buttons.Should().OnlyContain(markup => markup.Contains("AutomationProperties.HelpText="));

        var contextMenuItems = document
            .Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Click")?.Value is "SsPinItem_Click" or "SsUnpinItem_Click" or "SsRemoveRecentItem_Click")
            .Select(item => new
            {
                Header = LocalizedAttribute(item, "Header"),
                Click = item.Attribute("Click")?.Value,
                KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                AutomationId = item.Attribute("AutomationProperties.AutomationId")?.Value,
                AutomationName = LocalizedAttribute(item, "AutomationProperties.Name"),
                AutomationHelpText = LocalizedAttribute(item, "AutomationProperties.HelpText")
            })
            .ToList();

        contextMenuItems.Should().Contain(item => item.Header == "Pin to list" && item.KeyTip == "P");
        contextMenuItems.Should().Contain(item => item.Header == "Unpin from list" && item.KeyTip == "U");
        contextMenuItems.Should().Contain(item => item.Header == "Remove from list" && item.KeyTip == "R");
        contextMenuItems.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.AutomationId));
        contextMenuItems.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.AutomationName));
        contextMenuItems.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.AutomationHelpText));
    }

    [Fact]
    public void BackstageAccountEntryPoint_DisclosesLocalAccountDecision()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var accountButton = document
            .Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "SsAccountNavBtn");

        accountButton.Attribute(x + "Name")?.Value.Should().Be("SsAccountNavBtn");
        accountButton.Attribute("Click")?.Value.Should().Be("SsAccountBtn_Click");
        LocalizedAttribute(accountButton, "AutomationProperties.Name").Should().Be("Account");
        accountButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"BackstageAccountButton\"");
        LocalizedAttribute(accountButton, "AutomationProperties.HelpText").Should().Contain("Show local account information");
        accountButton.Attribute("IsTabStop")?.Value.Should().Be("True");
        LocalizedAttribute(accountButton, local + "RibbonTooltip.Title").Should().Contain("Local");
        accountButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("AC");
        LocalizedAttribute(accountButton, local + "RibbonTooltip.Description").Should().Contain("Microsoft account");
    }

    [Fact]
    public void BackstageOptionsEntryPoint_IsNamedCommandForUiAutomation()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var optionsButton = document
            .Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "SsOptionsNavBtn");

        optionsButton.Attribute("Click")?.Value.Should().Be("SsOptionsBtn_Click");
        LocalizedAttribute(optionsButton, "AutomationProperties.Name").Should().Be("Options");
        optionsButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"BackstageOptionsButton\"");
        LocalizedAttribute(optionsButton, "AutomationProperties.HelpText").Should().Contain("Open FreeX settings");
        optionsButton.Attribute("IsTabStop")?.Value.Should().Be("True");
    }

    [Fact]
    public void EscapeFromVisibleBackstage_ReturnsToWorkbookBeforeTransientCancellation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));

        source.Should().Contain("IsStartScreenVisible()");
        source.Should().Contain("HideStartScreen();");
        source.IndexOf("HideStartScreen();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf("CancelCopyAndTransientModes();", StringComparison.Ordinal));
    }

    [Fact]
    public void BackstageExportEntryPoint_DisclosesRealPdfAndXpsExport()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var exportButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                GetButtonText(element, presentation) == "Export" &&
                element.Attribute("Click")?.Value == "ExportPdfButton_Click");

        LocalizedAttribute(exportButton, local + "RibbonTooltip.Title").Should().Be("Export PDF/XPS");
        LocalizedAttribute(exportButton, local + "RibbonTooltip.Description").Should().Contain("PDF");
        LocalizedAttribute(exportButton, local + "RibbonTooltip.Description").Should().Contain("XPS");
        LocalizedAttribute(exportButton, local + "RibbonTooltip.Description").Should().Contain("selection");
        LocalizedAttribute(exportButton, local + "RibbonTooltip.Description").Should().Contain("workbook");
        LocalizedAttribute(exportButton, local + "RibbonTooltip.Description").Should().NotContain("active sheet");
        LocalizedAttribute(exportButton, local + "RibbonTooltip.Description").Should().NotContain("PDF printer");
    }

    [Fact]
    public void BackstageCommandButtons_HaveAltKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var startScreen = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "StartScreenOverlay");

        var missing = startScreen
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click") is not null)
            .Where(button => button.Attribute("Click")?.Value != "SsRecentItem_Click")
            .Where(button => button.Attribute("Click")?.Value is not ("SsPinItem_Click" or "SsUnpinItem_Click"))
            .Where(button => button.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(button =>
                LocalizedAttribute(button, "Content") ??
                button.Attribute(x + "Name")?.Value ??
                button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("File/Backstage commands should be reachable through Excel-style Alt keytips");
    }

    [Fact]
    public void BackstageCommandButtons_ExposeVisibleAccessKeysForSaveAndClose()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var startScreen = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "StartScreenOverlay");

        startScreen.Descendants(presentation + "Button")
            .Select(button => GetButtonText(button, presentation))
            .Should()
            .Contain(["_Save", "_Close"]);
    }

    [Fact]
    public void BackstageMouseOnlyCommands_AreNotUsedForRecentPinnedTabs()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants()
            .Where(element => element.Attribute("MouseDown")?.Value is "SsRecentTab_MouseDown" or "SsPinnedTab_MouseDown")
            .Should()
            .BeEmpty("Recent/Pinned Backstage tab selectors should be command buttons, not mouse-only elements");

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "SsRecentTab_Click" or "SsPinnedTab_Click")
            .Where(button => button.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(button => LocalizedAttribute(button, "Content") ?? button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("Recent/Pinned Backstage tab selectors should participate in keytip navigation");
    }

    [Fact]
    public void BackstageCommands_DoNotReuseKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var startScreen = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "StartScreenOverlay");

        var duplicates = startScreen
            .Descendants()
            .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is not null)
            .Where(element => element.Name != presentation + "MenuItem")
            .GroupBy(element => element.Attribute(local + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        duplicates.Should().BeEmpty("Backstage keytips should route deterministically without duplicate visible command keys");
    }

    [Fact]
    public void BackstageSearchBox_HasAccessibleNameAndHelpText()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "SsSearchBox");

        var name = searchBox.Attribute("AutomationProperties.Name");
        var helpText = searchBox.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("Backstage search is a keyboard-focusable File workflow field");
        helpText.Should().NotBeNull("Backstage search should announce what it filters");
        LocalizedAttribute(searchBox, "AutomationProperties.Name").Should().Be("Search Recent Files");
        LocalizedAttribute(searchBox, "AutomationProperties.HelpText").Should().Be("Filter recent and pinned files");
    }

    [Fact]
    public void BackstageOpenProgressOverlay_ExposesAccessibleStatusText()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var overlay = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(x + "Name")?.Value == "OpenProgressOverlay");

        LocalizedAttribute(overlay, "AutomationProperties.Name").Should().Be("Opening workbook");
        LocalizedAttribute(overlay, "AutomationProperties.HelpText")
            .Should().Be("Shows workbook open progress and blocks workbook interaction until loading finishes or fails.");
        overlay.Attribute("Panel.ZIndex")?.Value.Should().Be("260");

        var progressBar = document
            .Descendants(presentation + "ProgressBar")
            .Single(element => element.Attribute(x + "Name")?.Value == "OpenProgressBar");

        LocalizedAttribute(progressBar, "AutomationProperties.Name").Should().Be("Opening Progress");
        progressBar.Attribute("Minimum")?.Value.Should().Be("0");
        progressBar.Attribute("Maximum")?.Value.Should().Be("100");

        var progressTexts = document
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute(x + "Name")?.Value is "OpenProgressTitle" or "OpenProgressDetail")
            .Select(element => LocalizedAttribute(element, "AutomationProperties.Name"))
            .ToList();

        progressTexts.Should().Equal("Open progress title", "Open progress detail");
    }

    [Fact]
    public void ShareCommandButtons_ArePresentedAsWindowsShareCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var shareButtons = document
            .Descendants(presentation + "Button")
            .Where(button =>
                button.Attribute("Click")?.Value is "ShareWorkbookBtn_Click" or "SsShareBtn_Click")
            .ToList();

        var shareButtonPlans = shareButtons
            .Select(button => new
            {
                Content = GetButtonText(button, presentation),
                Click = button.Attribute("Click")?.Value,
                KeyTip = button.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Title = LocalizedAttribute(button, local + "RibbonTooltip.Title"),
                Description = LocalizedAttribute(button, local + "RibbonTooltip.Description")
            })
            .ToList();

        shareButtonPlans.Select(button => button.Click)
            .Should().BeEquivalentTo(["ShareWorkbookBtn_Click", "SsShareBtn_Click"]);
        shareButtonPlans.Should().OnlyContain(button =>
            (button.Content == "Share" || button.Content == "Share Workbook") &&
            button.KeyTip == "SH" &&
            button.Title == button.Content &&
            button.Description == "Save the workbook if needed and open Windows Share for the file." &&
            !button.Description.Contains("Microsoft 365", StringComparison.OrdinalIgnoreCase) &&
            !button.Description.Contains("cloud", StringComparison.OrdinalIgnoreCase) &&
            !button.Description.Contains("coauthor", StringComparison.OrdinalIgnoreCase) &&
            !ContainsExcludedStatus(button.Content) &&
            !ContainsExcludedStatus(button.Title) &&
            !ContainsExcludedStatus(button.Description));
    }

    [Fact]
    public void ExternalTemplateEntryPoint_DisclosesExcludedStatusBeforeClick()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "SsMoreTemplatesBtn_Click")
            .Where(element =>
                !ContainsExcludedStatus(LocalizedAttribute(element, "Content")) &&
                !ContainsExcludedStatus(LocalizedAttribute(element, local + "RibbonTooltip.Title")) &&
                !ContainsExcludedStatus(LocalizedAttribute(element, local + "RibbonTooltip.Description")))
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
}
