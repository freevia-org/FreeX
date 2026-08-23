using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class Wave189AutoFilterDatePhysicalSourceTests
{
    [Fact]
    public void DatePhysicalSelector_RequiresBeforeAndAfterCriteriaRowsAndXlsxFixture()
    {
        var runner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "Run-FreeXLinuxInteractionValidation.ps1");
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");
        var fixture = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "New-FreeXWave189AutoFilterDateFixture.ps1");

        runner.Should().Contain("autofilter-date-criteria-persistence");
        runner.Should().Contain("autofilter-date-criteria-before-save-reopen-physical");
        runner.Should().Contain("autofilter-date-criteria-after-save-reopen-physical");
        runner.Should().Contain("New-FreeXWave189AutoFilterDateFixture.ps1");
        probe.Should().Contain("probe_autofilter_date_criteria_persistence_physical");
        probe.Should().Contain("lessThan|value=45323");
        probe.Should().Contain("greaterThan|value=45323");
        probe.Should().Contain("Jan01,Jan15,");
        probe.Should().Contain("Mar15,,");
        fixture.Should().Contain("@(\"Jan01\", [datetime]::new(2024, 1, 1))");
        fixture.Should().Contain("@(\"Mar15\", [datetime]::new(2024, 3, 15))");
        fixture.Should().Contain("New-DateCell");
        fixture.Should().Contain("numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"");
        fixture.Should().Contain("<autoFilter ref=`\"A1:B5`\"");
    }

    [Fact]
    public void DatePhysicalProbeUsesRenderedGlyphAndProductionOpenRoute()
    {
        var probe = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh");

        probe.Should().Contain("click_date_glyph");
        probe.Should().Contain("send_key ctrl+F12");
        probe.Should().Contain("package_date_signature");
        probe.Should().Contain("Date Filters > Before");
        probe.Should().Contain("Date Filters > After");
        probe.Should().Contain("before-dialog-closed");
        probe.Should().Contain("after-dialog-closed");
    }
}
