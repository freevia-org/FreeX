using System.IO;
using System.Windows.Input;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowXamlKeyTipTests
{
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
}
