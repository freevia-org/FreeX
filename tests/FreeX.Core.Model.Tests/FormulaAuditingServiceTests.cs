using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FormulaAuditingServiceTests
{
    [Fact]
    public void FormulaErrorCheckingRuleCatalog_ListsSupportedOptionsInStableExcelLikeOrder()
    {
        FormulaErrorCheckingRuleCatalog.SupportedRules.Select(rule => (rule.ErrorCode, rule.Label))
            .Should().Equal(
                (ErrorValue.DivByZero.Code, "Formulas that divide by zero"),
                (ErrorValue.Value.Code, "Formulas with incompatible values"),
                (ErrorValue.Ref.Code, "Formulas with invalid cell references"),
                (ErrorValue.Name.Code, "Formulas with unrecognized names"),
                (ErrorValue.NA.Code, "Formulas returning #N/A"),
                (ErrorValue.Num.Code, "Formulas with invalid numbers"),
                (ErrorValue.Null.Code, "Formulas with invalid intersections"),
                (ErrorValue.Spill.Code, "Formulas with blocked spill ranges"),
                (ErrorValue.Circular.Code, "Formulas with circular references"),
                (FormulaAuditingService.FormulaStoredAsTextErrorCode, "Formulas stored as text"),
                (FormulaAuditingService.InconsistentCalculatedColumnFormulaErrorCode, "Inconsistent calculated column formula in tables"),
                (FormulaAuditingService.InconsistentFormulaErrorCode, "Formulas inconsistent with nearby formulas"),
                (FormulaAuditingService.FormulaOmitsAdjacentCellsErrorCode, "Formulas which omit cells in a region"),
                (FormulaAuditingService.UnlockedFormulaCellsErrorCode, "Unlocked cells containing formulas"),
                (FormulaAuditingService.FormulaRefersToBlankCellsErrorCode, "Formulas referring to blank cells"),
                (FormulaAuditingService.DataValidationErrorCode, "Data entered in cells is invalid"),
                (FormulaAuditingService.TwoDigitYearTextDateErrorCode, "Cells containing years represented as 2 digits"),
                (FormulaAuditingService.NumberStoredAsTextErrorCode, "Numbers formatted as text or preceded by an apostrophe"));
    }

    [Fact]
    public void Benchmark_SparseFormulaErrorIssues_ReportsTimingAndAllocatedBytes()
    {
        const int valueRows = 100_000;
        const int formulaRows = 2_000;
        const int steps = 3;

        var wb = new Workbook("perf");
        var sheet = wb.AddSheet("Sheet1");
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.NumberStoredAsTextErrorCode);
        wb.DisabledFormulaErrorCodes.Add(FormulaAuditingService.TwoDigitYearTextDateErrorCode);

        for (uint row = 1; row <= valueRows; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        for (uint row = 1; row <= formulaRows; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromFormula($"A{row}+1"));

        FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        var maxStepMs = 0d;

        for (var stepIndex = 0; stepIndex < steps; stepIndex++)
        {
            var step = Stopwatch.StartNew();
            FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id).Should().BeEmpty();
            step.Stop();
            maxStepMs = Math.Max(maxStepMs, step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var meanMs = total.Elapsed.TotalMilliseconds / steps;

        Console.WriteLine(
            $"PERF FORMULA_AUDIT_SPARSE_FORMULAS occupied={sheet.CellCount} formulas={sheet.FormulaCellCount} steps={steps} total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={meanMs:F2} max_ms={maxStepMs:F2} allocated_bytes={allocatedBytes}");
        allocatedBytes.Should().BeLessThan(6_500_000);
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, .. parts]);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException($"Could not find workspace file '{Path.Combine(parts)}'.");
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
