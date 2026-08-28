using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave196RibbonFormattingPhysicalSourceTests
{
    [Fact]
    public void RibbonFormattingSelector_PinsProductionKeyTipAndPersistedStyleEvidence()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave196RibbonFormattingFixture.ps1");

        runner.Should().Contain("ribbon-formatting");
        runner.Should().Contain("ribbon-home-bold-keytip-physical");
        runner.Should().Contain("New-FreeXWave196RibbonFormattingFixture.ps1");
        probe.Should().Contain("probe_ribbon_home_bold_keytip");
        probe.Should().Contain("enter_keytip_mode");
        probe.Should().Contain("keytip_key h");
        probe.Should().Contain("keytip_key 1");
        probe.Should().Contain("fontId");
        probe.Should().Contain("bold=true");
        probe.Should().Contain("send_shifted_function_key F12");
        probe.Should().Contain("xdotool keydown --clearmodifiers --window \"$window_id\" Shift_L");
        probe.Should().Contain("xdotool keyup --window \"$window_id\" Shift_L");
        fixture.Should().Contain("Wave196 Bold Target");
        fixture.Should().Contain("<b/>");
        fixture.Should().Contain("<cellXfs count=\"2\">");
    }

    [Fact]
    public void RibbonFormattingRoute_UsesSharedMutationAndMatchesWpfHandler()
    {
        var avalonia = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.ApplicationCommandRouting.cs");
        var host = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs");
        var shared = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "Ribbon", "WorkbookFormatRibbonCommands.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var wpfShortcuts = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "KeyboardShortcutMatcher.CommandRules.cs");
        var shortcutCatalog = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Shell", "WorkbookKeyboardShortcutCatalog.cs");
        var avaloniaShortcuts = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.KeyboardParity.cs");
        var definition = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Ribbon.Definitions", "HomeRibbonDefinition.cs");

        avalonia.Should().Contain("ToggleSelectedRangeBold()");
        host.Should().Contain("WorkbookFormatRibbonCommands.Bold");
        shared.Should().Contain("s.SetSelectedRangeBold(on)");
        wpf.Should().Contain("ApplyStyleDiff(new StyleDiff(Bold: IsRibbonCommandChecked(\"Bold\")))");
        wpfShortcuts.Should().Contain("WorkbookKeyboardShortcutCatalog.Rules");
        wpfShortcuts.Should().Contain("WorkbookShortcutRoute.SaveWorkbook => KeyboardCommandShortcut.SaveWorkbook");
        shortcutCatalog.Should().Contain("new(WorkbookShortcutRoute.SaveWorkbook, new WorkbookShortcutChord(WorkbookShortcutKey.F12, WorkbookShortcutModifiers.Shift))");
        avaloniaShortcuts.Should().Contain("key == Key.F24 && physicalKey != PhysicalKey.F12");
        avaloniaShortcuts.Should().Contain("modifiers | KeyModifiers.Shift");
        definition.Should().Contain(".IconToggle(\"Bold\", \"Bold\", Ico.Bold, \"1\")");
    }
}
