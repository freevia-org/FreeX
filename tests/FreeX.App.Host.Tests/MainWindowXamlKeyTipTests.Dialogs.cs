using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
    [Fact]
    public void DialogEntryPointButtons_HaveStableAutomationIds()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var expected = new Dictionary<string, string>
        {
            ["InsertFunctionBtn_Click"] = "FormulasInsertFunctionButton",
            ["SsAccountBtn_Click"] = "BackstageAccountButton",
            ["SsOptionsBtn_Click"] = "BackstageOptionsButton",
            ["HelpOnlineBtn_Click"] = "HelpOnlineButton",
            ["CheckForUpdatesBtn_Click"] = "HelpCheckForUpdatesButton",
            ["SendFeedbackBtn_Click"] = "HelpFeedbackButton",
            ["AboutBtn_Click"] = "HelpAboutFreeXButton",
            ["LegalNoticesBtn_Click"] = "HelpLegalNoticesButton",
        };

        foreach (var (clickHandler, automationId) in expected)
        {
            var matchingAutomationIds = document
                .Descendants()
                .Where(element => element.Attribute("Click")?.Value == clickHandler)
                .Select(element => element.ToString())
                .ToList();

            matchingAutomationIds.Should().Contain(element => element.Contains($"AutomationProperties.AutomationId=\"{automationId}\""));
        }

        var automationInvokeButtonMarkup = document
            .Descendants(local + "AutomationInvokeButton")
            .Select(element => element.ToString())
            .ToList();

        foreach (var automationId in expected.Values)
            automationInvokeButtonMarkup.Should().Contain(element => element.Contains($"AutomationProperties.AutomationId=\"{automationId}\""));
    }

    [Fact]
    public void HelpExternalEntryPoints_ExposeStableAutomationAndHonestHelpText()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var helpOnline = document
            .Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "HelpOnlineButton");
        var feedback = document
            .Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "HelpFeedbackButton");
        var updates = document
            .Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "HelpCheckForUpdatesButton");

        helpOnline.Attribute("Click")?.Value.Should().Be("HelpOnlineBtn_Click");
        helpOnline.ToString().Should().Contain("AutomationProperties.AutomationId=\"HelpOnlineButton\"");
        LocalizedAttribute(helpOnline, "AutomationProperties.HelpText").Should().Be("Open the FreeX help documentation in a web browser.");
        helpOnline.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("HO");

        updates.Attribute("Click")?.Value.Should().Be("CheckForUpdatesBtn_Click");
        updates.ToString().Should().Contain("AutomationProperties.AutomationId=\"HelpCheckForUpdatesButton\"");
        LocalizedAttribute(updates, "AutomationProperties.HelpText").Should().Be("Open the latest FreeX tester release in a web browser.");
        updates.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("UP");

        feedback.Attribute("Click")?.Value.Should().Be("SendFeedbackBtn_Click");
        feedback.ToString().Should().Contain("AutomationProperties.AutomationId=\"HelpFeedbackButton\"");
        LocalizedAttribute(feedback, "AutomationProperties.HelpText").Should().Be("Open a prefilled GitHub issue with safe app diagnostics.");
        feedback.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("FE");
    }

    [Fact]
    public void DialogEntryPointHandlers_UseOwnedActivatedDialogs()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"))!;
        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(appHostDirectory, "MainWindow*.cs")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
        var invokeButtonSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "AutomationInvokeButton.cs"));

        source.Should().Contain("ShowOwnedDialog(");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("var dlg = new InsertFunctionDialog");
        source.Should().Contain("var dlg = new OptionsDialog");
        source.Should().Contain("ShowOwnedDialog(dlg)");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("AppInfo.AboutText");
        source.Should().Contain("var dialog = new LegalNoticesDialog();");
        source.Should().Contain("ShowOwnedDialog(dialog);");
        invokeButtonSource.Should().Contain("IInvokeProvider");
        invokeButtonSource.Should().Contain("Dispatcher.BeginInvoke");
        invokeButtonSource.Should().Contain("ButtonBase.ClickEvent");
    }

    [Fact]
    public void ReviewShowComments_DisclosesDialogListBehavior()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showCommentsButton = document
            .Descendants(presentation + "Button")
            .Single(element => string.Equals(
                element.Attribute(local + "RibbonMetadata.CommandName")?.Value,
                "Show Comments",
                StringComparison.Ordinal));

        LocalizedAttribute(showCommentsButton, local + "RibbonTooltip.Description").Should().Contain("list");
        LocalizedAttribute(showCommentsButton, local + "RibbonTooltip.Description").Should().NotContain("hide");
        LocalizedAttribute(showCommentsButton, local + "RibbonTooltip.Description").Should().NotContain("indicators");
    }

    [Fact]
    public void ReviewCommentCommands_ExposeThreadedCommentsAndSimpleNotesDistinctly()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var commentButtons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value is
                "ReviewNewThreadedCommentBtn_Click" or
                "ReviewDeleteThreadedCommentBtn_Click" or
                "ReviewNewCommentBtn_Click" or
                "ReviewDeleteCommentBtn_Click" or
                "ReviewPrevCommentBtn_Click" or
                "ReviewNextCommentBtn_Click" or
                "ReviewShowCommentsBtn_Click" or
                "ReviewPrevNoteBtn_Click" or
                "ReviewNextNoteBtn_Click" or
                "ReviewShowNotesBtn_Click")
            .ToList();

        var tooltipTexts = commentButtons
            .Select(element => new
            {
                Title = LocalizedAttribute(element, local + "RibbonTooltip.Title") ?? "",
                Description = LocalizedAttribute(element, local + "RibbonTooltip.Description") ?? ""
            })
            .ToList();

        tooltipTexts.Should().HaveCount(11);
        tooltipTexts
            .Single(text => text.Title.Equals("New Comment", StringComparison.OrdinalIgnoreCase))
            .Description.Should().Contain("threaded comment");
        tooltipTexts
            .Where(text => text.Title.Contains("Comment", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(text => text.Description.Contains("comment", StringComparison.OrdinalIgnoreCase));
        tooltipTexts
            .Where(text => text.Title.Contains("Note", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(text => text.Description.Contains("note", StringComparison.OrdinalIgnoreCase));
        tooltipTexts.Select(text => text.Description)
            .Should().NotContain(description => description.Contains("threaded comments are not implemented", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InsertCommentCommand_ReusesThreadedCommentWorkflow()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.InsertCommands.cs"));

        var insertCommentButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "InsertCommentBtn_Click");

        LocalizedAttribute(insertCommentButton, local + "RibbonTooltip.Title").Should().Be("Comment");
        LocalizedAttribute(insertCommentButton, local + "RibbonTooltip.Description").Should().Contain("threaded comment");
        LocalizedAttribute(insertCommentButton, local + "RibbonTooltip.Description").Should().NotContain("not implemented");
        source.Should().Contain("private void InsertCommentBtn_Click(object sender, RoutedEventArgs e) => ReviewNewThreadedCommentBtn_Click(sender, e);");
    }

    [Fact]
    public void SpellingTooltip_DisclosesKnownCorrectionsBaseline()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var spellingButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SpellCheckBtn_Click");

        LocalizedAttribute(spellingButton, local + "RibbonTooltip.Description").Should().Contain("known misspellings");
        LocalizedAttribute(spellingButton, local + "RibbonTooltip.Description").Should().Contain("threaded comments");
        LocalizedAttribute(spellingButton, local + "RibbonTooltip.Description").Should().Contain("replace all");
        LocalizedAttribute(spellingButton, local + "RibbonTooltip.Description").Should().NotContain("proofing engine");
    }

    [Fact]
    public void AccessibilityTooltip_DisclosesCurrentCheckerCoverage()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var accessibilityButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "AccessibilityCheckerBtn_Click");

        var description = LocalizedAttribute(accessibilityButton, local + "RibbonTooltip.Description");
        description.Should().Contain("merged cells");
        description.Should().Contain("blank table headers");
        description.Should().Contain("alternate text");
        description.Should().Contain("charts without titles");
    }

    [Fact]
    public void ReviewProofingEntryPoints_ExposeStableAutomationMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var statisticsButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "WorkbookStatisticsBtn_Click");
        var accessibilityButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "AccessibilityCheckerBtn_Click");

        statisticsButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"ReviewWorkbookStatisticsButton\"");
        LocalizedAttribute(statisticsButton, "AutomationProperties.HelpText").Should().Be("Show workbook counts for sheets, cells, formulas, comments, and objects.");
        statisticsButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("W");

        accessibilityButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"ReviewAccessibilityCheckerButton\"");
        LocalizedAttribute(accessibilityButton, "AutomationProperties.HelpText").Should().Be("Find merged cells, blank table headers, objects missing alternate text, and charts without titles.");
        accessibilityButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("CA");
    }

    [Fact]
    public void AllowEditRangesTooltip_DisclosesRangeManagerWorkflow()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var allowEditRangesButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "AllowEditRangesBtn_Click");

        allowEditRangesButton.Attribute("Name")?.Value.Should().Be("AllowEditRangesButton");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("AR");
        LocalizedAttribute(allowEditRangesButton, local + "RibbonTooltip.Description").Should().Contain("Add");
        LocalizedAttribute(allowEditRangesButton, local + "RibbonTooltip.Description").Should().Contain("delete");
        LocalizedAttribute(allowEditRangesButton, local + "RibbonTooltip.Description").Should().Contain("clear");
        LocalizedAttribute(allowEditRangesButton, local + "RibbonTooltip.Description").Should().Contain("ranges");
        LocalizedAttribute(allowEditRangesButton, local + "RibbonTooltip.Description").Should().NotContain("permissions");
    }

    [Fact]
    public void AltTextTooltip_DisclosesSelectedCellAnchoredObjectTarget()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var altTextButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SetAltTextBtn_Click");

        LocalizedAttribute(altTextButton, local + "RibbonTooltip.Description").Should().Contain("anchored at the selected cell");
    }

    [Fact]
    public void ArrangeAllTooltip_DisclosesStoredArrangementState()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var arrangeAllButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ArrangeAllPickerBtn_Click");

        LocalizedAttribute(arrangeAllButton, local + "RibbonTooltip.Description").Should().Contain("Store");
        LocalizedAttribute(arrangeAllButton, local + "RibbonTooltip.Description").Should().Contain("arrangement");
        LocalizedAttribute(arrangeAllButton, local + "RibbonTooltip.Description").Should().Contain("multi-window hosting");
    }

    [Fact]
    public void ZoomToSelectionTooltip_DisclosesGridViewportFit()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var zoomSelectionButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ZoomSelectionBtn_Click");

        LocalizedAttribute(zoomSelectionButton, local + "RibbonTooltip.Description").Should().Contain("visible grid");
        LocalizedAttribute(zoomSelectionButton, local + "RibbonTooltip.Description").Should().NotContain("screen");
    }

    [Fact]
    public void SplitTooltip_DisclosesFrozenPaneCleanup()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var splitButton = document
            .Descendants(presentation + "ToggleButton")
            .Single(element => element.Attribute("Click")?.Value == "SplitViewBtn_Click");

        LocalizedAttribute(splitButton, local + "RibbonTooltip.Description").Should().Contain("clears frozen panes");
    }

    [Fact]
    public void FreezePanesTooltip_DisclosesSplitPaneCleanup()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var freezePanesButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "FreezePanesPickerBtn_Click");

        LocalizedAttribute(freezePanesButton, local + "RibbonTooltip.Description").Should().Contain("clears split panes");
    }

    [Fact]
    public void ProtectSheetTooltip_DisclosesSetProtectionWorkflow()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var protectSheetButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ProtectSheetBtn_Click");

        protectSheetButton.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value.Should().Be("ProtectSheetButton");
        LocalizedAttribute(protectSheetButton, local + "RibbonTooltip.Description").Should().Contain("Set");
        LocalizedAttribute(protectSheetButton, local + "RibbonTooltip.Description").Should().Contain("locked cells");
        LocalizedAttribute(protectSheetButton, local + "RibbonTooltip.Description").Should().NotContain("unwanted changes");
    }

    [Fact]
    public void ProtectWorkbookTooltip_DisclosesStructureProtectionWorkflow()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var protectWorkbookButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ProtectWorkbookBtn_Click");

        protectWorkbookButton.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value.Should().Be("ProtectWorkbookButton");
        LocalizedAttribute(protectWorkbookButton, local + "RibbonTooltip.Description").Should().Contain("structural changes");
        LocalizedAttribute(protectWorkbookButton, local + "RibbonTooltip.Description").Should().Contain("adding, deleting, or renaming sheets");
    }

    [Fact]
    public void ErrorCheckingButton_ExposesOptionsEntryPoint()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var errorCheckingButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Error Checking");

        var menuItems = errorCheckingButton
            .Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = LocalizedAttribute(item, "Header"),
                KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Click = item.Attribute("Click")?.Value
            })
            .ToList();

        menuItems.Should().Contain(item =>
            item.Header == "Error Checking..." &&
            item.KeyTip == "E" &&
            item.Click == "ErrorCheckBtn_Click");
        menuItems.Should().Contain(item =>
            item.Header == "Error Checking Options..." &&
            item.KeyTip == "O" &&
            item.Click == "SsOptionsBtn_Click");
    }

    [Fact]
    public void DeferredCommandButtons_DescribeDeferredStatusInTooltip()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants()
            .Where(element => element.Name == presentation + "Button" || element.Name == presentation + "ToggleButton")
            .Where(button => button.Attribute("Click")?.Value == "PageLayoutDeferredBtn_Click")
            .Where(button =>
                LocalizedAttribute(button, local + "RibbonTooltip.Description")?.Contains("Deferred:", StringComparison.OrdinalIgnoreCase) != true)
            .Select(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") ?? LocalizedAttribute(button, "Content") ?? "Button")
            .ToList();

        missing.Should().BeEmpty("deferred visible commands should clearly say they are deferred before the user clicks");
    }
}
