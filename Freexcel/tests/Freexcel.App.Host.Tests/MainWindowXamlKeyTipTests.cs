using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowXamlKeyTipTests
{
    [Fact]
    public void BackstageInfoVersion_MatchesAboutDialogVersion()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute("Text")?.Value == AppInfo.VersionText)
            .Should()
            .ContainSingle("Backstage Info and About should show the same Freexcel version");
    }

    [Fact]
    public void BackstageInfo_DoesNotAdvertiseCloudDocumentManagement()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cloudCopy = document
            .Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value ?? element.Value)
            .Where(text =>
                text.Contains("check in", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("check out", StringComparison.OrdinalIgnoreCase))
            .ToList();

        cloudCopy.Should().BeEmpty("SharePoint-style check-in/out workflows are excluded from Freexcel");
    }

    [Fact]
    public void BackstageInfo_DoesNotAdvertiseDocumentInspector()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var inspectorCopy = document
            .Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value ?? element.Value)
            .Where(text =>
                text.Contains("hidden properties", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("personal information", StringComparison.OrdinalIgnoreCase))
            .ToList();

        inspectorCopy.Should().BeEmpty("Freexcel currently implements an accessibility checker, not Excel's full Document Inspector");
    }

    [Fact]
    public void BackstageAccountEntryPoint_DisclosesLocalAccountDecision()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var accountButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Content")?.Value == "Account");

        accountButton.Attribute("Click")?.Value.Should().Be("SsAccountBtn_Click");
        accountButton.Attribute(local + "RibbonTooltip.Title")?.Value.Should().Contain("Local");
        accountButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("AC");
        accountButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Microsoft account");
    }

    [Fact]
    public void BackstageExportEntryPoint_DisclosesXpsBackedPdfExport()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var exportButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Content")?.Value == "Export" &&
                element.Attribute("Click")?.Value == "ExportPdfButton_Click");

        exportButton.Attribute(local + "RibbonTooltip.Title")?.Value.Should().Be("Export PDF/XPS");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("XPS");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("PDF printer");
    }

    [Fact]
    public void ReviewShowComments_DisclosesDialogListBehavior()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showCommentsButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ReviewShowCommentsBtn_Click");

        showCommentsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("list");
        showCommentsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("hide");
        showCommentsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("indicators");
    }

    [Fact]
    public void ReviewCommentCommands_DiscloseSimpleCellNotesRatherThanThreadedComments()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var commentButtons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value is
                "ReviewNewCommentBtn_Click" or
                "ReviewDeleteCommentBtn_Click" or
                "ReviewShowCommentsBtn_Click")
            .ToList();

        var tooltipTexts = commentButtons
            .Select(element => new
            {
                Title = element.Attribute(local + "RibbonTooltip.Title")?.Value ?? "",
                Description = element.Attribute(local + "RibbonTooltip.Description")?.Value ?? ""
            })
            .ToList();

        tooltipTexts.Should().HaveCount(3);
        tooltipTexts.Should().OnlyContain(text =>
            text.Title.Contains("Note", StringComparison.OrdinalIgnoreCase) ||
            text.Description.Contains("note", StringComparison.OrdinalIgnoreCase));
        tooltipTexts
            .Single(text => text.Title.Equals("New Note", StringComparison.OrdinalIgnoreCase))
            .Description.Should()
            .Contain("threaded comments are not implemented");
    }

    [Fact]
    public void SpellingTooltip_DisclosesKnownCorrectionsBaseline()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var spellingButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SpellCheckBtn_Click");

        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("known misspellings");
        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("active sheet");
        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("proofing engine");
    }

    [Fact]
    public void AllowEditRangesTooltip_DisclosesAddRangePromptWorkflow()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var allowEditRangesButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "AllowEditRangesBtn_Click");

        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Add");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("range");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("manager");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("permissions");
    }

    [Fact]
    public void AltTextTooltip_DisclosesSelectedCellAnchoredObjectTarget()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var altTextButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SetAltTextBtn_Click");

        altTextButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("anchored at the selected cell");
    }

    [Fact]
    public void ArrangeAllTooltip_DisclosesStoredArrangementState()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var arrangeAllButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ArrangeAllPickerBtn_Click");

        arrangeAllButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Store");
        arrangeAllButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("arrangement");
        arrangeAllButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("multi-window hosting");
    }

    [Fact]
    public void ZoomToSelectionTooltip_DisclosesGridViewportFit()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var zoomSelectionButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ZoomSelectionBtn_Click");

        zoomSelectionButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("visible grid");
        zoomSelectionButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("screen");
    }

    [Fact]
    public void SplitTooltip_DisclosesFrozenPaneCleanup()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var splitButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SplitViewBtn_Click");

        splitButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("clears frozen panes");
    }

    [Fact]
    public void FreezePanesTooltip_DisclosesSplitPaneCleanup()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var freezePanesButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "FreezePanesPickerBtn_Click");

        freezePanesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("clears split panes");
    }

    [Fact]
    public void ProtectSheetTooltip_DisclosesSetProtectionWorkflow()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var protectSheetButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ProtectSheetBtn_Click");

        protectSheetButton.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value.Should().Be("ProtectSheetButton");
        protectSheetButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Set");
        protectSheetButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("locked cells");
        protectSheetButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("unwanted changes");
    }

    [Fact]
    public void ProtectWorkbookTooltip_DisclosesStructureProtectionWorkflow()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var protectWorkbookButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ProtectWorkbookBtn_Click");

        protectWorkbookButton.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value.Should().Be("ProtectWorkbookButton");
        protectWorkbookButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("structural changes");
        protectWorkbookButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("adding, deleting, or renaming sheets");
    }

    [Fact]
    public void TitledRibbonControls_HaveAltKeyTips()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";

        var missing = document
            .Descendants()
            .Where(element => element.Attribute(local + "RibbonTooltip.Title") is not null)
            .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(element => element.Attribute(local + "RibbonTooltip.Title")?.Value ?? element.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible titled ribbon controls should participate in Excel-style Alt keytip navigation");
    }

    [Fact]
    public void RibbonTabs_DoNotReuseCommandKeyTipsWithinTheSameTab()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
                tab.Descendants()
                    .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .GroupBy(element => element.Attribute(local + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{tab.Attribute("Header")?.Value ?? "Tab"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("unique per-tab keytips are required for deterministic Excel-style command routing");
    }

    [Fact]
    public void KeyedRibbonDropDowns_HaveKeyTipsForDirectMenuItems()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .SelectMany(button => button
                .Descendants(presentation + "ContextMenu")
                .Elements(presentation + "MenuItem")
                .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is null)
                .Select(menuItem =>
                    $"{button.Attribute(local + "RibbonTooltip.Title")?.Value}:{menuItem.Attribute("Header")?.Value}"))
            .ToList();

        missing.Should().BeEmpty("audited ribbon dropdown menus should be reachable through staged Alt keytips");
    }

    [Fact]
    public void AllContextMenuCommands_HaveKeyTipsForDirectMenuItems()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ContextMenu")
            .Elements(presentation + "MenuItem")
            .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(menuItem => menuItem.Attribute("Header")?.Value ?? "MenuItem")
            .ToList();

        missing.Should().BeEmpty("every command surfaced through a context menu should have deterministic keyboard access metadata");
    }

    [Fact]
    public void BackstageCommandButtons_HaveAltKeyTips()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var startScreen = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "StartScreenOverlay");

        var missing = startScreen
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click") is not null)
            .Where(button => button.Attribute("Click")?.Value != "SsRecentItem_Click")
            .Where(button => button.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(button =>
                button.Attribute("Content")?.Value ??
                button.Attribute(x + "Name")?.Value ??
                button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("File/Backstage commands should be reachable through Excel-style Alt keytips");
    }

    [Fact]
    public void BackstageMouseOnlyCommands_AreNotUsedForRecentPinnedTabs()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
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
            .Select(button => button.Attribute("Content")?.Value ?? button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("Recent/Pinned Backstage tab selectors should participate in keytip navigation");
    }

    [Fact]
    public void BackstageCommands_DoNotReuseKeyTips()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
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
    public void StatusBarZoomCommandButtons_HaveAltKeyTips()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "ZoomOutBtn_Click" or "ZoomInBtn_Click")
            .Where(button => button.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(button => button.Attribute("Content")?.Value ?? button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("status-bar zoom commands should participate in the visible command keytip contract");
    }

    [Fact]
    public void RibbonCheckBoxCommands_HaveTooltipTitlesDescriptionsAndKeyTips()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "CheckBox")
            .Where(checkBox =>
                checkBox.Attribute("Click") is not null ||
                checkBox.Attribute("Checked") is not null ||
                checkBox.Attribute("Unchecked") is not null)
            .Where(checkBox =>
                checkBox.Attribute(local + "RibbonTooltip.Title") is null ||
                checkBox.Attribute(local + "RibbonTooltip.Description") is null ||
                checkBox.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(checkBox => checkBox.Attribute("Content")?.Value ?? checkBox.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible ribbon checkbox commands should expose the same Excel-style tooltip and keytip metadata as button commands");
    }

    [Fact]
    public void RibbonComboBoxCommands_HaveAccessibleNamesMatchingTooltipTitles()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ComboBox")
            .Where(comboBox => comboBox.Attribute(local + "RibbonTooltip.Title") is not null)
            .Where(comboBox =>
                comboBox.Attribute("AutomationProperties.Name")?.Value !=
                comboBox.Attribute(local + "RibbonTooltip.Title")!.Value)
            .Select(comboBox => comboBox.Attribute(local + "RibbonTooltip.Title")!.Value)
            .ToList();

        missing.Should().BeEmpty("focusable ribbon combo box commands should announce the same command name shown in Excel-style tooltips");
    }

    [Fact]
    public void NonRibbonTooltipClickButtons_HaveAccessibleNames()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click") is not null)
            .Where(button => button.Attribute(local + "RibbonTooltip.Title") is null)
            .Where(button => button.Attribute("AutomationProperties.Name") is null)
            .Select(button =>
                button.Attribute(x + "Name")?.Value ??
                button.Attribute("Content")?.Value ??
                button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("clickable buttons outside the ribbon-tooltip command system should still have accessible names");
    }

    [Fact]
    public void StatusBarZoomSlider_HasAccessibleRangeMetadata()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var zoomSlider = document
            .Descendants(presentation + "Slider")
            .Single(slider => slider.Attribute(x + "Name")?.Value == "ZoomSlider");

        var name = zoomSlider.Attribute("AutomationProperties.Name");
        var helpText = zoomSlider.Attribute("AutomationProperties.HelpText");
        var tooltip = zoomSlider.Attribute("ToolTip");

        name.Should().NotBeNull("the keyboard-focusable zoom slider needs a screen-reader name");
        helpText.Should().NotBeNull("the zoom slider should disclose the Excel-style zoom range");
        tooltip.Should().NotBeNull("the zoom slider should expose a standard pointer tooltip");

        name!.Value.Should().Be("Zoom Slider");
        helpText!.Value.Should().Contain("10%").And.Contain("400%");
        tooltip!.Value.Should().Be("Zoom");
    }

    [Theory]
    [InlineData("CellAddressBox", "Name Box", "Go to a cell or named range")]
    [InlineData("FormulaBar", "Formula Bar", "Edit the active cell value or formula")]
    public void FormulaBarTextFields_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedName,
        string expectedHelpText)
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var textBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == controlName);

        var name = textBox.Attribute("AutomationProperties.Name");
        var helpText = textBox.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("formula bar text fields are keyboard-focusable Excel surface controls");
        helpText.Should().NotBeNull("formula bar text fields should announce their workflow role");
        name!.Value.Should().Be(expectedName);
        helpText!.Value.Should().Be(expectedHelpText);
    }

    [Fact]
    public void BackstageSearchBox_HasAccessibleNameAndHelpText()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "SsSearchBox");

        var name = searchBox.Attribute("AutomationProperties.Name");
        var helpText = searchBox.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("Backstage search is a keyboard-focusable File workflow field");
        helpText.Should().NotBeNull("Backstage search should announce what it filters");
        name!.Value.Should().Be("Search Recent Files");
        helpText!.Value.Should().Be("Filter recent and pinned files");
    }

    [Theory]
    [InlineData("VerticalScroll", "Vertical Worksheet Scroll Bar", "Scroll worksheet rows")]
    [InlineData("HorizontalScroll", "Horizontal Worksheet Scroll Bar", "Scroll worksheet columns")]
    public void WorksheetScrollBars_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedName,
        string expectedHelpText)
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollBar = document
            .Descendants(presentation + "ScrollBar")
            .Single(element => element.Attribute(x + "Name")?.Value == controlName);

        var name = scrollBar.Attribute("AutomationProperties.Name");
        var helpText = scrollBar.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("worksheet scrollbars are keyboard-focusable Excel surface controls");
        helpText.Should().NotBeNull("worksheet scrollbars should announce whether they move rows or columns");
        name!.Value.Should().Be(expectedName);
        helpText!.Value.Should().Be(expectedHelpText);
    }

    [Fact]
    public void NestedRibbonMenuItems_HaveStagedKeyTips()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "MenuItem")
            .Where(menuItem => menuItem.Descendants(presentation + "MenuItem").Any())
            .SelectMany(menuItem => menuItem
                .Elements(presentation + "MenuItem")
                .Where(child => child.Attribute(local + "RibbonTooltip.KeyTip") is null)
                .Select(child => $"{menuItem.Attribute("Header")?.Value}:{child.Attribute("Header")?.Value}"))
            .ToList();

        missing.Should().BeEmpty("nested ribbon menu choices should be reachable through staged Alt keytips");
    }

    [Fact]
    public void RibbonMenus_DoNotReuseKeyTipsWithinTheSameMenu()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "ContextMenu")
            .Concat(document.Descendants(presentation + "MenuItem")
                .Where(menuItem => menuItem.Elements(presentation + "MenuItem").Any()))
            .SelectMany(menu =>
                menu.Elements(presentation + "MenuItem")
                    .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .GroupBy(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{menu.Attribute("Header")?.Value ?? "ContextMenu"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("menu-level keytips must be unique for deterministic staged Alt routing");
    }

    [Fact]
    public void DeferredCommandButtons_DescribeDeferredStatusInTooltip()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button =>
                button.Attribute("Click")?.Value is "PageLayoutDeferredBtn_Click" or "ViewWindowDeferredBtn_Click")
            .Where(button =>
                button.Attribute(local + "RibbonTooltip.Description")?.Value.Contains("Deferred:", StringComparison.OrdinalIgnoreCase) != true)
            .Select(button => button.Attribute(local + "RibbonTooltip.Title")?.Value ?? button.Attribute("Content")?.Value ?? "Button")
            .ToList();

        missing.Should().BeEmpty("deferred visible commands should clearly say they are deferred before the user clicks");
    }

    [Fact]
    public void PageLayoutThemesButton_OpensWorkbookThemeMenu()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var themesButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Themes");

        themesButton.Attribute("Click")?.Value.Should().Be("ThemeBtn_Click");
        themesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        themesButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Freexcel Colorful", "Grayscale", "Customize...");
        themesButton.Descendants(presentation + "MenuItem")
            .Single(item => item.Attribute("Header")?.Value == "Customize...")
            .Attribute("Click")?.Value.Should().Be("ThemeCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeColorsButton_OpensColorSchemeMenu()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var colorsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Theme Colors");

        colorsButton.Attribute("Click")?.Value.Should().Be("ThemeColorsBtn_Click");
        colorsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        colorsButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Freexcel Colorful", "Grayscale");
    }

    [Fact]
    public void PageLayoutThemeFontsButton_OpensFontPairMenu()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var fontsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Theme Fonts");

        fontsButton.Attribute("Click")?.Value.Should().Be("ThemeFontsBtn_Click");
        fontsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        fontsButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Arial", "Times New Roman");
    }

    [Fact]
    public void PageLayoutThemeEffectsButton_OpensEffectSetMenu()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var effectsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Theme Effects");

        effectsButton.Attribute("Click")?.Value.Should().Be("ThemeEffectsBtn_Click");
        effectsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        effectsButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Subtle", "Refined");
    }

    [Fact]
    public void ShareCommandButtons_DiscloseExcludedStatusBeforeClick()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button =>
                button.Attribute("Click")?.Value is "ShareWorkbookBtn_Click" or "SsShareBtn_Click")
            .Where(button =>
                !ContainsExcludedStatus(button.Attribute("Content")?.Value) &&
                !ContainsExcludedStatus(button.Attribute(local + "RibbonTooltip.Title")?.Value) &&
                !ContainsExcludedStatus(button.Attribute(local + "RibbonTooltip.Description")?.Value))
            .Select(button => button.Attribute("Content")?.Value ?? button.Attribute("x:Name")?.Value ?? "Button")
            .ToList();

        missing.Should().BeEmpty("Share is an explicit cloud/collaboration exclusion and should not look like a normal parity command");
    }

    [Fact]
    public void ExternalTemplateEntryPoint_DisclosesExcludedStatusBeforeClick()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "SsMoreTemplatesBtn_Click")
            .Where(element =>
                !ContainsExcludedStatus(element.Attribute("Content")?.Value) &&
                !ContainsExcludedStatus(element.Attribute(local + "RibbonTooltip.Title")?.Value) &&
                !ContainsExcludedStatus(element.Attribute(local + "RibbonTooltip.Description")?.Value))
            .Select(element => element.Attribute("Content")?.Value ?? element.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("online template discovery depends on an external Microsoft service and should not look like a normal local command");

        document
            .Descendants()
            .Any(element => element.Attribute("MouseDown")?.Value == "SsMoreTemplates_MouseDown")
            .Should().BeFalse("online template discovery should be a normal command button, not a mouse-only text element");
    }

    [Fact]
    public void PivotTableEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotTableBtn_Click")
            .ToList();

        buttons.Should().ContainSingle();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Create");
    }

    [Fact]
    public void PivotTableRefreshEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "RefreshPivotTableBtn_Click")
            .ToList();

        buttons.Should().ContainSingle();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Refresh");
    }

    [Fact]
    public void PivotTableShowDetailsEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotTableShowDetailsBtn_Click")
            .ToList();

        buttons.Should().ContainSingle();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("detail");
    }

    [Fact]
    public void PivotTableShowDetailsGesture_IsAttemptedBeforeDoubleClickEdit()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml.cs"));

        source.Should().Contain("e.ClickCount == 2");
        source.Should().Contain("TryShowPivotTableDetails(showMessage: false)");
    }

    [Fact]
    public void PivotChartEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(FindWorkspaceFile("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotChartBtn_Click")
            .ToList();

        buttons.Should().ContainSingle();
        buttons[0].Attribute("Content")?.Value.Should().Contain("PivotChart");
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("PivotTable");
    }

    private static bool ContainsExcludedStatus(string? value) =>
        value?.Contains("excluded", StringComparison.OrdinalIgnoreCase) == true;

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
