using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R102: RenameSheetCommand and RemoveSheetCommand's CF/DV cross-sheet-reference rewrite only
/// touched <see cref="ConditionalFormat.FormulaText"/> and <see cref="DataValidation.Formula1"/>/
/// <see cref="DataValidation.Formula2"/> -- unlike RenameStructuredTableCommand's R100 fix, which
/// runs the shared <see cref="RowColumnShiftHelpers.RewriteRuleFormulas"/> helper that ALSO
/// rewrites colorScale/dataBar/iconSet cfvo threshold values whose ThresholdType is
/// <see cref="CfThresholdType.Formula"/> (e.g. a Color Scale "Formula" minimum of
/// "=Sheet2!$B$1"). Real Excel keeps such a threshold pointed at the renamed sheet under its new
/// name, and rewrites it to #REF! when that sheet is deleted, just like an ordinary CF formula.
/// </summary>
public sealed class R102_SheetRenameDeleteCfThresholdRewriteTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Data");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ══════════════════════════════════════════════════════════════════════
    // RenameSheetCommand: ColorScale Formula-type threshold
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameSheetCommand_RewritesColorScaleFormulaMinThreshold_AndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var target = wb.AddSheet("Sheet2");

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.ColorScale,
            MinThresholdType = CfThresholdType.Formula,
            MinThresholdValue = "Sheet2!$B$1",
        };
        sheet.ConditionalFormats.Add(cf);

        var command = new RenameSheetCommand(target.Id, "Renamed");
        command.Apply(ctx).Success.Should().BeTrue();

        cf.MinThresholdValue.Should().Be("Renamed!$B$1",
            because: "a Color Scale Formula-type minimum referencing the renamed sheet must follow the rename, " +
                      "just like RenameStructuredTableCommand's R100 fix does for the same threshold field");

        command.Revert(ctx);

        cf.MinThresholdValue.Should().Be("Sheet2!$B$1", because: "undo must restore the original threshold formula");
    }

    [Fact]
    public void RenameSheetCommand_RewritesIconSetFormulaThreshold_AndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var target = wb.AddSheet("Sheet2");

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.IconSet,
        };
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "0"));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Formula, "Sheet2!$C$1"));
        cf.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "67"));
        sheet.ConditionalFormats.Add(cf);

        var command = new RenameSheetCommand(target.Id, "Renamed");
        command.Apply(ctx).Success.Should().BeTrue();

        cf.IconSetThresholds[1].Value.Should().Be("Renamed!$C$1",
            because: "an Icon Set Formula-type per-icon threshold referencing the renamed sheet must follow the rename");

        command.Revert(ctx);

        cf.IconSetThresholds[1].Value.Should().Be("Sheet2!$C$1", because: "undo must restore the original threshold formula");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Sibling no-regression: cf.FormulaText and dv.Formula1/Formula2 still rewrite
    // (the pre-existing behavior this refactor must not break)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenameSheetCommand_StillRewritesFormulaCfAndDvFormulas_AndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var target = wb.AddSheet("Sheet2");

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "Sheet2!A1>0",
        };
        sheet.ConditionalFormats.Add(cf);

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 10, 2)),
            Formula1 = "Sheet2!$A$1",
            Formula2 = "Sheet2!$A$2",
        };
        sheet.DataValidations.Add(dv);

        var command = new RenameSheetCommand(target.Id, "Renamed");
        command.Apply(ctx).Success.Should().BeTrue();

        cf.FormulaText.Should().Be("Renamed!A1>0");
        dv.Formula1.Should().Be("Renamed!$A$1");
        dv.Formula2.Should().Be("Renamed!$A$2");

        command.Revert(ctx);

        cf.FormulaText.Should().Be("Sheet2!A1>0");
        dv.Formula1.Should().Be("Sheet2!$A$1");
        dv.Formula2.Should().Be("Sheet2!$A$2");
    }

    // ══════════════════════════════════════════════════════════════════════
    // RemoveSheetCommand: identical gap on the #REF! pass (named in the finding as
    // "RemoveSheetCommand's equivalent #REF!-rewrite pass ... has the identical gap")
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RemoveSheetCommand_RewritesDataBarFormulaThresholdToRef_AndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var target = wb.AddSheet("Sheet2");

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.DataBar,
            DataBarMinThresholdType = CfThresholdType.Formula,
            DataBarMinThresholdValue = "Sheet2!$B$1",
        };
        sheet.ConditionalFormats.Add(cf);

        var command = new RemoveSheetCommand(target.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        cf.DataBarMinThresholdValue.Should().Be("#REF!",
            because: "a Data Bar Formula-type minimum referencing the deleted sheet must become #REF!, " +
                      "mirroring the identical cf.FormulaText #REF! rewrite this command already performs");

        command.Revert(ctx);

        cf.DataBarMinThresholdValue.Should().Be("Sheet2!$B$1", because: "undo must restore the original threshold formula");
    }
}
