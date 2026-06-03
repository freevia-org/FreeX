using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
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
    public void QuickAccessToolbar_BuildsPersistedCommandsWithKeyTipsAndSharedCommandRoutes()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
        var catalogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "QuickAccessToolbarCatalog.cs"));
        var qatSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.QuickAccessToolbar.cs"));
        var keyTipSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.KeyTips.cs"));
        var backstageSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.Backstage.cs"));
        var commandSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CommandExecution.cs"));

        xaml.Should().Contain("x:Name=\"TitleBarQatPanel\"");
        xaml.Should().Contain("x:Name=\"BelowRibbonQatPanel\"");
        catalogSource.Should().Contain("DefaultCommandIds");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Save");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Undo");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Redo");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.Print");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.InsertFunction");
        catalogSource.Should().Contain("QuickAccessToolbarCommandIds.NameManager");
        qatSource.Should().Contain("RebuildQuickAccessToolbar()");
        qatSource.Should().Contain("RibbonTooltip.SetKeyTip(button, FormatQuickAccessToolbarKeyTip(visibleIndex));");
        qatSource.Should().Contain("AutomationProperties.SetAutomationId(button, command.AutomationId);");
        qatSource.Should().Contain("RegisterName(command.AutomationId, button);");
        qatSource.Should().Contain("ExecuteQuickAccessToolbarCommand(command.Id, button, args)");

        keyTipSource.Should().Contain("private bool TryInvokeTopLevelQatKeyTip(string keyTip)");
        keyTipSource.Should().Contain("GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel)");
        keyTipSource.Should().Contain("private IEnumerable<FrameworkElement> EnumerateKeyTipCandidateElements");
        keyTipSource.Should().Contain("RibbonTabs.Items.OfType<TabItem>()");
        keyTipSource.Should().Contain("EnumerateQuickAccessToolbarButtons()");
        keyTipSource.Should().Contain("selectedTab.Content as DependencyObject ?? selectedTab");
        keyTipSource.Should().Contain("if (!match.IsEnabled)");
        keyTipSource.Should().Contain("match.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, match));");

        backstageSource.Should().Contain("private async void SaveButton_Click(object sender, RoutedEventArgs e)");
        backstageSource.Should().Contain("FileSavePlanner.TryResolveExistingPath(_currentFilePath, _fileAdapters, out var target)");
        backstageSource.Should().Contain("await SaveWorkbookToTargetAsync(target!)");
        backstageSource.Should().Contain("await SaveWorkbookWithDialogAsync()");
        backstageSource.Should().Contain("MarkWorkbookSaved()");
        backstageSource.Should().Contain("UpdateTitleBar()");

        qatSource.Should().Contain("case QuickAccessToolbarCommandIds.Undo:");
        qatSource.Should().Contain("ExecuteUndo();");
        qatSource.Should().Contain("case QuickAccessToolbarCommandIds.Redo:");
        qatSource.Should().Contain("ExecuteRedo();");
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
                LocalizedAttribute(tab, "Header") ?? "",
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

        LocalizedAttribute(zoomSlider, "AutomationProperties.Name").Should().Be(UiText.Get("MainWindow_AutomationName_ZoomSlider"));
        LocalizedAttribute(zoomSlider, "AutomationProperties.HelpText").Should().Contain("10%").And.Contain("400%");
        LocalizedAttribute(zoomSlider, "ToolTip").Should().Be(UiText.Get("MainWindow_ToolTip_Zoom"));
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
        zoomControls.Attribute("KeyboardNavigation.TabNavigation")?.Value.Should().Be("Cycle");
        zoomControls.Attribute("KeyboardNavigation.ControlTabNavigation")?.Value.Should().Be("Cycle");
    }

    [Theory]
    [InlineData("CellAddressBox", "MainWindow_AutomationName_NameBox", "MainWindow_AutomationHelpText_GoToACellOrNamedRange")]
    [InlineData("FormulaBar", "MainWindow_AutomationName_FormulaBar", "MainWindow_AutomationHelpText_EditTheActiveCellValueOrFormula")]
    public void FormulaBarTextFields_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedNameKey,
        string expectedHelpTextKey)
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
        LocalizedAttribute(textBox, "AutomationProperties.Name").Should().Be(UiText.Get(expectedNameKey));
        LocalizedAttribute(textBox, "AutomationProperties.HelpText").Should().Be(UiText.Get(expectedHelpTextKey));
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
        var strokeDimensions = button
            .Descendants(presentation + "Rectangle")
            .Select(rectangle => (Width: rectangle.Attribute("Width")?.Value, Height: rectangle.Attribute("Height")?.Value))
            .ToArray();
        strokeDimensions.Should().Contain(("12", "2"));
        if (buttonName == "StatusZoomInButton")
        {
            strokeDimensions.Should().Contain(("2", "12"));
        }
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

    [Theory]
    [InlineData("VerticalScroll", "MainWindow_AutomationName_VerticalWorksheetScrollBar", "MainWindow_AutomationHelpText_ScrollWorksheetRows")]
    [InlineData("HorizontalScroll", "MainWindow_AutomationName_HorizontalWorksheetScrollBar", "MainWindow_AutomationHelpText_ScrollWorksheetColumns")]
    public void WorksheetScrollBars_HaveAccessibleNamesAndHelpText(
        string controlName,
        string expectedNameKey,
        string expectedHelpTextKey)
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
        LocalizedAttribute(scrollBar, "AutomationProperties.Name").Should().Be(UiText.Get(expectedNameKey));
        LocalizedAttribute(scrollBar, "AutomationProperties.HelpText").Should().Be(UiText.Get(expectedHelpTextKey));
    }
}
