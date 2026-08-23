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
        probe.Should().Contain("wait_for_document_idle");
        probe.Should().Contain("MainWindow's save workflow releases _isSaving");
        probe.Should().Contain("wait_for_document_idle || return 1");
        probe.Should().Contain("reopen_date_document_with_retry \"$output/autofilter-date-before-open-cycle.txt\"");
        probe.Should().Contain("reopen_date_document_with_retry \"$output/autofilter-date-after-open-cycle.txt\"");
        probe.Split("reopen_date_document_with_retry", StringSplitOptions.None).Should().HaveCount(4);
        probe.Should().Contain("before-open-attempts=$before_open_attempts");
        probe.Should().Contain("autofilter-date-after-open-cycle.txt");
        probe.Should().Contain("autofilter-date-before-open-cycle.txt");
        probe.Should().Contain("write_open_cycle_diagnostics");
        probe.Should().Contain("after-open-attempts=$after_open_attempts");
        probe.Should().Contain("for attempt in $(seq 1 4)");
        probe.Should().Contain("sleep \"$dialog_settle_seconds\"");
        probe.Should().Contain("read_first_rendered_date_label Mar15");
        probe.Should().Contain("for attempt in $(seq 1 5)");
        probe.Should().Contain("copy_cell_formula_by_address A1");
        probe.Should().Contain("\"$anti_stale\" != \"Region\"");
        probe.Should().Contain("select_cell 0 1 date-grid-first-rendered-row");
        probe.Should().Contain("external X11 clipboard owner that can contend with Avalonia");
        probe.Should().Contain("Click and Copy that visible grid cell as independent");
        probe.Should().Contain("click 3");
        probe.Should().Contain("send_active_key Home Down Return");
        probe.Should().Contain("after-reopened-visible=$after_reopened_visible");
        probe.Should().Contain("after-reopened-semantic-a5=$after_reopened_semantic");
        probe.Should().Contain("copy_cell_formula_by_address A5");
        probe.Should().Contain("\"$after_reopened_visible\" == \"Mar15\"");
        probe.Should().Contain("\"$after_reopened_semantic\" == \"Mar15\"");
        probe.Should().NotContain("read_filtered_visible_date_labels");
        probe.Should().Contain("\"$active_title\" == \"Open Workbook\"");
        probe.Should().Contain("\"$active_pid\" == \"$main_pid\"");
        probe.Should().Contain("\" $baseline_ids \" != *\" $active_id \"*");
        probe.Should().Contain("accepted-dialog-window-id=");
        probe.Should().Contain("exact active dialog identity");
        probe.Should().Contain("before-dialog-title=$before_dialog_title");
        probe.Should().Contain("after-dialog-title=$after_dialog_title");
        probe.Should().NotContain("if (( after_windows > before_windows )); then before_dialog_open=true");
        probe.Should().Contain("[[ -n \"$main_pid\" ]] || return 1");
        probe.Should().Contain("-n \"$main_pid\" && -n \"$active_pid\"");
        probe.Should().Contain("autofilter-date-after-reopened.png;autofilter-date-after-reopened-grid-read.png;autofilter-date-postcondition.txt");
        probe.Should().Contain("                    \"$active_id\" \"$active_title\" \"$active_pid\" >> \"$diagnostics_path\"\n                fi\n                sleep 0.2");
        probe.Should().NotContain("                    \"$active_id\" \"$active_title\" \"$active_pid\" >> \"$diagnostics_path\"\n                    return 1");
        probe.Should().Contain("Date Filters > Before");
        probe.Should().Contain("Date Filters > After");
        probe.Should().Contain("for _ in $(seq 1 3)");
        probe.Should().Contain("screen_changed \"$output/autofilter-date-after-before.png\"");
        probe.Should().Contain("before-dialog-closed");
        probe.Should().Contain("after-dialog-closed");
        probe.Should().Contain("After applied/persisted checks: menu-open=$after_menu_open;");
        probe.Should().Contain("clean-save=$after_save_clean; package=$after_package;");
        probe.Should().Contain("reopen-dialog-open=$after_dialog_open;");
        probe.Should().Contain("reopen-dialog-title=$after_dialog_title;");
        probe.Should().Contain("reopen-dialog-closed=$after_dialog_closed; reopened-grid=$after_reopened_visible;");
        probe.Should().Contain("reopened-semantic-a5=$after_reopened_semantic.");
        probe.Should().NotContain("After did not prove the rendered date criteria commit");
    }
}
