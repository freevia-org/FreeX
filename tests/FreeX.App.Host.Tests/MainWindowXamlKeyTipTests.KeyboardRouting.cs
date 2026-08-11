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
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

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
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

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
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var commandSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardCommands.cs");

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
    public void StandaloneAltKeyTips_AreNotSuppressedByTextBoxFocus()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");
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
    public void AltHeldKeyTipContinuation_IsHandledBeforeDirectTopLevelAltRouting()
    {
        var selectionSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Selection.cs");
        var keyTipSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyTips.cs");

        var activeContinuationIndex = selectionSource.IndexOf(
            "IsRibbonKeyTipContinuationModifierState(Keyboard.Modifiers)",
            StringComparison.Ordinal);
        var directTopLevelIndex = selectionSource.IndexOf(
            "if (Keyboard.Modifiers == ModifierKeys.Alt && TryHandleDirectRibbonKeyTip(keyTipKey))",
            StringComparison.Ordinal);

        activeContinuationIndex.Should().BeGreaterThanOrEqualTo(0);
        directTopLevelIndex.Should().BeGreaterThan(activeContinuationIndex);
        keyTipSource.Should().Contain("modifiers is ModifierKeys.None or ModifierKeys.Alt");
    }

    [Fact]
    public void F6ShellFocusCycle_ContinuesWhenRegionRejectsFocus()
    {
        var keyboardFocusSource = DialogSourceTestSupport.ReadHostSources("MainWindow.KeyboardFocus.cs");

        keyboardFocusSource.Should().Contain("ShellFocusCyclePlanner.TryFocusNextAvailable(");
        keyboardFocusSource.Should().Contain("FocusShellRegion);");
        keyboardFocusSource.Should().NotContain("Enum.GetValues<ShellFocusTarget>()");
        keyboardFocusSource.Should().Contain("return FormulaBar.Focus();");
        keyboardFocusSource.Should().Contain("return TryFocusCurrentSheetTab() || AddSheetButton.Focus();");
        keyboardFocusSource.Should().Contain("return FocusStatusBar();");
    }

    [Fact]
    public void MainWindowPreviewKeys_HandleWorksheetKeytipAndContextMenuEntryPoints()
    {
        var source = DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            "",
            "MainWindow.xaml.cs",
            "MainWindow.Selection.cs",
            "MainWindow.KeyboardCommands.cs");

        source.Should().Contain("this.PreviewKeyDown += MainWindow_PreviewKeyDown;");
        source.Should().Contain("KeyboardCommandShortcut.ShowKeyTips");
        source.Should().Contain("KeyboardCommandShortcut.OpenContextMenu");
    }
}
