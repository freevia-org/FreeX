using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave197RibbonNumberFormatPhysicalSourceTests
{
    [Fact]
    public void NumberFormatPhysicalLane_UsesProductionFixtureAndReopenedPackageEvidence()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave197RibbonNumberFormatFixture.ps1");

        runner.Should().Contain("ribbon-number-format");
        runner.Should().Contain("New-FreeXWave197RibbonNumberFormatFixture.ps1");
        probe.Should().Contain("probe_ribbon_home_number_format_keytip");
        probe.Should().Contain("keytip_key n");
        probe.Should().Contain("xdotool_mousemove_sync 840 149 click 1");
        probe.Should().Contain("send_key ctrl+s");
        probe.Should().Contain("send_shifted_function_key F12");
        probe.Should().Contain("numFmtId");
        probe.Should().Contain("zipfile.ZipFile");
        probe.Should().Contain("num_fmt_id == 2");
        fixture.Should().Contain("Wave197 Number Target");
        fixture.Should().Contain("<v>1234.5</v>");
        fixture.Should().Contain("<cellXfs count=\"1\">");
    }

    [Fact]
    public void NumberFormatPhysicalLane_MatchesSharedCompositionAndWpfRoute()
    {
        var avalonia = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.cs");
        var host = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "Ribbon", "AvaloniaRibbonHost.cs");
        var composition = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Services", "Ribbon", "FreeXRibbonCompositionPlanner.cs");
        var wpf = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");

        avalonia.Should().Contain("SetNumberFormat = ApplyRibbonNumberFormat");
        avalonia.Should().Contain("KeyDown += MainWindow_KeyDown");
        avalonia.Should().Contain("combo.DropDownClosed +=");
        avalonia.Should().Contain("ScheduleWorksheetFocusAfterRibbonComboClosed(combo.IsKeyboardFocusWithin)");
        avalonia.Should().Contain("internal bool ScheduleWorksheetFocusAfterRibbonComboClosed");
        avalonia.Should().Contain("DispatcherPriority.Input");
        avalonia.Should().Contain("_session.SetSelectedRangeNumberFormat(numberFormat)");
        host.Should().Contain("Register(registry, \"Number Format\", new ValueRibbonCommand(setNumberFormat))");
        composition.Should().Contain("\"Number Format\" => HomeNumberFormatGalleryPlanner.Choices");
        wpf.Should().Contain("ApplyStyleDiff(new StyleDiff(NumberFormat: code))");
    }
}
