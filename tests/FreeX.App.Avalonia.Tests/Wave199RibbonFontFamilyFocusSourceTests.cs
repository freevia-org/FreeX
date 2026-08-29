using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave199RibbonFontFamilyFocusSourceTests
{
    [Fact]
    public void RibbonComboFocusCandidate_IsRejectedWhenPhysicalProbeStillFails()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var start = source.IndexOf("internal void ConfigureWorksheetRibbonComboFocus", StringComparison.Ordinal);
        var end = source.IndexOf("private static IEnumerable<Control> EnumerateRibbonControls", start, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var handler = source[start..end];

        handler.Should().Contain("ScheduleWorksheetFocusAfterRibbonComboClosed(combo.IsKeyboardFocusWithin)");
        handler.Should().NotContain("var comboOwnsKeyboardFocus");
        handler.Should().NotContain("|| combo.IsFocused");
        handler.Should().NotContain("if (!combo.IsDropDownOpen)");
    }

    [Fact]
    public void PhysicalProbe_MeasuresAutomaticFocusBeforeAnyWorksheetReselect()
    {
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var start = probe.IndexOf("probe_ribbon_home_font_family_combo()", StringComparison.Ordinal);
        var end = probe.IndexOf("\n}\n\n", start, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var body = probe[start..end];
        var automaticProbe = body.IndexOf("automatic_focus_clipboard", StringComparison.Ordinal);
        var reselect = body.IndexOf("select_cell 0 0 A1", StringComparison.Ordinal);

        automaticProbe.Should().BeGreaterThanOrEqualTo(0);
        reselect.Should().BeGreaterThan(automaticProbe);
        body.Should().Contain("automatic-focus-after-combo=$automatic_focus");
        body.Should().Contain("automatic-focus-status=$automatic_focus_status");
        body.Should().Contain("automatic-focus-clipboard=$automatic_focus_clipboard");
        body.Should().NotContain("automatic-focus-after-combo=not-measured");
    }

    [Fact]
    public void WpfAuthority_UsesNativeFontComboSelectionAndWorksheetKeyboardRoute()
    {
        var wpfFormatting = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var wpfSelection = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.Selection.cs");

        wpfFormatting.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        wpfFormatting.Should().Contain("FontNameBox_LostKeyboardFocus");
        wpfSelection.Should().Contain("Keyboard.FocusedElement is TextBox or ComboBox");
        wpfSelection.Should().Contain("SheetGrid.SelectedRange");
    }
}
