using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression tests for the finding: Sheet.Clone's ClonePivotTable unconditionally rewrote a
/// PivotTableModel's SourceRange (its data-source range, which can legitimately live on a
/// DIFFERENT sheet than the one hosting the pivot table -- Excel's normal "PivotTable on new
/// sheet, data on the original sheet" pattern) onto the newly duplicated sheet, instead of only
/// remapping it when the source range actually lived on the sheet being duplicated. This mirrors
/// the already-correct handling of ChartModel.DataRange in
/// <see cref="DuplicateSheetDrawingCloner"/>'s CloneChart (see DSheetObjectsRegressionTests'
/// DuplicateSheet_CrossSheetChartDataRange_StaysOnOriginalSheet /
/// DuplicateSheet_SameSheetChartDataRange_IsRemappedOntoCopy for the analogous chart tests).
/// </summary>
public sealed class R102_DuplicateSheetPivotSourceRangeTests
{
    [Fact]
    public void DuplicateSheet_CrossSheetPivotSourceRange_StaysOnOriginalSheet()
    {
        // 'Data' holds the pivot's source data; 'PivotSheet' hosts the rendered pivot table.
        // Duplicating PivotSheet must NOT remap the pivot's SourceRange onto the new copy --
        // Excel keeps a copied PivotTable's source-data reference pointing at the original sheet.
        var workbook = new Workbook("test");
        var data = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("PivotSheet");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(data.Id, 1, 1),
            new CellAddress(data.Id, 100, 4));
        var targetRange = new GridRange(
            new CellAddress(pivotSheet.Id, 1, 1),
            new CellAddress(pivotSheet.Id, 5, 3));

        pivotSheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        });

        var command = new DuplicateSheetCommand(pivotSheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[2];
        copy.Name.Should().Be("PivotSheet (2)");
        var copiedPivot = copy.PivotTables.Should().ContainSingle().Subject;

        copiedPivot.SourceRange.Should().Be(sourceRange,
            because: "a pivot SourceRange pointing at another sheet must not be remapped onto the duplicate");

        // TargetRange (the pivot's own rendered location) always lives on the hosting sheet, so
        // it must travel with the copy -- this is the sibling behaviour the fix must not break.
        copiedPivot.TargetRange.Start.Sheet.Should().Be(copy.Id);
        copiedPivot.TargetRange.End.Sheet.Should().Be(copy.Id);
        copiedPivot.TargetRange.Start.Row.Should().Be(targetRange.Start.Row);
        copiedPivot.TargetRange.End.Row.Should().Be(targetRange.End.Row);
    }

    [Fact]
    public void DuplicateSheet_SameSheetPivotSourceRange_IsRemappedOntoCopy()
    {
        // When the pivot's SourceRange points at the sheet being duplicated itself, the copy's
        // pivot must point at the copy's own data (matching Excel: same-sheet refs travel with it).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 2));
        var targetRange = new GridRange(
            new CellAddress(sheet.Id, 1, 5),
            new CellAddress(sheet.Id, 5, 7));

        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = sourceRange,
            TargetRange = targetRange
        });

        var command = new DuplicateSheetCommand(sheet.Id);
        command.Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1];
        var copiedPivot = copy.PivotTables.Should().ContainSingle().Subject;

        copiedPivot.SourceRange.Start.Sheet.Should().Be(copy.Id);
        copiedPivot.SourceRange.End.Sheet.Should().Be(copy.Id);
        copiedPivot.SourceRange.Start.Row.Should().Be(sourceRange.Start.Row);
        copiedPivot.SourceRange.End.Row.Should().Be(sourceRange.End.Row);

        copiedPivot.TargetRange.Start.Sheet.Should().Be(copy.Id);
        copiedPivot.TargetRange.End.Sheet.Should().Be(copy.Id);
    }
}
