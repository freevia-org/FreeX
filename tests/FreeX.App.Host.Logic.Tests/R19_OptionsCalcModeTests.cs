using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

// R19-meta-2: the WPF Options > Formulas dialog collapsed WorkbookCalculationMode.AutomaticExceptDataTables
// into a lossy AutoCalculate bool.
//
// (a) OptionsDialogCalculationSettings.FromWorkbook used to seed AutoCalculate = (CalculationMode == Automatic),
//     so a workbook in AutomaticExceptDataTables mode showed "Manual calculation" checked in the dialog --
//     wrong, the workbook was actually auto-recalculating except data tables.
// (b) ApplyOptionsCalculationSettings (MainWindow.Backstage.cs) mapped AutoCalculate=false to
//     WorkbookCalculationMode.Manual and fired SetCalculationModeCommand whenever that computed mode didn't
//     equal the workbook's EXACT CalculationMode, so an unrelated Options edit (touching only the iterative-
//     calculation fields) silently downgraded AutomaticExceptDataTables to Automatic/Manual on OK.
//
// The fix seeds AutoCalculate from "CalculationMode != Manual" (so AutomaticExceptDataTables shows as Automatic,
// not Manual) and changes ApplyOptionsCalculationSettings to gate SetCalculationModeCommand on the boolean
// "currently Manual" state (mirroring the Avalonia host's MainWindow.Options.cs CalculationModeIsManual guard),
// applying the iterative-calculation block independently of the mode block. The FromWorkbook seeding below is
// the deterministic root-cause guard; part (b)'s apply-path guard is a direct read-verified mirror of the
// already-proven Avalonia path (a full STA MainWindow test of that private method is environmentally
// unreliable -- a headless MainWindow manages its own active-workbook lifecycle -- so it is not asserted here).
public sealed class R19_calc_mode_wpf_Tests
{
    [Fact]
    public void FromWorkbook_AutomaticExceptDataTables_SeedsAutoCalculateTrueNotManual()
    {
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        workbook.CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables;

        var settings = OptionsDialogCalculationSettings.FromWorkbook(workbook);

        // The dialog only has Automatic/Manual radios; AutomaticExceptDataTables must display as Automatic
        // (checked), never as Manual -- the pre-fix seeding showed Manual and let an unrelated edit downgrade it.
        settings.AutoCalculate.Should().BeTrue(
            "a workbook that auto-recalculates except data tables is not Manual, so the dialog " +
            "must not show the Manual radio checked");
        settings.CalculationMode.Should().Be(WorkbookCalculationMode.AutomaticExceptDataTables);
    }
}
