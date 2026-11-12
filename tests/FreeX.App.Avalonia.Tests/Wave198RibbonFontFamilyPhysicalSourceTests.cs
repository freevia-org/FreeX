using FluentAssertions;
using System.Security.Cryptography;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave198RibbonFontFamilyPhysicalSourceTests
{
    [Fact]
    public void FontFamilyPhysicalLane_UsesProductionFixtureFocusAndPackageEvidence()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave198RibbonFontFamilyFixture.ps1");

        runner.Should().Contain("ribbon-font-family");
        runner.Should().Contain("New-FreeXWave198RibbonFontFamilyFixture.ps1");
        probe.Should().Contain("probe_ribbon_home_font_family_combo");
        probe.Should().Contain("xdotool_mousemove_sync 323 96 click 1");
        probe.Should().Contain("xdotool_mousemove_sync 280 149 click 1");
        probe.Should().Contain("select_cell 0 0 A1");
        probe.Should().Contain("ribbon-home-font-family-combo-focus-reselect.png");
        probe.Should().Contain("send_key Escape || true");
        probe.Should().Contain("send_key Right");
        probe.Should().Contain("focus_clipboard");
        probe.Should().Contain("font-name");
        probe.Should().Contain("name.lower() == 'arial'");
        probe.Should().Contain("save-clean=$save_clean");
        probe.Should().Contain("ribbon-home-font-family-combo-focus-auto.png");
        probe.Should().Contain("automatic-focus-after-combo=$automatic_focus");
        probe.Should().Contain("automatic-focus-status=$automatic_focus_status");
        probe.Should().Contain("automatic-focus-clipboard=$automatic_focus_clipboard");
        probe.Should().Contain("worksheet-focus-after-reselect=$worksheet_focus");
        fixture.Should().Contain("Wave198 Font Family Target");
        fixture.Should().Contain("<name val=\"Calibri\"/>");
        fixture.Should().Contain("Unchanged");
    }

    [Fact]
    public void FontFamilyPhysicalLane_MatchesAvaloniaProductionRouteAndWpfHandler()
    {
        var avalonia = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var host = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs");
        var shared = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "WorkbookSession.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var definition = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Ribbon.Definitions", "HomeRibbonDefinition.cs");

        avalonia.Should().Contain("SetFontName = ApplyRibbonFontName");
        avalonia.Should().Contain("_session.SetSelectedRangeFontName(fontName)");
        avalonia.Should().Contain("ScheduleWorksheetFocusAfterRibbonComboClosed(combo.IsKeyboardFocusWithin)");
        host.Should().Contain("Register(registry, \"Font\", new ValueRibbonCommand(setFontName))");
        shared.Should().Contain("new StyleDiff(FontName: fontName.Trim())");
        wpf.Should().Contain("ApplyStyleDiff(new StyleDiff(FontName: name))");
        definition.Should().Contain(".ComboBox(\"Font\", \"Font\"");
    }

}
