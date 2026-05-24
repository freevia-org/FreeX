using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowXamlKeyTipTests
{
    [Fact]
    public void RibbonSurface_IsReachableByKeyboardTabTraversal()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace keyboardNavigation = "clr-namespace:System.Windows.Input;assembly=PresentationFramework";

        var ribbonTabs = document
            .Descendants(presentation + "TabControl")
            .Single(element => element.Attribute(x + "Name")?.Value == "RibbonTabs");

        ribbonTabs.Attribute("Focusable")?.Value.Should().Be("True");
        ribbonTabs.Attribute("IsTabStop")?.Value.Should().Be("True");
        ribbonTabs.Attribute(keyboardNavigation + "KeyboardNavigation.TabNavigation")?.Value.Should().Be("Continue");
        ribbonTabs.Attribute(keyboardNavigation + "KeyboardNavigation.ControlTabNavigation")?.Value.Should().Be("Continue");
        ribbonTabs.Attribute(keyboardNavigation + "KeyboardNavigation.DirectionalNavigation")?.Value.Should().Be("Contained");
    }

    [Fact]
    public void RibbonCommandStyles_PreserveKeyboardFocusStops()
    {
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Resources", "MainWindowResources.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var styles = resources
            .Descendants(presentation + "Style")
            .Where(style =>
                (style.Attribute(x + "Key")?.Value is "RibbonBtn" or "RibbonToggleBtn") ||
                style.Attribute("TargetType")?.Value == "TabItem")
            .ToList();

        styles.Should().HaveCount(3);
        styles.Should().OnlyContain(style =>
            style.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Focusable" &&
                (string?)setter.Attribute("Value") == "True"));
        styles.Should().OnlyContain(style =>
            style.Elements(presentation + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "IsTabStop" &&
                (string?)setter.Attribute("Value") == "True"));
    }

    [Fact]
    public void RibbonKeyboardFocus_IsNotHijackedByWorksheetNavigation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardFocus.cs"));

        const string callSite = "if (TryHandleFocusedRibbonKeyboardNavigation(e))";

        source.Should().Contain(callSite);
        keyboardFocusSource.Should().Contain("private bool TryHandleFocusedRibbonKeyboardNavigation(System.Windows.Input.KeyEventArgs e)");
        var callIndex = source.IndexOf(callSite, StringComparison.Ordinal);
        var gridNavigationIndex = source.IndexOf("if (SheetGrid.SelectedRange == null) return;", callIndex, StringComparison.Ordinal);

        gridNavigationIndex.Should().BeGreaterThan(callIndex);
        callIndex
            .Should()
            .BeLessThan(gridNavigationIndex);
    }

    [Fact]
    public void F6ShellFocusCycle_IsHandledBeforeTextBoxPreviewKeyFiltering()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardFocus.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardCommands.cs"));

        const string previewHandler = "private void MainWindow_PreviewKeyDown";
        const string f6PreviewCall = "if (TryHandleShellFocusCyclePreview(e))";
        var previewHandlerIndex = selectionSource.IndexOf(previewHandler, StringComparison.Ordinal);
        var f6Index = selectionSource.IndexOf(f6PreviewCall, previewHandlerIndex, StringComparison.Ordinal);
        var textBoxFilterIndex = selectionSource.IndexOf(
            "if (Keyboard.FocusedElement is TextBox or ComboBox)",
            previewHandlerIndex,
            StringComparison.Ordinal);

        previewHandlerIndex.Should().BeGreaterThanOrEqualTo(0);
        f6Index.Should().BeGreaterThanOrEqualTo(0);
        textBoxFilterIndex.Should().BeGreaterThanOrEqualTo(0);
        f6Index.Should().BeLessThan(textBoxFilterIndex);
        commandSource.Should().Contain("KeyboardCommandShortcut.CycleShellFocus");
        keyboardFocusSource.Should().Contain("FocusShellRegion(");
    }

    [Fact]
    public void F6ShellFocusCycle_ContinuesWhenRegionRejectsFocus()
    {
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardFocus.cs"));

        keyboardFocusSource.Should().Contain("if (FocusShellRegion(current))");
        keyboardFocusSource.Should().Contain("return FormulaBar.Focus();");
        keyboardFocusSource.Should().Contain("return TryFocusCurrentSheetTab() || AddSheetButton.Focus();");
        keyboardFocusSource.Should().Contain("return FocusStatusBar();");
    }

    [Fact]
    public void BackstageSidebarButtons_RenderAccessKeyMarkersAsMnemonics()
    {
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "Resources", "MainWindowResources.xaml"));
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
    public void BackstageSaveAsButton_UsesAccessKeyMatchingKeyTip()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:Freexcel.App.Host";

        var saveAsButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SaveAsButton_Click");

        saveAsButton.Attribute("Content")?.Value.Should().Be("Save _As");
        saveAsButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("A");
    }

    [Fact]
    public void BackstageInfoVersion_MatchesAboutDialogVersion()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
    public void BackstageInfo_ShowsFormulaErrorSummary()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        document
            .Descendants(presentation + "TextBlock")
            .Select(element => element.Attribute("Text")?.Value)
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var visibleButtons = document
            .Descendants(presentation + "Button")
            .Select(element => element.Attribute("Click")?.Value)
            .ToList();

        visibleButtons.Should().Contain("SsPinItem_Click", "pinning should not be hidden behind a context menu");
        visibleButtons.Should().Contain("SsUnpinItem_Click", "pinned files need a visible unpin affordance");
    }

    [Fact]
    public void ConditionalFormattingTopBottomRules_ExposeExcelParityMenuChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var menuItems = document
            .Descendants(presentation + "MenuItem")
            .Select(element => new
            {
                Header = element.Attribute("Header")?.Value,
                Click = element.Attribute("Click")?.Value
            })
            .ToList();

        menuItems.Should().Contain(item => item.Header == "Top 10%..." && item.Click == "CfTop10PercentMenuItem_Click");
        menuItems.Should().Contain(item => item.Header == "Bottom 10%..." && item.Click == "CfBottom10PercentMenuItem_Click");
        menuItems.Should().Contain(item => item.Header == "Below Average..." && item.Click == "CfBelowAvgMenuItem_Click");
    }

    [Fact]
    public void DataTab_ExposesFlashFillCommand()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var dataTab = document
            .Descendants(presentation + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "Data");

        var flashFillButton = dataTab
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(local + "RibbonTooltip.Title")?.Value == "Flash Fill");

        flashFillButton.Attribute("Click")?.Value.Should().Be("FlashFillMenuItem_Click");
        flashFillButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("FF");
        flashFillButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("examples");
    }

    [Fact]
    public void BackstageAccountEntryPoint_DisclosesLocalAccountDecision()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var accountButton = document
            .Descendants()
            .Single(element => element.Attribute("Content")?.Value == "Account");

        accountButton.Attribute(x + "Name")?.Value.Should().Be("SsAccountNavBtn");
        accountButton.Attribute("Click")?.Value.Should().Be("SsAccountBtn_Click");
        accountButton.ToString().Should().Contain("AutomationProperties.Name=\"Account\"");
        accountButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"BackstageAccountButton\"");
        accountButton.ToString().Should().Contain("AutomationProperties.HelpText=\"Show local account information");
        accountButton.Attribute("IsTabStop")?.Value.Should().Be("True");
        accountButton.Attribute(local + "RibbonTooltip.Title")?.Value.Should().Contain("Local");
        accountButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("AC");
        accountButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Microsoft account");
    }

    [Fact]
    public void BackstageOptionsEntryPoint_IsNamedCommandForUiAutomation()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var optionsButton = document
            .Descendants()
            .Single(element => element.Attribute(x + "Name")?.Value == "SsOptionsNavBtn");

        optionsButton.Attribute("Click")?.Value.Should().Be("SsOptionsBtn_Click");
        optionsButton.ToString().Should().Contain("AutomationProperties.Name=\"Options\"");
        optionsButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"BackstageOptionsButton\"");
        optionsButton.ToString().Should().Contain("AutomationProperties.HelpText=\"Open Freexcel settings");
        optionsButton.Attribute("IsTabStop")?.Value.Should().Be("True");
    }

    [Fact]
    public void DialogEntryPointButtons_HaveStableAutomationIds()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:Freexcel.App.Host";

        var expected = new Dictionary<string, string>
        {
            ["InsertFunctionBtn_Click"] = "FormulasInsertFunctionButton",
            ["SsAccountBtn_Click"] = "BackstageAccountButton",
            ["SsOptionsBtn_Click"] = "BackstageOptionsButton",
            ["AboutBtn_Click"] = "HelpAboutFreexcelButton",
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
    public void DialogEntryPointHandlers_UseOwnedActivatedDialogs()
    {
        var appHostDirectory = Path.GetDirectoryName(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"))!;
        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(appHostDirectory, "MainWindow*.cs")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
        var invokeButtonSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "AutomationInvokeButton.cs"));

        source.Should().Contain("ShowOwnedDialog(");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("var dlg = new InsertFunctionDialog");
        source.Should().Contain("var dlg = new OptionsDialog");
        source.Should().Contain("ShowOwnedDialog(dlg)");
        source.Should().Contain("ShowOwnedMessage(");
        source.Should().Contain("AppInfo.AboutText");
        invokeButtonSource.Should().Contain("IInvokeProvider");
        invokeButtonSource.Should().Contain("Dispatcher.BeginInvoke");
        invokeButtonSource.Should().Contain("ButtonBase.ClickEvent");
    }

    [Fact]
    public void MainWindowPreviewKeys_HandleWorksheetKeytipAndContextMenuEntryPoints()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.KeyboardCommands.cs"));

        source.Should().Contain("this.PreviewKeyDown += MainWindow_PreviewKeyDown;");
        source.Should().Contain("KeyboardCommandShortcut.ShowKeyTips");
        source.Should().Contain("KeyboardCommandShortcut.OpenContextMenu");
    }

    [Fact]
    public void EscapeFromVisibleBackstage_ReturnsToWorkbookBeforeTransientCancellation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs"));

        source.Should().Contain("IsStartScreenVisible()");
        source.Should().Contain("HideStartScreen();");
        source.IndexOf("HideStartScreen();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(source.IndexOf("CancelCopyAndTransientModes();", StringComparison.Ordinal));
    }

    [Fact]
    public void BackstageExportEntryPoint_DisclosesRealPdfAndXpsExport()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var exportButton = document
            .Descendants(presentation + "Button")
            .Single(element =>
                element.Attribute("Content")?.Value == "Export" &&
                element.Attribute("Click")?.Value == "ExportPdfButton_Click");

        exportButton.Attribute(local + "RibbonTooltip.Title")?.Value.Should().Be("Export PDF/XPS");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("PDF");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("XPS");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("selection");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("workbook");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("active sheet");
        exportButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("PDF printer");
    }

    [Fact]
    public void ReviewShowComments_DisclosesDialogListBehavior()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
    public void ReviewCommentCommands_ExposeThreadedCommentsAndSimpleNotesDistinctly()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var commentButtons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value is
                "ReviewNewThreadedCommentBtn_Click" or
                "ReviewNewCommentBtn_Click" or
                "ReviewDeleteCommentBtn_Click" or
                "ReviewPrevCommentBtn_Click" or
                "ReviewNextCommentBtn_Click" or
                "ReviewShowCommentsBtn_Click")
            .ToList();

        var tooltipTexts = commentButtons
            .Select(element => new
            {
                Title = element.Attribute(local + "RibbonTooltip.Title")?.Value ?? "",
                Description = element.Attribute(local + "RibbonTooltip.Description")?.Value ?? ""
            })
            .ToList();

        tooltipTexts.Should().HaveCount(7);
        tooltipTexts
            .Single(text => text.Title.Equals("New Comment", StringComparison.OrdinalIgnoreCase))
            .Description.Should().Contain("threaded comment");
        tooltipTexts
            .Where(text => text.Title.Contains("Note", StringComparison.OrdinalIgnoreCase))
            .Should().OnlyContain(text => text.Description.Contains("note", StringComparison.OrdinalIgnoreCase));
        tooltipTexts.Select(text => text.Description)
            .Should().NotContain(description => description.Contains("threaded comments are not implemented", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InsertCommentCommand_ReusesThreadedCommentWorkflow()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.InsertCommands.cs"));

        var insertCommentButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "InsertCommentBtn_Click");

        insertCommentButton.Attribute(local + "RibbonTooltip.Title")?.Value.Should().Be("New Comment");
        insertCommentButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("threaded comment");
        insertCommentButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("not implemented");
        source.Should().Contain("private void InsertCommentBtn_Click(object sender, RoutedEventArgs e) => ReviewNewThreadedCommentBtn_Click(sender, e);");
    }

    [Fact]
    public void SpellingTooltip_DisclosesKnownCorrectionsBaseline()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var spellingButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "SpellCheckBtn_Click");

        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("known misspellings");
        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("text cells");
        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("replace all");
        spellingButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("proofing engine");
    }

    [Fact]
    public void AccessibilityTooltip_DisclosesCurrentCheckerCoverage()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var accessibilityButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "AccessibilityCheckerBtn_Click");

        var description = accessibilityButton.Attribute(local + "RibbonTooltip.Description")?.Value;
        description.Should().Contain("merged cells");
        description.Should().Contain("alternate text");
        description.Should().Contain("charts without titles");
    }

    [Fact]
    public void AllowEditRangesTooltip_DisclosesRangeManagerWorkflow()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var allowEditRangesButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "AllowEditRangesBtn_Click");

        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Add");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("delete");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("clear");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("ranges");
        allowEditRangesButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("permissions");
    }

    [Fact]
    public void AltTextTooltip_DisclosesSelectedCellAnchoredObjectTarget()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var splitButton = document
            .Descendants(presentation + "ToggleButton")
            .Single(element => element.Attribute("Click")?.Value == "SplitViewBtn_Click");

        splitButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("clears frozen panes");
    }

    [Fact]
    public void FreezePanesTooltip_DisclosesSplitPaneCleanup()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
    public void DirectContextMenuKeyTips_DoNotUsePrefixCollisions()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "ContextMenu")
            .SelectMany(menu =>
            {
                var directItems = menu
                    .Elements(presentation + "MenuItem")
                    .Select(item => new
                    {
                        Header = item.Attribute("Header")?.Value ?? "MenuItem",
                        KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.KeyTip))
                    .ToList();

                return directItems
                    .SelectMany(item => directItems
                        .Where(other => !ReferenceEquals(item, other))
                        .Where(other => other.KeyTip!.StartsWith(item.KeyTip!, StringComparison.OrdinalIgnoreCase))
                        .Select(other => $"{item.Header}:{item.KeyTip} prefixes {other.Header}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("leaf menu keytips must resolve without waiting for longer sibling keytips");
    }

    [Fact]
    public void CellStylesGallery_ExposesExpandedPresetLabelsAndRoutesThroughPlanner()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cellStylesMenu = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Cell Styles")
            .Descendants(presentation + "ContextMenu")
            .Single();

        var labels = cellStylesMenu
            .Elements(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .ToList();

        labels.Should().Contain([
            "Normal",
            "Good",
            "Bad",
            "Neutral",
            "Input",
            "Output",
            "Calculation",
            "Check Cell",
            "Linked Cell",
            "Explanatory Text",
            "Heading 1",
            "Heading 2",
            "Note",
            "Warning Text",
            "Total",
            "20% - Accent 1",
            "20% - Accent 2",
            "20% - Accent 3",
            "20% - Accent 4",
            "20% - Accent 5",
            "20% - Accent 6"
        ]);

        source.Should().Contain("ApplyCellStylePreset(CellStylePreset preset)");
        source.Should().Contain("CellStyleDiffPlanner.GetCellStylePresetDiff(preset, _workbook.Theme)");
        source.Should().Contain("CellStyleInputMenuItem_Click");
        source.Should().Contain("=> ApplyCellStylePreset(CellStylePreset.Input);");
        source.Should().NotContain("CellStyleGoodMenuItem_Click(object sender, RoutedEventArgs e)\r\n        => ApplyStyleDiff(new StyleDiff");
    }

    [Fact]
    public void ConditionalFormattingIconSets_ExposeGroupedPresetGalleryAndMoreRules()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var iconSetsMenu = document
            .Descendants(presentation + "MenuItem")
            .Single(item => item.Attribute("Header")?.Value == "Icon Sets");

        iconSetsMenu.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("I");
        iconSetsMenu.Elements(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should()
            .Contain(["Directional", "Shapes", "Indicators", "Ratings", "More Rules..."]);

        iconSetsMenu.Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Tag") is not null)
            .Select(item => item.Attribute("Tag")!.Value)
            .Should()
            .Contain(["3Arrows", "3TrafficLights1", "3Flags", "4Rating", "5Boxes"]);

        source.Should().Contain("CfIconSetPresetMenuItem_Click");
        source.Should().Contain("ApplyIconSetPreset");
        source.Should().Contain("ConditionalFormatIconSetPlanner.CreateRule");
    }

    [Fact]
    public void BackstageCommandButtons_HaveAltKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
    public void BackstageCommandButtons_ExposeVisibleAccessKeysForSaveAndClose()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var startScreen = document
            .Descendants(presentation + "Grid")
            .Single(element => element.Attribute(x + "Name")?.Value == "StartScreenOverlay");

        startScreen.Descendants(presentation + "Button")
            .Select(button => button.Attribute("Content")?.Value)
            .Should()
            .Contain(["_Save", "_Close"]);
    }

    [Fact]
    public void BackstageMouseOnlyCommands_AreNotUsedForRecentPinnedTabs()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
    public void DataTabCommandTooltips_DoNotAdvertiseExcludedConnectors()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        static string DescriptionFor(
            XDocument document,
            XNamespace presentation,
            XNamespace local,
            string title) =>
            document.Descendants(presentation + "Button")
                .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == title)
                .Attribute(local + "RibbonTooltip.Description")!
                .Value;

        var getData = DescriptionFor(document, presentation, local, "Get Data");
        var refreshAll = DescriptionFor(document, presentation, local, "Refresh All");

        getData.Should().Contain("local CSV file");
        getData.Should().Contain("excluded");
        refreshAll.Should().Contain("Recalculate formulas");
        refreshAll.Should().Contain("External data connections");
        refreshAll.Should().Contain("excluded");
    }

    [Fact]
    public void HomePasteButton_ExposesPasteSpecialMenuChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var pasteButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Paste");

        var headers = pasteButton
            .Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToList();

        headers.Should().ContainInOrder([
            "Paste",
            "Values",
            "Formulas",
            "Formatting",
            "Transpose",
            "Paste Special..."
        ]);

        pasteButton.Descendants(presentation + "MenuItem")
            .Should().OnlyContain(item => item.Attribute(local + "RibbonTooltip.KeyTip") != null);
    }

    [Fact]
    public void NonRibbonTooltipClickButtons_HaveAccessibleNames()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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

    [Fact]
    public void StatusBarAggregates_AreConstrainedAwayFromZoomControls()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var statusBarGrid = document
            .Descendants(presentation + "Grid")
            .Single(grid => grid.Attribute(x + "Name")?.Value == "StatusBarGrid");

        statusBarGrid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Select(column => column.Attribute("Width")?.Value)
            .Should()
            .Equal("Auto", "*", "Auto");

        var statsViewport = statusBarGrid
            .Descendants(presentation + "Border")
            .Single(border => border.Attribute(x + "Name")?.Value == "StatusStatsViewport");

        statsViewport.Attribute("Grid.Column")?.Value.Should().Be("1");
        statsViewport.Attribute("ClipToBounds")?.Value.Should().Be("True");
        statsViewport.Attribute("Margin")?.Value.Should().NotContain("180");

        var statsPanel = statsViewport
            .Descendants(presentation + "StackPanel")
            .Single(panel => panel.Attribute(x + "Name")?.Value == "StatusStatsPanel");

        statsPanel.Attribute("HorizontalAlignment")?.Value.Should().Be("Right");
        statsPanel.Attribute("ClipToBounds")?.Value.Should().Be("True");

        var zoomControls = statusBarGrid
            .Descendants(presentation + "StackPanel")
            .Single(panel => panel.Attribute(x + "Name")?.Value == "StatusZoomControls");

        zoomControls.Attribute("Grid.Column")?.Value.Should().Be("2");
        zoomControls.Attribute("MinWidth")?.Value.Should().NotBeNullOrWhiteSpace();
        zoomControls.Attribute("Background")?.Value.Should().Be("{StaticResource FreexcelStatusSurfaceBrush}");
        zoomControls.Attribute("Panel.ZIndex")?.Value.Should().Be("1");
    }

    [Theory]
    [InlineData("CellAddressBox", "Name Box", "Go to a cell or named range")]
    [InlineData("FormulaBar", "Formula Bar", "Edit the active cell value or formula")]
    public void FormulaBarTextFields_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedName,
        string expectedHelpText)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
    public void ErrorCheckingButton_ExposesOptionsEntryPoint()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var errorCheckingButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Error Checking");

        var menuItems = errorCheckingButton
            .Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = item.Attribute("Header")?.Value,
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
    public void PageLayoutBreaksButton_OpensExcelStyleBreaksMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var breaksButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Breaks");

        breaksButton.Attribute("Click")?.Value.Should().Be("PageBreaksBtn_Click");
        breaksButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("B");
        breaksButton.Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = item.Attribute("Header")?.Value,
                KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Click = item.Attribute("Click")?.Value
            })
            .Should()
            .Equal([
                new { Header = (string?)"Insert Page Break", KeyTip = (string?)"I", Click = (string?)"InsertPageBreakMenuItem_Click" },
                new { Header = (string?)"Remove Page Break", KeyTip = (string?)"R", Click = (string?)"RemovePageBreakMenuItem_Click" },
                new { Header = (string?)"Reset All Page Breaks", KeyTip = (string?)"A", Click = (string?)"ResetAllPageBreaksMenuItem_Click" }
            ]);
    }

    [Fact]
    public void DeferredCommandButtons_DescribeDeferredStatusInTooltip()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var colorsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Theme Colors");

        colorsButton.Attribute("Click")?.Value.Should().Be("ThemeColorsBtn_Click");
        colorsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        colorsButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Freexcel Colorful", "Grayscale", "Customize Colors...");
        colorsButton.Descendants(presentation + "MenuItem")
            .Single(item => item.Attribute("Header")?.Value == "Customize Colors...")
            .Attribute("Click")?.Value.Should().Be("ThemeColorsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeFontsButton_OpensFontPairMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var fontsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Theme Fonts");

        fontsButton.Attribute("Click")?.Value.Should().Be("ThemeFontsBtn_Click");
        fontsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        fontsButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Arial", "Times New Roman", "Customize Fonts...");
        fontsButton.Descendants(presentation + "MenuItem")
            .Single(item => item.Attribute("Header")?.Value == "Customize Fonts...")
            .Attribute("Click")?.Value.Should().Be("ThemeFontsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeEffectsButton_OpensEffectSetMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var effectsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute(local + "RibbonTooltip.Title")?.Value == "Theme Effects");

        effectsButton.Attribute("Click")?.Value.Should().Be("ThemeEffectsBtn_Click");
        effectsButton.Attribute(local + "RibbonTooltip.Description")?.Value.Should().NotContain("Deferred:");
        effectsButton.Descendants(presentation + "MenuItem")
            .Select(item => item.Attribute("Header")?.Value)
            .Should().Equal("Office", "Subtle", "Refined", "Customize Effects...");
        effectsButton.Descendants(presentation + "MenuItem")
            .Single(item => item.Attribute("Header")?.Value == "Customize Effects...")
            .Attribute("Click")?.Value.Should().Be("ThemeEffectsCustomizeMenuItem_Click");
    }

    [Fact]
    public void ShareCommandButtons_ArePresentedAsWindowsShareCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var shareButtons = document
            .Descendants(presentation + "Button")
            .Where(button =>
                button.Attribute("Click")?.Value is "ShareWorkbookBtn_Click" or "SsShareBtn_Click")
            .ToList();

        shareButtons.Should().NotBeEmpty();
        shareButtons
            .Select(button => new
            {
                Content = button.Attribute("Content")?.Value,
                Title = button.Attribute(local + "RibbonTooltip.Title")?.Value,
                Description = button.Attribute(local + "RibbonTooltip.Description")?.Value
            })
            .Should()
            .OnlyContain(button =>
                button.Content == "Share" &&
                button.Title == "Share" &&
                button.Description != null &&
                button.Description.Contains("Windows Share", StringComparison.Ordinal) &&
                !ContainsExcludedStatus(button.Content) &&
                !ContainsExcludedStatus(button.Title) &&
                !ContainsExcludedStatus(button.Description));
    }

    [Fact]
    public void ExternalTemplateEntryPoint_DisclosesExcludedStatusBeforeClick()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "RefreshPivotTableBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("Refresh");
    }

    [Fact]
    public void PivotTableShowDetailsEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotTableShowDetailsBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        buttons[0].Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("detail");
    }

    [Fact]
    public void PivotTableShowDetailsGesture_IsAttemptedBeforeDoubleClickEdit()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.Selection.cs")) +
            ReadPivotCommandSource();

        source.Should().Contain("e.ClickCount == 2");
        source.Should().Contain("TryShowPivotTableDetails(showMessage: false)");
    }

    [Fact]
    public void PivotChartEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotChartBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        buttons.Should().AllSatisfy(button => button.Attribute("Content")?.Value.Should().Contain("PivotChart"));
        buttons.Should().AllSatisfy(button => button.Attribute(local + "RibbonTooltip.Description")?.Value.Should().Contain("PivotTable"));
    }

    [Fact]
    public void PivotTableFieldListPane_HasExcelLikeZonesAndCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var namedElements = document
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        namedElements.Should().Contain([
            "PivotFieldListPane",
            "PivotFieldListSearchBox",
            "PivotAvailableFieldsList",
            "PivotFieldListDeferLayoutCheckBox",
            "PivotFieldListUpdateBtn",
            "PivotRowsList",
            "PivotColumnsList",
            "PivotValuesList",
            "PivotFiltersList"
        ]);

        document
            .Descendants(presentation + "Button")
            .Select(button => button.Attribute("Click")?.Value)
            .Should()
            .Contain([
                "PivotFieldToRowsBtn_Click",
                "PivotFieldToColumnsBtn_Click",
                "PivotFieldToValuesBtn_Click",
                "PivotFieldToFiltersBtn_Click",
                "PivotFieldRemoveBtn_Click",
                "PivotFieldListUpdateBtn_Click",
                "PivotFieldListCloseBtn_Click"
            ]);

        document
            .Descendants(presentation + "CheckBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListDeferLayoutCheckBox")
            .Attribute("Click")?.Value
            .Should()
            .Be("PivotFieldListDeferLayoutCheckBox_Click");
    }

    [Fact]
    public void PivotTableFieldListPane_SearchAppearsBeforeAvailableFieldsList()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListSearchBox");
        var availableFieldsList = document
            .Descendants(presentation + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotAvailableFieldsList");

        searchBox.Attribute("AutomationProperties.Name")?.Value.Should().Be("Search PivotTable Fields");
        searchBox.IsBefore(availableFieldsList).Should().BeTrue("search should be above the available fields list");
    }

    [Fact]
    public void PivotTableFieldListPane_RemoveButton_ExposesVisibleAccessKey()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "PivotFieldRemoveBtn_Click")
            .Attribute("Content")?.Value
            .Should()
            .Be("_Remove");
    }

    [Fact]
    public void PivotTableFieldListPane_RoutesThroughLayoutCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("RefreshPivotFieldListPane()");
        source.Should().Contain("ConfigurePivotTableLayoutCommand");
        source.Should().Contain("PivotFieldToRowsBtn_Click");
        source.Should().Contain("PivotFieldListCloseBtn_Click");
    }

    [Fact]
    public void PivotTableFieldListPane_ExposesFieldDropdownCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Click")?.Value?.StartsWith("PivotField", StringComparison.Ordinal) == true)
            .Select(item => item.Attribute("Click")!.Value)
            .Should()
            .Contain([
                "PivotFieldSortAscendingMenuItem_Click",
                "PivotFieldSortDescendingMenuItem_Click",
                "PivotFieldSelectItemsMenuItem_Click",
                "PivotFieldLabelFilterMenuItem_Click",
                "PivotFieldValueFilterMenuItem_Click",
                "PivotFieldClearFilterMenuItem_Click",
                "PivotFieldValueSettingsMenuItem_Click"
            ]);

        document
            .Descendants(presentation + "MenuItem")
            .Where(item => item.Attribute("Click")?.Value == "PivotFieldSortAscendingMenuItem_Click")
            .Should()
            .AllSatisfy(item => item.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void PivotTableValueFieldSettings_UsesExcelStyleDialog()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var dialogXaml = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotValueFieldSettingsDialog(current, headers)");
        mainWindowSource.Should().NotContain("Value Field Settings: name,function,show-values-as");
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFieldSettingsDialog.xaml.cs"));
        dialogSource.Should().Contain("% of Grand Total");
        dialogSource.Should().Contain("% of Row Total");
        dialogSource.Should().Contain("% of Column Total");
        dialogSource.Should().Contain("Running Total In");
        dialogSource.Should().Contain("Difference From");
        dialogSource.Should().Contain("Rank Smallest to Largest");
        dialogSource.Should().Contain("BaseFieldBox");
        dialogSource.Should().Contain("BaseItemBox");
        dialogSource.Should().Contain("NumberFormatPresetBox");
        dialogSource.Should().Contain("NumberFormatPresets");
        dialogSource.Should().Contain("NumberFormatCode");

        dialogXaml
            .Descendants(presentation + "TabItem")
            .Select(tab => tab.Attribute("Header")?.Value?.Replace("_", "", StringComparison.Ordinal))
            .Should()
            .Contain(["Summarize Values By", "Show Values As", "Number Format"]);

        dialogXaml
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain([
                "CustomNameBox",
                "SummaryFunctionBox",
                "ShowValuesAsBox",
                "BaseFieldBox",
                "BaseItemBox",
                "NumberFormatPresetBox",
                "NumberFormatBox",
                "NumberFormatCodeBox"
            ]);
    }

    [Fact]
    public void PivotTableFieldListPane_SupportsDragDropReordering()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var fieldLists = document
            .Descendants(presentation + "ListBox")
            .Where(list => (list.Attribute(xaml + "Name")?.Value ?? "").StartsWith("Pivot", StringComparison.Ordinal))
            .ToList();

        fieldLists.Should().NotBeEmpty();
        fieldLists.Should().AllSatisfy(list =>
        {
            list.Attribute("AllowDrop")?.Value.Should().Be("True");
            list.Attribute("PreviewMouseMove")?.Value.Should().Be("PivotFieldList_PreviewMouseMove");
            list.Attribute("Drop")?.Value.Should().Be("PivotFieldList_Drop");
        });

        source.Should().Contain("PivotFieldList_PreviewMouseMove");
        source.Should().Contain("PivotFieldList_Drop");
        source.Should().Contain("MovePivotFieldToZone");
    }

    [Fact]
    public void PivotTableAvailableFields_ExposeExcelStyleCheckboxToggles()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var availableList = document
            .Descendants(presentation + "ListBox")
            .Single(list => list.Attribute(xaml + "Name")?.Value == "PivotAvailableFieldsList");

        availableList
            .Descendants(presentation + "CheckBox")
            .Single()
            .Attribute("Click")?.Value
            .Should()
            .Be("PivotAvailableFieldCheckBox_Click");

        source.Should().Contain("PivotFieldListItem");
        source.Should().Contain("PivotAvailableFieldCheckBox_Click");
        source.Should().Contain("TogglePivotAvailableField");
    }

    [Fact]
    public void PivotTableSelectItems_UsesCheckboxFilterDialog()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var dialogXaml = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotFieldFilterDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotFieldFilterDialog");
        mainWindowSource.Should().NotContain("PivotTable item filter: values separated by comma or semicolon");

        dialogXaml
            .Descendants()
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain(["FilterSearchBox", "SelectAllCheckBox", "FilterItemsList"]);

        dialogXaml
            .Descendants(presentation + "CheckBox")
            .Where(item => item.Attribute(xaml + "Name")?.Value == "SelectAllCheckBox")
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void PivotTableRuleFilters_UseDialogChrome()
    {
        var mainWindowSource = ReadPivotCommandSource();
        var labelDialog = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotLabelFilterDialog.xaml"));
        var valueDialog = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFilterDialog.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotLabelFilterDialog");
        mainWindowSource.Should().Contain("new PivotValueFilterDialog");
        mainWindowSource.Should().NotContain("Label Filter: equals:text");
        mainWindowSource.Should().NotContain("Value Filter: top:n");

        labelDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["LabelFilterKindBox", "LabelFilterValueBox", "LabelFilterValue2Box"]);
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotLabelFilterDialog.xaml.cs"))
            .Should()
            .Contain("PivotLabelFilterKind.Between")
            .And.Contain("PivotLabelFilterKind.GreaterThan")
            .And.Contain("PivotLabelFilterKind.LessThan");
        valueDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["ValueFilterKindBox", "ValueFilterValueBox", "ValueFilterValue2Box"]);
        File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PivotValueFilterDialog.xaml.cs"))
            .Should()
            .Contain("PivotValueFilterKind.Between")
            .And.Contain("PivotValueFilterKind.NotBetween")
            .And.Contain("PivotValueFilterKind.AboveAverage")
            .And.Contain("PivotValueFilterKind.BelowAverage");
    }

    [Fact]
    public void PivotChartFieldButtons_RouteToPivotFieldMenus()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml.cs")) +
            ReadPivotCommandSource();

        source.Should().Contain("SheetGrid.PivotChartFieldButtonRequested += OnPivotChartFieldButtonRequested");
        source.Should().Contain("OnPivotChartFieldButtonRequested");
        source.Should().Contain("CreatePivotFieldContextMenu");
        source.Should().Contain("PivotFieldSelectItemsMenuItem_Click");
        source.Should().Contain("PivotFieldLabelFilterMenuItem_Click");
        source.Should().Contain("PivotFieldValueFilterMenuItem_Click");
    }

    [Fact]
    public void SlicerTimelinePane_ExposesInteractivePivotFilters()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "SlicerTimelinePane")
            .Attribute("AutomationProperties.Name")?.Value
            .Should()
            .Be("Slicers and Timelines");

        document.Descendants(presentation + "ItemsControl")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should()
            .Contain(["SlicerItemsControl", "TimelineItemsControl"]);

        source.Should().Contain("RefreshSlicerTimelinePane");
        source.Should().Contain("GetPivotSourceSheet");
        source.Should().Contain("AddSlicerCommand");
        source.Should().Contain("AddTimelineCommand");
        source.Should().Contain("SetSlicerSelectionCommand");
        source.Should().Contain("SetTimelineRangeCommand");
        source.Should().Contain("SlicerTileButton_Click");
        source.Should().Contain("TimelineApplyButton_Click");
    }

    [Fact]
    public void PivotTableContextualTabs_ExposeAnalyzeAndDesignCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:Freexcel.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var contextualTabs = document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute(xaml + "Name")?.Value is "PivotTableAnalyzeTab" or "PivotTableDesignTab")
            .ToList();

        contextualTabs.Select(tab => tab.Attribute("Header")?.Value)
            .Should()
            .BeEquivalentTo(["PivotTable Analyze", "Design"]);

        var clickHandlers = contextualTabs
            .Descendants(presentation + "Button")
            .Select(button => button.Attribute("Click")?.Value)
            .Where(click => click is not null)
            .ToHashSet(StringComparer.Ordinal);

        clickHandlers.Should().Contain([
            "PivotFieldListBtn_Click",
            "RefreshPivotTableBtn_Click",
            "PivotTableShowDetailsBtn_Click",
            "PivotChartBtn_Click",
            "PivotChartChangeTypeBtn_Click",
            "PivotChartOptionsBtn_Click",
            "PivotInsertSlicerBtn_Click",
            "PivotInsertTimelineBtn_Click",
            "PivotGrandTotalsBtn_Click",
            "PivotSubtotalsBtn_Click",
            "PivotReportLayoutBtn_Click",
            "PivotBlankRowsBtn_Click",
            "PivotRowHeadersBtn_Click",
            "PivotColumnHeadersBtn_Click",
            "PivotBandedRowsBtn_Click",
            "PivotBandedColumnsBtn_Click",
            "PivotStyleGalleryBtn_Click"
        ]);

        contextualTabs
            .Descendants(presentation + "Button")
            .Should()
            .AllSatisfy(button => button.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void PivotTableContextualLayoutCommands_RouteThroughUndoableOptionsCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("ApplyPivotOptions(");
        source.Should().Contain("new ConfigurePivotTableOptionsCommand");
        source.Should().NotContain("PivotTableRefreshService.Refresh(_workbook, sheet, pivotTable);");
    }

    [Fact]
    public void PivotTableContextualLayoutCommands_PreserveCompactIndentWhenUsingOptionWrapper()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("int? compactRowLabelIndent = null");
        source.Should().Contain("bool? printTitles = null");
        source.Should().Contain("bool? printExpandCollapseButtons = null");
        source.Should().Contain("bool updateAltText = false");
        source.Should().Contain("compactRowLabelIndent,");
        source.Should().Contain("updateAltText: true");
    }

    [Fact]
    public void PivotTableChangeDataSource_RoutesThroughUndoableSourceCommand()
    {
        var source = ReadPivotCommandSource();

        source.Should().Contain("new ChangePivotTableSourceCommand");
        source.Should().Contain("TryParseWorkbookRange");
        source.Should().NotContain("Rebinding a loaded PivotTable cache to a different source range is still tracked as a parity gap.");
    }

    private static bool ContainsExcludedStatus(string? value) =>
        value?.Contains("excluded", StringComparison.OrdinalIgnoreCase) == true;

    private static string ReadPivotCommandSource()
    {
        return string.Join(
            "\n",
            new[]
            {
                "MainWindow.PivotCommands.cs",
                "MainWindow.PivotAdvancedCommands.cs",
                "MainWindow.PivotChartCommands.cs",
                "MainWindow.PivotDesignCommands.cs",
                "MainWindow.PivotSlicerTimeline.cs"
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", fileName))));
    }
}
