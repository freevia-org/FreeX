using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r163 remediation. The rename-resilience fix added a position fallback keyed on
/// <see cref="SlicerModel.SourceFieldIndex"/> -- but that is ONE field on the slicer, while
/// <see cref="SetSlicerSelectionCommand"/> loops over every pivot table the slicer is connected to
/// through Excel's Report Connections. The first version wrote the index back on every connected
/// pivot, so two pivots that share a field NAME at DIFFERENT positions clobbered each other, and the
/// fallback could then recover a position belonging to a sibling pivot -- self-healing
/// <see cref="SlicerModel.SourceFieldName"/> to an unrelated column and filtering the wrong field.
///
/// That is worse than the defect it replaced: the pre-fix behaviour merely left a connected pivot
/// stale, where this actively corrupted it. The fallback and its write-back are now both scoped to
/// the slicer's own source pivot.
/// </summary>
public sealed class R163_PivotSlicerMultiPivotIndexScopeTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    [Fact]
    public void ConnectedPivotWithTheSameFieldNameAtADifferentPosition_DoesNotHijackTheSlicerIndex()
    {
        var (workbook, sheet, sourcePivot, connectedPivot) = BuildTwoPivotsSharingASlicer();

        var addOutcome = new AddSlicerCommand("Region Slicer", "PT1", "Region")
            .Apply(new TestCommandContext(workbook));
        addOutcome.Success.Should().BeTrue(addOutcome.ErrorMessage);

        var slicer = workbook.Slicers.Single();
        slicer.ConnectedPivotTableNames.Add(connectedPivot.Name);

        // PT1 (the source) holds Region at column 0; PT2 holds Region at column 1. Resolving both must
        // leave the slicer's remembered position pointing at ITS OWN pivot's column, not PT2's.
        var outcome = new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        slicer.SourceFieldIndex.Should().Be(0, "the remembered position belongs to the slicer's source pivot");
        slicer.SourceFieldName.Should().Be("Region");
    }

    [Fact]
    public void RenamingOnlyTheConnectedPivotsHeader_LeavesTheSlicerBoundToItsOwnField()
    {
        var (workbook, sheet, sourcePivot, connectedPivot) = BuildTwoPivotsSharingASlicer();

        var addOutcome = new AddSlicerCommand("Region Slicer", "PT1", "Region")
            .Apply(new TestCommandContext(workbook));
        addOutcome.Success.Should().BeTrue(addOutcome.ErrorMessage);

        var slicer = workbook.Slicers.Single();
        slicer.ConnectedPivotTableNames.Add(connectedPivot.Name);
        new SetSlicerSelectionCommand("Region Slicer", ["East"]).Apply(new TestCommandContext(workbook));

        // Rename ONLY the connected pivot's Region header. The slicer's own source pivot is untouched,
        // so the slicer must keep resolving by name against PT1 and must NOT adopt a position or a
        // name from PT2.
        sheet.SetCell(Addr(sheet, "E1"), new TextValue("Territory"));
        PivotTableRefreshService.Refresh(workbook, sheet, connectedPivot, rescanCacheSharedItems: true);

        var outcome = new SetSlicerSelectionCommand("Region Slicer", ["West"]).Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // Before the remediation this came back as the sibling pivot's unrelated column caption.
        slicer.SourceFieldName.Should().Be("Region");
        slicer.SourceFieldIndex.Should().Be(0);
    }

    /// <summary>
    /// PT1 over A:B with Region in the FIRST column; PT2 over D:F with Region in the SECOND column, so
    /// a position borrowed from one pivot is provably wrong for the other.
    /// </summary>
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Source, PivotTableModel Connected)
        BuildTwoPivotsSharingASlicer()
    {
        var workbook = new Workbook("R163PivotSlicerMultiPivot");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        sheet.SetCell(Addr(sheet, "E1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "F1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "G1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "E2"), new TextValue("Hardware"));
        sheet.SetCell(Addr(sheet, "F2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "G2"), new NumberValue(5));
        sheet.SetCell(Addr(sheet, "E3"), new TextValue("Software"));
        sheet.SetCell(Addr(sheet, "F3"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "G3"), new NumberValue(7));

        var firstCache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3",
        };
        firstCache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"]));
        firstCache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(firstCache);

        var secondCache = new PivotCacheModel
        {
            CacheId = 2,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "E1:G3",
        };
        secondCache.Fields.Add(new PivotCacheFieldModel("Category", ContainsString: true, SharedItems: ["Hardware", "Software"]));
        secondCache.Fields.Add(new PivotCacheFieldModel("Region", ContainsString: true, SharedItems: ["East", "West"]));
        secondCache.Fields.Add(new PivotCacheFieldModel("Amount", ContainsNumber: true));
        workbook.PivotCaches.Add(secondCache);

        var source = new PivotTableModel
        {
            Name = "PT1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "A10", "C16"),
        };
        source.RowFields.Add(new PivotFieldModel(0));
        source.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(source);

        var connected = new PivotTableModel
        {
            Name = "PT2",
            CacheId = 2,
            SourceRange = Range(sheet, "E1", "G3"),
            TargetRange = Range(sheet, "E10", "G16"),
        };
        connected.RowFields.Add(new PivotFieldModel(1));
        connected.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(connected);

        PivotTableRefreshService.Refresh(workbook, sheet, source);
        PivotTableRefreshService.Refresh(workbook, sheet, connected);

        return (workbook, sheet, source, connected);
    }
}
