using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowXamlKeyTipTests
{
    [Fact]
    public void RibbonSurface_IsReachableByKeyboardTabTraversal()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml"));
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
    public void TitleBarWindowChrome_ExposesMinimizeMaximizeRestoreAndCloseButtons()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var systemButtons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "MinimizeBtn_Click" or "MaxRestoreBtn_Click" or "CloseSysBtn_Click")
            .Select(button => new
            {
                Click = button.Attribute("Click")?.Value,
                AutomationName = LocalizedAttribute(button, "AutomationProperties.Name"),
                IconKind = button.Element(local + "RibbonIcon")?.Attribute("Kind")?.Value
            })
            .ToList();

        systemButtons.Should().BeEquivalentTo(
        [
            new { Click = "MinimizeBtn_Click", AutomationName = "Minimize", IconKind = "WindowMinimize" },
            new { Click = "MaxRestoreBtn_Click", AutomationName = "Maximize or Restore", IconKind = "WindowMaximize" },
            new { Click = "CloseSysBtn_Click", AutomationName = "Close", IconKind = "WindowClose" }
        ]);

        source.Should().Contain("SystemCommands.MinimizeWindow(this)");
        source.Should().Contain("SystemCommands.RestoreWindow(this)");
        source.Should().Contain("SystemCommands.MaximizeWindow(this)");
        source.Should().Contain("SystemCommands.CloseWindow(this)");
    }

    [Fact]
    public void QuickAccessToolbar_SaveUndoRedoExposeKeyTipsAndSharedCommandRoutes()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var keyTipSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyTips.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CommandExecution.cs"));
        var shellSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Shell.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var qatButtons = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute(x + "Name")?.Value is "SaveQatBtn" or "UndoQatBtn" or "RedoQatBtn")
            .Select(button => new
            {
                Name = button.Attribute(x + "Name")?.Value,
                Click = button.Attribute("Click")?.Value,
                KeyTip = button.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                AutomationName = LocalizedAttribute(button, "AutomationProperties.Name")
            })
            .ToList();

        qatButtons.Should().BeEquivalentTo(
        [
            new { Name = "SaveQatBtn", Click = "SaveButton_Click", KeyTip = "1", AutomationName = "Save" },
            new { Name = "UndoQatBtn", Click = "UndoQatBtn_Click", KeyTip = "2", AutomationName = "Undo" },
            new { Name = "RedoQatBtn", Click = "RedoQatBtn_Click", KeyTip = "3", AutomationName = "Redo" }
        ]);

        keyTipSource.Should().Contain("private bool TryInvokeTopLevelQatKeyTip(string keyTip)");
        keyTipSource.Should().Contain("GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)");
        keyTipSource.Should().Contain("private IEnumerable<FrameworkElement> EnumerateKeyTipCandidateElements");
        keyTipSource.Should().Contain("RibbonTabs.Items.OfType<TabItem>()");
        keyTipSource.Should().Contain("EnumerateQuickAccessKeyTipButtons()");
        keyTipSource.Should().Contain("selectedTab.Content as DependencyObject ?? selectedTab");
        keyTipSource.Should().Contain("if (!match.IsEnabled)");
        keyTipSource.Should().Contain("match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));");

        backstageSource.Should().Contain("private async void SaveButton_Click(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target)");
        backstageSource.Should().Contain("await SaveWorkbookToTargetAsync(target!)");
        backstageSource.Should().Contain("await SaveWorkbookWithDialogAsync()");
        backstageSource.Should().Contain("MarkWorkbookSaved()");
        backstageSource.Should().Contain("UpdateTitleBar()");

        shellSource.Should().Contain("private void UndoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteUndo();");
        shellSource.Should().Contain("private void RedoQatBtn_Click(object sender, RoutedEventArgs e) => ExecuteRedo();");
        commandSource.Should().Contain("_commandBus.Undo(_workbook.Id)");
        commandSource.Should().Contain("_commandBus.Redo(_workbook.Id)");
        commandSource.Should().Contain("RefreshToolbar()");
    }

    [Fact]
    public void EditableFontSizeBox_CommitsTypedKeyboardInputWithEnter()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var fontSizeBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontSizeBox");

        fontSizeBox.Attribute("IsEditable")?.Value.Should().Be("True");
        fontSizeBox.Attribute("KeyDown")?.Value.Should().Be("FontSizeBox_KeyDown");
        source.Should().Contain("private void FontSizeBox_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key != Key.Enter) return;");
        source.Should().Contain("private void CommitFontSizeBoxText(bool preferSelectedItem = false)");
        source.Should().Contain("var text = preferSelectedItem ? GetSelectedFontSizeText() : FontSizeBox.Text;");
        source.Should().Contain("WorksheetSizeInputParser.TryParsePositiveSize(text, out var size)");
        source.Should().Contain("ApplyFontSizeAndFitRows(size);");
    }

    [Fact]
    public void EditableFontNameBox_CommitsTypedKeyboardInputWithEnter()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var fontNameBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontNameBox");

        fontNameBox.Attribute("IsEditable")?.Value.Should().Be("True");
        fontNameBox.Attribute("IsTextSearchEnabled")?.Value.Should().Be("True");
        fontNameBox.Attribute("KeyDown")?.Value.Should().Be("FontNameBox_KeyDown");
        source.Should().Contain("private void FontNameBox_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("if (e.Key != Key.Enter) return;");
        source.Should().Contain("private void CommitFontNameBoxText()");
        source.Should().Contain("var name = FontNameBox.Text?.Trim();");
        source.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name));");
    }

    [Fact]
    public void EditableFontBoxes_CommitTypedKeyboardInputWhenFocusLeaves()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var fontNameBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontNameBox");
        var fontSizeBox = document
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FontSizeBox");

        fontNameBox.Attribute("LostKeyboardFocus")?.Value.Should().Be("FontNameBox_LostKeyboardFocus");
        fontSizeBox.Attribute("LostKeyboardFocus")?.Value.Should().Be("FontSizeBox_LostKeyboardFocus");
        source.Should().Contain("private void FontNameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("private void FontSizeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        source.Should().Contain("CommitFontNameBoxText();");
        source.Should().Contain("CommitFontSizeBoxText();");
    }

    [Fact]
    public void RibbonKeyboardFocus_IsNotHijackedByWorksheetNavigation()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));

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
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

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
    public void F10KeyTips_AreHandledBeforeTextBoxPreviewKeyFiltering()
    {
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

        const string previewHandler = "private void MainWindow_PreviewKeyDown";
        const string f10PreviewCall = "if (TryHandleShowKeyTipsPreview(e, sender))";
        var previewHandlerIndex = selectionSource.IndexOf(previewHandler, StringComparison.Ordinal);
        var f10Index = selectionSource.IndexOf(f10PreviewCall, previewHandlerIndex, StringComparison.Ordinal);
        var textBoxFilterIndex = selectionSource.IndexOf(
            "if (Keyboard.FocusedElement is TextBox or ComboBox)",
            previewHandlerIndex,
            StringComparison.Ordinal);

        previewHandlerIndex.Should().BeGreaterThanOrEqualTo(0);
        f10Index.Should().BeGreaterThanOrEqualTo(0);
        textBoxFilterIndex.Should().BeGreaterThanOrEqualTo(0);
        f10Index.Should().BeLessThan(textBoxFilterIndex);
        commandSource.Should().Contain("KeyboardCommandShortcut.ShowKeyTips");
    }

    [Fact]
    public void ShortcutAndKeyTipRoutingSnapshot_CoversRepresentativeEntryPoints()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var cellsCommandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));
        var editingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));
        var formattingSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        var selectionSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs"));
        var worksheetContextSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.WorksheetContextMenu.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        KeyboardShortcutMatcher.TryGetCommandShortcut(
                Key.F10,
                Key.None,
                ModifierKeys.None,
                out var f10Shortcut)
            .Should()
            .BeTrue();
        f10Shortcut.Should().Be(KeyboardCommandShortcut.ShowKeyTips);
        commandSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.ShowKeyTips, (_, _) => EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel));");
        selectionSource.Should().Contain("private bool TryHandleShowKeyTipsPreview(System.Windows.Input.KeyEventArgs e, object sender)");

        KeyboardShortcutMatcher.TryGetCommandShortcut(
                Key.F10,
                Key.None,
                ModifierKeys.Shift,
                out var shiftF10Shortcut)
            .Should()
            .BeTrue();
        shiftF10Shortcut.Should().Be(KeyboardCommandShortcut.OpenContextMenu);
        commandSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenContextMenu, (_, _) => OpenKeyboardContextMenu());");
        worksheetContextSource.Should().Contain("foreach (var command in WorksheetContextMenuPlanner.BuildCommands(targetKind, state))");
        WorksheetContextMenuPlanner.BuildCommands()
            .Should()
            .Contain(command => command.Header == "Format Cells..." && command.Action == WorksheetContextMenuAction.FormatCells);

        var topLevelKeyTips = document
            .Descendants(presentation + "TabItem")
            .Select(tab => new RibbonTopLevelKeyTipEntry(
                tab.Attribute("Header")?.Value ?? "",
                tab.Attribute(local + "RibbonTooltip.KeyTip")?.Value))
            .ToArray();
        var fileRoute = RibbonTopLevelKeyTipRouter.Resolve("F", topLevelKeyTips);
        fileRoute.Should().Be(RibbonTopLevelKeyTipAction.BackstageFile);
        editingSource.Should().Contain("RibbonTopLevelKeyTipRouter.Resolve(keyTip, EnumerateVisibleTopLevelRibbonKeyTipEntries())");
        editingSource.Should().Contain("{ Kind: RibbonTopLevelKeyTipActionKind.BackstageFile } => OpenFileBackstageFromKeyTip()");
        FindTab(document, "File").Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("F");

        FindTab(document, "Home").Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("H");
        var conditionalFormattingButton = document
            .Descendants(presentation + "Button")
            .Single(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") == "Conditional Formatting");
        conditionalFormattingButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("L");
        var greaterThanRule = document
            .Descendants(presentation + "MenuItem")
            .Single(element => element.Attribute("Click")?.Value == "CfGtMenuItem_Click");
        LocalizedAttribute(greaterThanRule, "Header").Should().Be("Greater Than...");
        greaterThanRule.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("HG");
        formattingSource.Should().Contain("private void CfGtMenuItem_Click(object sender, RoutedEventArgs e)       => ShowCfDialog(\"Greater Than\");");

        KeyboardShortcutMatcher.TryGetCommandShortcut(
                Key.D1,
                Key.None,
                ModifierKeys.Control,
                out var formatCellsShortcut)
            .Should()
            .BeTrue();
        formatCellsShortcut.Should().Be(KeyboardCommandShortcut.OpenFormatCells);
        commandSource.Should().Contain("_keyboardCommandDispatcher.Register(KeyboardCommandShortcut.OpenFormatCells, (_, _) => OpenFormatCellsDialog());");
        cellsCommandSource.Should().Contain("private void OpenFormatCellsDialog");
    }

    [Fact]
    public void StandaloneAltKeyTips_AreNotSuppressedByTextBoxFocus()
    {
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));
        var keyUpStart = keyboardFocusSource.IndexOf(
            "private void MainWindow_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)",
            StringComparison.Ordinal);
        var deactivatedStart = keyboardFocusSource.IndexOf(
            "private void MainWindow_Deactivated(object? sender, EventArgs e)",
            StringComparison.Ordinal);

        keyUpStart.Should().BeGreaterThanOrEqualTo(0);
        deactivatedStart.Should().BeGreaterThan(keyUpStart);
        var keyUpSource = keyboardFocusSource[keyUpStart..deactivatedStart];

        keyUpSource.Should().Contain("_standaloneAltKeyTipTracker.ShouldToggleOnKeyUp(keyTipKey)");
        keyUpSource.Should().NotContain("Keyboard.FocusedElement is TextBox or ComboBox");
        keyUpSource.Should().Contain("EnterRibbonKeyTipMode(RibbonKeyTipScope.TopLevel);");
    }

    [Fact]
    public void F6ShellFocusCycle_ContinuesWhenRegionRejectsFocus()
    {
        var keyboardFocusSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardFocus.cs"));

        keyboardFocusSource.Should().Contain("if (FocusShellRegion(current))");
        keyboardFocusSource.Should().Contain("return FormulaBar.Focus();");
        keyboardFocusSource.Should().Contain("return TryFocusCurrentSheetTab() || AddSheetButton.Focus();");
        keyboardFocusSource.Should().Contain("return FocusStatusBar();");
    }

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
    public void ConditionalFormattingTopBottomRules_ExposeExcelParityMenuChoices()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var menuItems = document
            .Descendants(presentation + "MenuItem")
            .Select(element => new
            {
                Header = LocalizedAttribute(element, "Header"),
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var dataTab = document
            .Descendants(presentation + "TabItem")
            .Single(element => LocalizedAttribute(element, "Header") == "Data");

        var flashFillButton = dataTab
            .Descendants(presentation + "Button")
            .Single(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") == "Flash Fill");

        flashFillButton.Attribute("Click")?.Value.Should().Be("FlashFillMenuItem_Click");
        flashFillButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("FF");
        LocalizedAttribute(flashFillButton, local + "RibbonTooltip.Description").Should().Contain("examples");
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
    public void MainWindowPreviewKeys_HandleWorksheetKeytipAndContextMenuEntryPoints()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs")) +
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyboardCommands.cs"));

        source.Should().Contain("this.PreviewKeyDown += MainWindow_PreviewKeyDown;");
        source.Should().Contain("KeyboardCommandShortcut.ShowKeyTips");
        source.Should().Contain("KeyboardCommandShortcut.OpenContextMenu");
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
    public void ReviewShowComments_DisclosesDialogListBehavior()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var showCommentsButton = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == "ReviewShowCommentsBtn_Click");

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
                "ReviewNewCommentBtn_Click" or
                "ReviewDeleteCommentBtn_Click" or
                "ReviewPrevCommentBtn_Click" or
                "ReviewNextCommentBtn_Click" or
                "ReviewShowCommentsBtn_Click")
            .ToList();

        var tooltipTexts = commentButtons
            .Select(element => new
            {
                Title = LocalizedAttribute(element, local + "RibbonTooltip.Title") ?? "",
                Description = LocalizedAttribute(element, local + "RibbonTooltip.Description") ?? ""
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
        LocalizedAttribute(spellingButton, local + "RibbonTooltip.Description").Should().Contain("text cells");
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
    public void TitledRibbonControls_HaveAltKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var missing = document
            .Descendants()
            .Where(element => element.Attribute(local + "RibbonTooltip.Title") is not null)
            .Where(element => element.Attribute("Click")?.Value is not ("SsPinItem_Click" or "SsUnpinItem_Click"))
            .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") ?? element.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible titled ribbon controls should participate in Excel-style Alt keytip navigation");
    }

    [Fact]
    public void RibbonTabs_DoNotReuseCommandKeyTipsWithinTheSameTab()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var duplicates = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
                tab.Descendants()
                    .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .GroupBy(element => element.Attribute(local + "RibbonTooltip.KeyTip")!.Value, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => $"{LocalizedAttribute(tab, "Header") ?? "Tab"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("unique per-tab keytips are required for deterministic Excel-style command routing");
    }

    [Fact]
    public void RibbonTabs_DoNotUseCommandKeyTipPrefixesWithinTheSameTab()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "TabItem")
            .SelectMany(tab =>
            {
                var commands = tab.Descendants()
                    .Where(element => element.Attribute(local + "RibbonTooltip.KeyTip") is not null)
                    .Where(element => element.Name != presentation + "MenuItem")
                    .Select(element => new
                    {
                        Scope = LocalizedAttribute(tab, "Header") ?? "Tab",
                        Name = LocalizedAttribute(element, local + "RibbonTooltip.Title")
                            ?? LocalizedAttribute(element, "Content")
                            ?? LocalizedAttribute(element, "Header")
                            ?? element.Attribute("Click")?.Value
                            ?? element.Name.LocalName,
                        KeyTip = element.Attribute(local + "RibbonTooltip.KeyTip")!.Value
                    })
                    .ToList();

                return commands.SelectMany(command => commands
                    .Where(other => !ReferenceEquals(command, other))
                    .Where(other => other.KeyTip.StartsWith(command.KeyTip, StringComparison.OrdinalIgnoreCase))
                    .Select(other => $"{command.Scope}:{command.Name}:{command.KeyTip} prefixes {other.Name}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("command keytips in the same ribbon scope must not shadow longer sibling keytips");
    }

    [Fact]
    public void TopLevelKeyTipHandling_WaitsForVisibleContextualTabPrefixes()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyTips.cs"));

        var prefixGuardIndex = source.IndexOf("HasVisibleTopLevelKeyTipLongerPrefix(_ribbonKeyTipSequence)", StringComparison.Ordinal);
        var topLevelRouteIndex = source.IndexOf("TryHandleTopLevelRibbonKeyTip(topLevelSequence)", StringComparison.Ordinal);

        prefixGuardIndex.Should().BeGreaterThanOrEqualTo(0);
        topLevelRouteIndex.Should().BeGreaterThanOrEqualTo(0);
        prefixGuardIndex.Should().BeLessThan(topLevelRouteIndex, "Alt, J should wait for visible JA/JD contextual tabs before selecting Draw");
    }

    [Fact]
    public void KeyedRibbonDropDowns_HaveKeyTipsForDirectMenuItems()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .SelectMany(button => button
                .Descendants(presentation + "ContextMenu")
                .Elements(presentation + "MenuItem")
                .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is null)
                .Select(menuItem =>
                    $"{LocalizedAttribute(button, local + "RibbonTooltip.Title")}:{LocalizedAttribute(menuItem, "Header")}"))
            .ToList();

        missing.Should().BeEmpty("audited ribbon dropdown menus should be reachable through staged Alt keytips");
    }

    [Fact]
    public void AllContextMenuCommands_HaveKeyTipsForDirectMenuItems()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ContextMenu")
            .Elements(presentation + "MenuItem")
            .Where(menuItem => menuItem.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(menuItem => LocalizedAttribute(menuItem, "Header") ?? "MenuItem")
            .ToList();

        missing.Should().BeEmpty("every command surfaced through a context menu should have deterministic keyboard access metadata");
    }

    [Fact]
    public void DirectContextMenuKeyTips_DoNotUsePrefixCollisions()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "ContextMenu")
            .SelectMany(menu =>
            {
                var directItems = menu
                    .Elements(presentation + "MenuItem")
                    .Select(item => new
                    {
                        Header = LocalizedAttribute(item, "Header") ?? "MenuItem",
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cellStylesMenu = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Cell Styles")
            .Descendants(presentation + "ContextMenu")
            .Single();

        var labels = cellStylesMenu
            .Elements(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
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
        var menuItemsByHeader = cellStylesMenu
            .Elements(presentation + "MenuItem")
            .ToDictionary(
                item => LocalizedAttribute(item, "Header") ?? string.Empty,
                item => item.Attribute("Click")?.Value ?? string.Empty);

        foreach (var preset in Enum.GetValues<CellStylePreset>())
        {
            var header = CellStylePresetHeader(preset);
            var clickHandler = menuItemsByHeader[header];

            clickHandler.Should().NotBeNullOrWhiteSpace($"{preset} must have a Cell Styles menu route");
            source.Should().Contain($"private void {clickHandler}(object sender, RoutedEventArgs e)");
            source.Should().Contain($"=> ApplyCellStylePreset(CellStylePreset.{preset});");
        }

        source.Should().NotContain("CellStyleGoodMenuItem_Click(object sender, RoutedEventArgs e)\r\n        => ApplyStyleDiff(new StyleDiff");
    }

    private static string CellStylePresetHeader(CellStylePreset preset) =>
        preset switch
        {
            CellStylePreset.CheckCell => "Check Cell",
            CellStylePreset.LinkedCell => "Linked Cell",
            CellStylePreset.ExplanatoryText => "Explanatory Text",
            CellStylePreset.Heading1 => "Heading 1",
            CellStylePreset.Heading2 => "Heading 2",
            CellStylePreset.WarningText => "Warning Text",
            CellStylePreset.Accent1_20 => "20% - Accent 1",
            CellStylePreset.Accent2_20 => "20% - Accent 2",
            CellStylePreset.Accent3_20 => "20% - Accent 3",
            CellStylePreset.Accent4_20 => "20% - Accent 4",
            CellStylePreset.Accent5_20 => "20% - Accent 5",
            CellStylePreset.Accent6_20 => "20% - Accent 6",
            _ => preset.ToString()
        };

    [Fact]
    public void ConditionalFormattingIconSets_ExposeGroupedPresetGalleryAndMoreRules()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var iconSetsMenu = document
            .Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Icon Sets");

        iconSetsMenu.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("I");
        iconSetsMenu.Elements(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
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
    public void StatusBarZoomCommandButtons_HaveAltKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value is "ZoomOutBtn_Click" or "ZoomInBtn_Click")
            .Where(button => button.Attribute(local + "RibbonTooltip.KeyTip") is null)
            .Select(button => LocalizedAttribute(button, "Content") ?? button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("status-bar zoom commands should participate in the visible command keytip contract");
    }

    [Fact]
    public void RibbonCheckBoxCommands_HaveTooltipTitlesDescriptionsAndKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
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
            .Select(checkBox => LocalizedAttribute(checkBox, "Content") ?? checkBox.Name.LocalName)
            .ToList();

        missing.Should().BeEmpty("visible ribbon checkbox commands should expose the same Excel-style tooltip and keytip metadata as button commands");
    }

    [Fact]
    public void RibbonComboBoxCommands_HaveAccessibleNamesMatchingTooltipTitles()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "ComboBox")
            .Where(comboBox => comboBox.Attribute(local + "RibbonTooltip.Title") is not null)
            .Where(comboBox =>
                LocalizedAttribute(comboBox, "AutomationProperties.Name") !=
                LocalizedAttribute(comboBox, local + "RibbonTooltip.Title")!)
            .Select(comboBox => LocalizedAttribute(comboBox, local + "RibbonTooltip.Title")!)
            .ToList();

        missing.Should().BeEmpty("focusable ribbon combo box commands should announce the same command name shown in Excel-style tooltips");
    }

    [Fact]
    public void DataTabCommandTooltips_DoNotAdvertiseExcludedConnectors()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        static string DescriptionFor(
            XDocument document,
            XNamespace presentation,
            XNamespace local,
            string title) =>
            LocalizedAttribute(
                document.Descendants(presentation + "Button")
                    .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == title),
                local + "RibbonTooltip.Description")!;

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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var pasteButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Paste");

        var headers = pasteButton
            .Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click") is not null)
            .Where(button => button.Attribute(local + "RibbonTooltip.Title") is null)
            .Where(button => button.Attribute("AutomationProperties.Name") is null)
            .Select(button =>
                button.Attribute(x + "Name")?.Value ??
                LocalizedAttribute(button, "Content") ??
                button.Attribute("Click")!.Value)
            .ToList();

        missing.Should().BeEmpty("clickable buttons outside the ribbon-tooltip command system should still have accessible names");
    }

    [Fact]
    public void StatusBarZoomSlider_HasAccessibleRangeMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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

        LocalizedAttribute(zoomSlider, "AutomationProperties.Name").Should().Be("Zoom Slider");
        LocalizedAttribute(zoomSlider, "AutomationProperties.HelpText").Should().Contain("10%").And.Contain("400%");
        LocalizedAttribute(zoomSlider, "ToolTip").Should().Be("Zoom");
    }

    [Fact]
    public void StatusBarAggregates_AreConstrainedAwayFromZoomControls()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
            .Descendants(presentation + "Grid")
            .Single(panel => panel.Attribute(x + "Name")?.Value == "StatusZoomControls");

        zoomControls.Attribute("Grid.Column")?.Value.Should().Be("2");
        zoomControls.Attribute("MinWidth")?.Value.Should().NotBeNullOrWhiteSpace();
        zoomControls.Attribute("Height")?.Value.Should().Be("24");
        zoomControls.Attribute("Background")?.Value.Should().Be("{StaticResource FreeXStatusSurfaceBrush}");
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var textBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == controlName);

        var name = textBox.Attribute("AutomationProperties.Name");
        var helpText = textBox.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("formula bar text fields are keyboard-focusable Excel surface controls");
        helpText.Should().NotBeNull("formula bar text fields should announce their workflow role");
        LocalizedAttribute(textBox, "AutomationProperties.Name").Should().Be(expectedName);
        LocalizedAttribute(textBox, "AutomationProperties.HelpText").Should().Be(expectedHelpText);
    }

    [Fact]
    public void NameBox_CommitsTypedReferenceWithEnter()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var nameBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "CellAddressBox");

        nameBox.Attribute("KeyDown")?.Value.Should().Be("CellAddressBox_KeyDown");
        source.Should().Contain("private void CellAddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)");
        source.Should().Contain("GoToDialog.TryParseReferenceRange(");
        source.Should().Contain("SetSelectionRange(selectedRange, selectedRange.Start);");
        source.Should().Contain("FocusSheetGridIfNeeded();");
        source.Should().Contain("CellAddressBox.SelectAll();");
    }

    [Fact]
    public void NameBox_EscapeCancelsTypedReferenceAndReturnsToGrid()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Editing.cs"));

        source.Should().Contain("if (e.Key == Key.Escape && e.KeyboardDevice.Modifiers == ModifierKeys.None)");
        source.Should().Contain("RestoreCellAddressBoxText();");
        source.Should().Contain("FocusSheetGridIfNeeded();");
        source.Should().Contain("private void RestoreCellAddressBoxText()");
        source.Should().Contain("CellAddressBox.Text = SheetGrid.SelectedRange is { } range");
        source.Should().Contain("? FormatRangeReference(range.Start, range.End)");
    }

    [Fact]
    public void FormulaBarTextFields_UseReadableExcelScaleSizing()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var formulaBar = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "FormulaBar");
        var nameBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(x + "Name")?.Value == "CellAddressBox");
        var overlay = document
            .Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute(x + "Name")?.Value == "FormulaBarReferenceOverlay");

        formulaBar.Attribute("FontSize")?.Value.Should().Be("18");
        formulaBar.Attribute("MinHeight")?.Value.Should().Be("30");
        formulaBar.Attribute("Padding")?.Value.Should().Be("6,3");
        nameBox.Attribute("FontSize")?.Value.Should().Be("15");
        nameBox.Attribute("MinHeight")?.Value.Should().Be("30");
        overlay.Attribute("FontSize")?.Value.Should().Be("18");
    }

    [Theory]
    [InlineData("StatusZoomOutButton")]
    [InlineData("StatusZoomInButton")]
    public void StatusBarZoomGlyphButtons_AreReadableAtExcelScale(string buttonName)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var button = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute(x + "Name")?.Value == buttonName);

        button.Attribute("Width")?.Value.Should().Be("22");
        button.Attribute("Height")?.Value.Should().Be("22");
        button.Attribute("FontSize")?.Value.Should().Be("18");
    }

    [Fact]
    public void GreenSurfaceButtons_UseCustomHoverChromeInsteadOfNativeBlueHover()
    {
        var mainWindow = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var resources = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "Resources", "MainWindowResources.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var buttonName in new[] { "StatusZoomOutButton", "StatusZoomInButton" })
        {
            var button = mainWindow
                .Descendants(presentation + "Button")
                .Single(element => element.Attribute(x + "Name")?.Value == buttonName);

            button.Attribute("Style")?.Value.Should().Be("{StaticResource StatusBarZoomButtonStyle}");
        }

        static XElement ResourceStyle(XDocument document, XNamespace presentation, XNamespace x, string key) =>
            document
                .Descendants(presentation + "Style")
                .Single(style => style.Attribute(x + "Key")?.Value == key);

        foreach (var styleKey in new[] { "StatusBarZoomButtonStyle", "SysBtnStyle", "TitleBarQatButton" })
        {
            var style = ResourceStyle(resources, presentation, x, styleKey);

            style
                .Descendants(presentation + "ControlTemplate")
                .Should()
                .NotBeEmpty($"{styleKey} should not fall back to the native WPF button template");

            style
                .ToString(SaveOptions.DisableFormatting)
                .Should()
                .Contain("FreeXTitleBarHoverBrush", $"{styleKey} should use the green title/status hover color");
        }

        var closeStyle = ResourceStyle(resources, presentation, x, "CloseSysBtnStyle");
        closeStyle.Attribute("BasedOn")?.Value.Should().Be("{StaticResource SysBtnStyle}");
        closeStyle
            .Descendants(presentation + "Trigger")
            .Where(trigger => trigger.Attribute("Property")?.Value == "IsMouseOver")
            .Should()
            .BeEmpty("the close button should share the same title-bar hover chrome as the other green-surface buttons");

        var greenSurfaceStyleText = string.Concat(
            new[] { "StatusBarZoomButtonStyle", "SysBtnStyle", "TitleBarQatButton", "CloseSysBtnStyle" }
                .Select(styleKey => ResourceStyle(resources, presentation, x, styleKey).ToString(SaveOptions.DisableFormatting)));

        greenSurfaceStyleText.Should().NotContain("#0078", "green-surface hover should not use Windows blue accent colors");
        greenSurfaceStyleText.Should().NotContain("SystemColors.Highlight", "green-surface hover should not use native highlight brushes");
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

    [Theory]
    [InlineData("SortAscButton_Click", "SortAscending")]
    [InlineData("SortDescButton_Click", "SortDescending")]
    [InlineData("FilterButton_Click", "Filter")]
    [InlineData("ClearFilterButton_Click", "Clear")]
    [InlineData("AdvancedFilterBtn_Click", "Filter")]
    public void DataSortFilterCommands_UseVectorRibbonIconsInsteadOfTextPlaceholders(
        string clickHandler,
        string expectedIconKind)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace local = "clr-namespace:FreeX.App.Host";

        var button = document
            .Descendants(presentation + "Button")
            .Single(element => element.Attribute("Click")?.Value == clickHandler);

        button
            .Descendants(local + "RibbonIcon")
            .Single()
            .Attribute("Kind")?.Value
            .Should().Be(expectedIconKind);

        button
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute("Tag")?.Value == "RibbonIcon")
            .Should()
            .BeEmpty("ribbon visuals should use vector icon controls instead of text placeholders");
    }

    [Fact]
    public void MainRibbon_DoesNotUseTextBlockIconPlaceholders()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var placeholders = document
            .Descendants(presentation + "TextBlock")
            .Where(element => element.Attribute("Tag")?.Value == "RibbonIcon")
            .Select(element => LocalizedAttribute(element, "Text") ?? "<unnamed>")
            .ToList();

        placeholders.Should().BeEmpty("the ribbon screenshot sweep should render actual SVG/vector icons, not text stand-ins");
    }

    [Theory]
    [InlineData("VerticalScroll", "Vertical Worksheet Scroll Bar", "Scroll worksheet rows")]
    [InlineData("HorizontalScroll", "Horizontal Worksheet Scroll Bar", "Scroll worksheet columns")]
    public void WorksheetScrollBars_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedName,
        string expectedHelpText)
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var scrollBar = document
            .Descendants(presentation + "ScrollBar")
            .Single(element => element.Attribute(x + "Name")?.Value == controlName);

        var name = scrollBar.Attribute("AutomationProperties.Name");
        var helpText = scrollBar.Attribute("AutomationProperties.HelpText");

        name.Should().NotBeNull("worksheet scrollbars are keyboard-focusable Excel surface controls");
        helpText.Should().NotBeNull("worksheet scrollbars should announce whether they move rows or columns");
        LocalizedAttribute(scrollBar, "AutomationProperties.Name").Should().Be(expectedName);
        LocalizedAttribute(scrollBar, "AutomationProperties.HelpText").Should().Be(expectedHelpText);
    }

    [Fact]
    public void NestedRibbonMenuItems_HaveStagedKeyTips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "MenuItem")
            .Where(menuItem => menuItem.Descendants(presentation + "MenuItem").Any())
            .SelectMany(menuItem => menuItem
                .Elements(presentation + "MenuItem")
                .Where(child => child.Attribute(local + "RibbonTooltip.KeyTip") is null)
                .Select(child => $"{LocalizedAttribute(menuItem, "Header")}:{LocalizedAttribute(child, "Header")}"))
            .ToList();

        missing.Should().BeEmpty("nested ribbon menu choices should be reachable through staged Alt keytips");
    }

    [Fact]
    public void RibbonMenus_DoNotReuseKeyTipsWithinTheSameMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
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
                    .Select(group => $"{LocalizedAttribute(menu, "Header") ?? "ContextMenu"}:{group.Key}"))
            .ToList();

        duplicates.Should().BeEmpty("menu-level keytips must be unique for deterministic staged Alt routing");
    }

    [Fact]
    public void RibbonMenus_DoNotUseKeyTipPrefixesWithinTheSameMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var collisions = document
            .Descendants(presentation + "ContextMenu")
            .Concat(document.Descendants(presentation + "MenuItem")
                .Where(menuItem => menuItem.Elements(presentation + "MenuItem").Any()))
            .SelectMany(menu =>
            {
                var items = menu.Elements(presentation + "MenuItem")
                    .Select(item => new
                    {
                        Header = LocalizedAttribute(item, "Header") ?? item.Attribute("Click")?.Value ?? "MenuItem",
                        KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.KeyTip))
                    .ToList();

                return items.SelectMany(item => items
                    .Where(other => !ReferenceEquals(item, other))
                    .Where(other => other.KeyTip!.StartsWith(item.KeyTip!, StringComparison.OrdinalIgnoreCase))
                    .Select(other => $"{LocalizedAttribute(menu, "Header") ?? "ContextMenu"}:{item.Header}:{item.KeyTip} prefixes {other.Header}:{other.KeyTip}"));
            })
            .ToList();

        collisions.Should().BeEmpty("menu-level keytips must not shadow longer sibling keytips");
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
    public void PageLayoutBreaksButton_OpensExcelStyleBreaksMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var breaksButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Breaks");

        breaksButton.Attribute("Click")?.Value.Should().Be("PageBreaksBtn_Click");
        breaksButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("BK");
        breaksButton.Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = LocalizedAttribute(item, "Header"),
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var missing = document
            .Descendants(presentation + "Button")
            .Where(button =>
                button.Attribute("Click")?.Value is "PageLayoutDeferredBtn_Click" or "ViewWindowDeferredBtn_Click")
            .Where(button =>
                LocalizedAttribute(button, local + "RibbonTooltip.Description")?.Contains("Deferred:", StringComparison.OrdinalIgnoreCase) != true)
            .Select(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") ?? LocalizedAttribute(button, "Content") ?? "Button")
            .ToList();

        missing.Should().BeEmpty("deferred visible commands should clearly say they are deferred before the user clicks");
    }

    [Fact]
    public void ViewWindowDeferredCommands_ExposeStableKeyTipsAndDeferredTooltips()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        document
            .Descendants(presentation + "Button")
            .Where(button => button.Attribute("Click")?.Value == "ViewWindowDeferredBtn_Click")
            .Select(button => new
            {
                Title = LocalizedAttribute(button, local + "RibbonTooltip.Title"),
                KeyTip = button.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Description = LocalizedAttribute(button, local + "RibbonTooltip.Description")
            })
            .Should()
            .Equal([
                new { Title = (string?)"New Window", KeyTip = (string?)"NW", Description = (string?)"Deferred: requires multiple live windows over the same workbook session." },
                new { Title = (string?)"Hide", KeyTip = (string?)"H", Description = (string?)"Deferred: requires workbook-window visibility state." },
                new { Title = (string?)"Unhide", KeyTip = (string?)"U", Description = (string?)"Deferred: requires workbook-window visibility state." },
                new { Title = (string?)"View Side by Side", KeyTip = (string?)"B", Description = (string?)"Deferred: requires multi-window workbook hosting and synchronized scroll routing." },
                new { Title = (string?)"Synchronous Scrolling", KeyTip = (string?)"SS", Description = (string?)"Deferred: requires paired workbook windows with synchronized viewport state." },
                new { Title = (string?)"Reset Window Position", KeyTip = (string?)"RP", Description = (string?)"Deferred: requires paired workbook windows and side-by-side layout state." },
                new { Title = (string?)"Switch Windows", KeyTip = (string?)"W", Description = (string?)"Deferred: requires a multi-window workbook registry." }
            ]);
    }

    [Fact]
    public void ViewWindowDeferredCommands_UseOwnedDeferredMessage()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.ViewCommands.cs"));
        var handlerStart = source.IndexOf("private void ViewWindowDeferredBtn_Click(", StringComparison.Ordinal);
        handlerStart.Should().BeGreaterThanOrEqualTo(0);
        var handlerEnd = source.IndexOf("private void FreezePanesPickerBtn_Click(", handlerStart, StringComparison.Ordinal);
        handlerEnd.Should().BeGreaterThan(handlerStart);
        var handler = source[handlerStart..handlerEnd];

        handler.Should().Contain("DeferredCommandMessages.MultiWindow(commandName)");
        handler.Should().Contain("ShowOwnedMessage(message.Body, message.Title, MessageBoxButton.OK, MessageBoxImage.Information);");
        handler.Should().NotContain("MessageBox.Show(");
    }

    [Fact]
    public void PageLayoutThemesButton_OpensWorkbookThemeMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var themesButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Themes");

        themesButton.Attribute("Click")?.Value.Should().Be("ThemeBtn_Click");
        LocalizedAttribute(themesButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        themesButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "FreeX Colorful", "Grayscale", "Customize...");
        themesButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize...")
            .Attribute("Click")?.Value.Should().Be("ThemeCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeColorsButton_OpensColorSchemeMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var colorsButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Theme Colors");

        colorsButton.Attribute("Click")?.Value.Should().Be("ThemeColorsBtn_Click");
        LocalizedAttribute(colorsButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        colorsButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "FreeX Colorful", "Grayscale", "Customize Colors...");
        colorsButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize Colors...")
            .Attribute("Click")?.Value.Should().Be("ThemeColorsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeFontsButton_OpensFontPairMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var fontsButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Theme Fonts");

        fontsButton.Attribute("Click")?.Value.Should().Be("ThemeFontsBtn_Click");
        LocalizedAttribute(fontsButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        fontsButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "Arial", "Times New Roman", "Customize Fonts...");
        fontsButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize Fonts...")
            .Attribute("Click")?.Value.Should().Be("ThemeFontsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeEffectsButton_OpensEffectSetMenu()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var effectsButton = document
            .Descendants(presentation + "Button")
            .Single(button => LocalizedAttribute(button, local + "RibbonTooltip.Title") == "Theme Effects");

        effectsButton.Attribute("Click")?.Value.Should().Be("ThemeEffectsBtn_Click");
        LocalizedAttribute(effectsButton, local + "RibbonTooltip.Description").Should().NotContain("Deferred:");
        effectsButton.Descendants(presentation + "MenuItem")
            .Select(item => LocalizedAttribute(item, "Header"))
            .Should().Equal("Office", "Subtle", "Refined", "Customize Effects...");
        effectsButton.Descendants(presentation + "MenuItem")
            .Single(item => LocalizedAttribute(item, "Header") == "Customize Effects...")
            .Attribute("Click")?.Value.Should().Be("ThemeEffectsCustomizeMenuItem_Click");
    }

    [Fact]
    public void PageLayoutThemeCommands_ExposeStableAutomationMetadata()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        AssertThemeButton(document, local, presentation, "Themes", "PageLayoutThemesButton", "Open workbook theme presets and customization.");
        AssertThemeButton(document, local, presentation, "Theme Colors", "PageLayoutThemeColorsButton", "Open workbook theme color presets and customization.");
        AssertThemeButton(document, local, presentation, "Theme Fonts", "PageLayoutThemeFontsButton", "Open workbook theme font presets and customization.");
        AssertThemeButton(document, local, presentation, "Theme Effects", "PageLayoutThemeEffectsButton", "Open workbook theme effect presets and customization.");

        var expectedMenuItems = new (string Header, string AutomationName, string AutomationId)[]
        {
            ("Office", "Office theme", "PageLayoutThemeOfficeMenuItem"),
            ("FreeX Colorful", "FreeX Colorful theme", "PageLayoutThemeColorfulMenuItem"),
            ("Grayscale", "Grayscale theme", "PageLayoutThemeGrayscaleMenuItem"),
            ("Customize...", "Customize theme", "PageLayoutThemeCustomizeMenuItem"),
            ("Office", "Office theme colors", "PageLayoutThemeColorsOfficeMenuItem"),
            ("FreeX Colorful", "FreeX Colorful theme colors", "PageLayoutThemeColorsColorfulMenuItem"),
            ("Grayscale", "Grayscale theme colors", "PageLayoutThemeColorsGrayscaleMenuItem"),
            ("Customize Colors...", "Customize theme colors", "PageLayoutThemeColorsCustomizeMenuItem"),
            ("Office", "Office theme fonts", "PageLayoutThemeFontsOfficeMenuItem"),
            ("Arial", "Arial theme fonts", "PageLayoutThemeFontsArialMenuItem"),
            ("Times New Roman", "Times New Roman theme fonts", "PageLayoutThemeFontsTimesMenuItem"),
            ("Customize Fonts...", "Customize theme fonts", "PageLayoutThemeFontsCustomizeMenuItem"),
            ("Office", "Office theme effects", "PageLayoutThemeEffectsOfficeMenuItem"),
            ("Subtle", "Subtle theme effects", "PageLayoutThemeEffectsSubtleMenuItem"),
            ("Refined", "Refined theme effects", "PageLayoutThemeEffectsRefinedMenuItem"),
            ("Customize Effects...", "Customize theme effects", "PageLayoutThemeEffectsCustomizeMenuItem")
        };

        foreach (var expected in expectedMenuItems)
        {
            var menuItem = document
                .Descendants(presentation + "MenuItem")
                .Single(item =>
                    LocalizedAttribute(item, "Header") == expected.Header &&
                    item.Attribute("AutomationProperties.AutomationId")?.Value == expected.AutomationId);

            LocalizedAttribute(menuItem, "AutomationProperties.Name").Should().Be(expected.AutomationName);
            LocalizedAttribute(menuItem, "AutomationProperties.HelpText").Should().NotBeNullOrWhiteSpace();
        }

        static void AssertThemeButton(
            XDocument document,
            XNamespace local,
            XNamespace presentation,
            string tooltipTitle,
            string automationId,
            string helpText)
        {
            var button = document
                .Descendants(presentation + "Button")
                .Single(element => LocalizedAttribute(element, local + "RibbonTooltip.Title") == tooltipTitle);

            LocalizedAttribute(button, "AutomationProperties.Name").Should().Be(tooltipTitle);
            button.Attribute("AutomationProperties.AutomationId")?.Value.Should().Be(automationId);
            LocalizedAttribute(button, "AutomationProperties.HelpText").Should().Be(helpText);
        }
    }

    [Fact]
    public void DrawFormatCropGradientEffectsButtons_ExposeAccessibleCommandsAndMenus()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var cropButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "PictureCropBtn_Click");
        var gradientButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "ObjectGradientBtn_Click");
        var effectsButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "ObjectEffectsBtn_Click");

        cropButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("C");
        cropButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"DrawCropPictureButton\"");
        gradientButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("G");
        gradientButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"DrawShapeGradientButton\"");
        effectsButton.Attribute(local + "RibbonTooltip.KeyTip")?.Value.Should().Be("FX");
        effectsButton.ToString().Should().Contain("AutomationProperties.AutomationId=\"DrawShapeEffectsButton\"");

        var cropMenuItems = cropButton
            .Descendants(presentation + "MenuItem")
            .Select(item => new
            {
                Header = LocalizedAttribute(item, "Header"),
                KeyTip = item.Attribute(local + "RibbonTooltip.KeyTip")?.Value,
                Click = item.Attribute("Click")?.Value,
                Markup = item.ToString()
            })
            .ToList();

        cropMenuItems.Should().Contain(item =>
            item.Header == "Crop..." &&
            item.KeyTip == "C" &&
            item.Click == "PictureCropDialogMenuItem_Click" &&
            item.Markup.Contains("AutomationProperties.AutomationId=\"DrawCropPictureMenuItem\""));
        cropMenuItems.Should().Contain(item =>
            item.Header == "Reset Crop" &&
            item.KeyTip == "R" &&
            item.Click == "PictureResetCropMenuItem_Click" &&
            item.Markup.Contains("AutomationProperties.AutomationId=\"DrawResetPictureCropMenuItem\""));
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

    [Fact]
    public void PivotTableEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotTableShowDetailsBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        LocalizedAttribute(buttons[0], local + "RibbonTooltip.Description").Should().Contain("detail");
    }

    [Fact]
    public void PivotTableShowDetailsGesture_IsAttemptedBeforeDoubleClickEdit()
    {
        var source =
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Selection.cs")) +
            ReadPivotCommandSource();

        source.Should().Contain("e.ClickCount == 2");
        source.Should().Contain("TryShowPivotTableDetails(showMessage: false)");
    }

    [Fact]
    public void PivotTableShowDetailsCommand_UsesUndoableDrillDownAndActivatesCreatedDetailSheet()
    {
        var source = ReadPivotCommandSource();
        var handlerSource = source[
            source.IndexOf("private bool TryShowPivotTableDetails", StringComparison.Ordinal)..
            source.IndexOf("private void RefreshPivotFieldListPane", StringComparison.Ordinal)];

        handlerSource.Should().Contain("PivotUiPlanner.ResolveShowDetailsTarget(sheet, SheetGrid.SelectedRange)");
        handlerSource.Should().Contain("new DrillDownPivotTableCommand(_currentSheetId, target.PivotTableName, target.PivotCell)");
        handlerSource.Should().Contain("\"Show PivotTable Details\"");
        handlerSource.Should().Contain("out var outcome");
        handlerSource.Should().Contain("outcome.AffectedCells?.FirstOrDefault()");
        handlerSource.Should().Contain("_currentSheetId = detailAnchor.Sheet;");
        handlerSource.Should().Contain("RefreshSheetTabs();");
        handlerSource.Should().Contain("UpdateViewport();");
        handlerSource.Should().NotContain("new AddSheetCommand");
        handlerSource.Should().NotContain("_workbook.Sheets.LastOrDefault()");
        handlerSource.Should().NotContain("PivotTableRefreshService.Refresh");
    }

    [Fact]
    public void PivotChartEntryPoint_IsAvailableOnInsertRibbon()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var buttons = document
            .Descendants(presentation + "Button")
            .Where(element => element.Attribute("Click")?.Value == "PivotChartBtn_Click")
            .ToList();

        buttons.Should().NotBeEmpty();
        buttons.Should().AllSatisfy(button => LocalizedAttribute(button, "Content").Should().Contain("PivotChart"));
        buttons.Should().AllSatisfy(button => LocalizedAttribute(button, local + "RibbonTooltip.Description").Should().Contain("PivotTable"));
    }

    [Fact]
    public void PivotTableFieldListPane_HasExcelLikeZonesAndCommands()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var searchBox = document
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotFieldListSearchBox");
        var availableFieldsList = document
            .Descendants(presentation + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "PivotAvailableFieldsList");

        LocalizedAttribute(searchBox, "AutomationProperties.Name").Should().Be("Search PivotTable Fields");
        searchBox.IsBefore(availableFieldsList).Should().BeTrue("search should be above the available fields list");
    }

    [Fact]
    public void PivotTableFieldListPane_RemoveButton_ExposesVisibleAccessKey()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var removeButton = document
            .Descendants(presentation + "Button")
            .Single(button => button.Attribute("Click")?.Value == "PivotFieldRemoveBtn_Click");

        LocalizedAttribute(removeButton, "Content").Should().Be("_Remove");
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace local = "clr-namespace:FreeX.App.Host";
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
        var dialogXaml = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFieldSettingsDialog.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotValueFieldSettingsDialog(current, headers)");
        mainWindowSource.Should().NotContain("Value Field Settings: name,function,show-values-as");
        var plannerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFieldSettingsDialogPlanner.cs"));
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFieldSettingsDialog.xaml.cs"));
        plannerSource.Should().Contain("% of Grand Total");
        plannerSource.Should().Contain("% of Row Total");
        plannerSource.Should().Contain("% of Column Total");
        plannerSource.Should().Contain("Running Total In");
        plannerSource.Should().Contain("Difference From");
        plannerSource.Should().Contain("Rank Smallest to Largest");
        dialogSource.Should().Contain("BaseFieldBox");
        dialogSource.Should().Contain("BaseItemBox");
        dialogSource.Should().Contain("NumberFormatPresetBox");
        dialogSource.Should().Contain("NumberFormatPresets");
        dialogSource.Should().Contain("NumberFormatCode");

        dialogXaml
            .Descendants(presentation + "TabItem")
            .Select(tab => LocalizedAttribute(tab, "Header")?.Replace("_", "", StringComparison.Ordinal))
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
        var dialogXaml = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotFieldFilterDialog.xaml"));
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
        var labelDialog = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotLabelFilterDialog.xaml"));
        var valueDialog = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFilterDialog.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        mainWindowSource.Should().Contain("new PivotLabelFilterDialog");
        mainWindowSource.Should().Contain("new PivotValueFilterDialog");
        mainWindowSource.Should().NotContain("Label Filter: equals:text");
        mainWindowSource.Should().NotContain("Value Filter: top:n");

        labelDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["LabelFilterKindBox", "LabelFilterValueBox", "LabelFilterValue2Box"]);
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotLabelFilterDialog.xaml.cs"))
            .Should()
            .Contain("PivotLabelFilterKind.Between")
            .And.Contain("PivotLabelFilterKind.GreaterThan")
            .And.Contain("PivotLabelFilterKind.LessThan");
        valueDialog.Descendants().Select(element => element.Attribute(xaml + "Name")?.Value)
            .Should().Contain(["ValueFilterKindBox", "ValueFilterValueBox", "ValueFilterValue2Box"]);
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "PivotValueFilterDialog.xaml.cs"))
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
            File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml.cs")) +
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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var source = ReadPivotCommandSource();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var slicerTimelinePane = document
            .Descendants(presentation + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "SlicerTimelinePane");

        LocalizedAttribute(slicerTimelinePane, "AutomationProperties.Name").Should().Be("Slicers and Timelines");

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
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace local = "clr-namespace:FreeX.App.Host";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var contextualTabs = document
            .Descendants(presentation + "TabItem")
            .Where(tab => tab.Attribute(xaml + "Name")?.Value is "PivotTableAnalyzeTab" or "PivotTableDesignTab")
            .ToList();

        contextualTabs.Select(tab => LocalizedAttribute(tab, "Header"))
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
        ResolveLocalizedValue(value)?.Contains("excluded", StringComparison.OrdinalIgnoreCase) == true;

    private static string? CommandName(XElement element, XNamespace local) =>
        element.Attribute(local + "RibbonMetadata.CommandName")?.Value ??
        LocalizedAttribute(element, local + "RibbonTooltip.Title");

    private static string? LocalizedAttribute(XElement element, XName name) =>
        ResolveLocalizedValue(element.Attribute(name)?.Value);

    private static string? LocalizedAttribute(XElement element, string name) =>
        ResolveLocalizedValue(element.Attribute(name)?.Value);

    private static string? ResolveLocalizedValue(string? value)
    {
        const string locPrefix = "{local:Loc Key=";
        if (value is not { Length: > 0 } ||
            !value.StartsWith(locPrefix, StringComparison.Ordinal) ||
            !value.EndsWith("}", StringComparison.Ordinal))
        {
            return value;
        }

        var key = value[locPrefix.Length..^1];
        return UiText.Get(key);
    }

    private static string? GetButtonText(XElement button, XNamespace presentation)
    {
        if (LocalizedAttribute(button, "Content") is { } content)
            return content;

        return button
            .Descendants()
            .Where(element => element.Name == presentation + "TextBlock" || element.Name == presentation + "AccessText")
            .Select(element => LocalizedAttribute(element, "Text") ?? element.Value)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private static XElement FindTab(XDocument document, string header)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        return document
            .Descendants(presentation + "TabItem")
            .Single(element => LocalizedAttribute(element, "Header") == header);
    }

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
            }.Select(fileName => File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", fileName))));
    }
}
