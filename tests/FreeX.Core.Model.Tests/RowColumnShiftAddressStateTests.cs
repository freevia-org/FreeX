using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class RowColumnShiftAddressStateTests
{
    [Fact]
    public void InsertRows_ShiftsRemainingAddressBearingStateAndUndoRestores()
    {
        var (workbook, sheet, ctx) = Setup();
        var style = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(5, 2, style);
        sheet.RowOutlineLevels[5] = 2;
        sheet.GroupHiddenRows.Add(5);
        sheet.CollapsedAnchorRows.Add(6);
        sheet.AllowEditRanges.Add(Range(sheet, 5, 1, 6, 2));
        sheet.PrintTitleRows = new WorksheetRepeatRange(5, 6);
        sheet.RowPageBreaksMetadata = PageBreakMetadata(5);
        workbook.WatchedCells.Add(Addr(sheet, 5, 3));
        sheet.CellWatchesMetadata = CellWatchMetadata("C5");
        sheet.IgnoredErrorsMetadata = IgnoredErrorMetadata("B5:C6");
        sheet.AutoFilter = new WorksheetAutoFilterModel("A5:C8", "<autoFilter ref=\"A5:C8\"/>");
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["East"]));
        sheet.SortState = SortState("A5:C8", "B5:B8");
        sheet.SmartTags = SmartTags("B5");
        sheet.SingleXmlCells = SingleXmlCells("C5");
        sheet.DataConsolidation = DataConsolidation(sheet.Name, "A5:C8");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = Addr(sheet, 5, 1), Text = "note" });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = Addr(sheet, 6, 1), Kind = DrawingShapeKind.Rectangle });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = Addr(sheet, 5, 4),
            IsLinkedToSourceRange = true,
            LinkedSourceRange = Range(sheet, 5, 1, 6, 2)
        });
        sheet.Sparklines.Add(new SparklineModel { Location = Addr(sheet, 5, 5), DataRange = Range(sheet, 5, 1, 8, 1) });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = Range(sheet, 5, 1, 8, 3),
            TargetRange = Range(sheet, 10, 1, 12, 3)
        });
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 5, 1, 8, 3)
        });
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A5:C8"
        });
        workbook.Scenarios.Add(new WorkbookScenario("Case", [new ScenarioCellValue(Addr(sheet, 5, 2), new NumberValue(10))]));

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 4, count: 2);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetStyleOnly(7, 2).Should().Be(style);
        sheet.GetStyleOnly(5, 2).Should().BeNull();
        sheet.RowOutlineLevels[7].Should().Be(2);
        sheet.GroupHiddenRows.Should().Contain(7);
        sheet.CollapsedAnchorRows.Should().Contain(8);
        sheet.AllowEditRanges.Should().ContainSingle().Which.Should().Be(Range(sheet, 7, 1, 8, 2));
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(7, 8));
        sheet.RowPageBreaksMetadata!.BreakNativeAttributes.Should().ContainKey(7);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(Addr(sheet, 7, 3));
        sheet.CellWatchesMetadata!.WatchNativeAttributes.Should().ContainKey("C7");
        sheet.IgnoredErrorsMetadata!.ErrorNativeAttributes.Should().ContainKey("B7:C8");
        sheet.AutoFilter!.Reference.Should().Be("A7:C10");
        sheet.AutoFilter.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(1);
        sheet.SortState!.Reference.Should().Be("A7:C10");
        sheet.SortState.Conditions.Should().ContainSingle().Which.Reference.Should().Be("B7:B10");
        sheet.SmartTags!.Cells.Should().ContainSingle().Which.Reference.Should().Be("B7");
        sheet.SingleXmlCells!.Cells.Should().ContainSingle().Which.Reference.Should().Be("C7");
        sheet.DataConsolidation!.References.Should().ContainSingle().Which.Reference.Should().Be("A7:C10");
        sheet.TextBoxes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 7, 1));
        sheet.DrawingShapes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 8, 1));
        sheet.Pictures.Should().ContainSingle().Which.LinkedSourceRange.Should().Be(Range(sheet, 7, 1, 8, 2));
        sheet.Sparklines.Should().ContainSingle().Which.Location.Should().Be(Addr(sheet, 7, 5));
        sheet.PivotTables.Should().ContainSingle().Which.SourceRange.Should().Be(Range(sheet, 7, 1, 10, 3));
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(Range(sheet, 7, 1, 10, 3));
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("A7:C10");
        workbook.Scenarios.Should().ContainSingle().Which.ChangingCells.Should().ContainSingle()
            .Which.Address.Should().Be(Addr(sheet, 7, 2));

        command.Revert(ctx);

        sheet.GetStyleOnly(5, 2).Should().Be(style);
        sheet.RowOutlineLevels[5].Should().Be(2);
        sheet.GroupHiddenRows.Should().Contain(5);
        sheet.CollapsedAnchorRows.Should().Contain(6);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(Addr(sheet, 5, 3));
        sheet.AutoFilter!.Reference.Should().Be("A5:C8");
        sheet.TextBoxes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 5, 1));
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(Range(sheet, 5, 1, 8, 3));
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("A5:C8");
    }

    // R114-outline-group-insert-extend-1: grouping rows 3-8 at level 1 (Data > Group in Excel)
    // creates ONE contiguous collapsible band. Inserting a row strictly inside that band (row 5,
    // between the group's boundaries) must extend the band to cover the new row instead of leaving
    // it at implicit level 0 -- which would split the single group into two separate runs on either
    // side of the insertion point (verified against real Excel's Insert Sheet Rows behavior; see
    // RowOutlineGroupScope.Resolve in GroupRowsCommand.cs, which detects group membership purely
    // from contiguous same-or-deeper outline levels). The group here is also fully collapsed
    // (GroupHiddenRows covers every detail row, CollapsedAnchorRows marks the summary row below the
    // run), so the newly-inserted row must join the hidden set too or the collapsed band would show
    // a visible gap.
    [Fact]
    public void R114_InsertRows_StrictlyInsideGroup_ExtendsOutlineLevelAndHiddenStateToNewRow()
    {
        var (workbook, sheet, ctx) = Setup();
        for (uint r = 3; r <= 8; r++)
        {
            sheet.RowOutlineLevels[r] = 1;
            sheet.GroupHiddenRows.Add(r);
        }
        sheet.CollapsedAnchorRows.Add(9);

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 5, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        // The whole band (old 3,4 untouched + new 5 + old 5..8 now shifted to 6..9) must share
        // level 1 with no gap, and the newly-inserted row must be hidden like the rest of the
        // (still fully collapsed) run.
        for (uint r = 3; r <= 9; r++)
        {
            sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1, because: $"row {r} must remain part of the single extended group");
            sheet.GroupHiddenRows.Should().Contain(r, because: $"row {r} must stay hidden by the still-collapsed extended group");
        }
        sheet.CollapsedAnchorRows.Should().Contain(10);

        command.Revert(ctx);

        // Undo must restore the pre-insert group exactly (rows 3-8 at level 1, no row 5-only entry).
        for (uint r = 3; r <= 8; r++)
            sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1);
        sheet.RowOutlineLevels.Should().NotContainKey(9);
        sheet.CollapsedAnchorRows.Should().Contain(9);
    }

    // Sibling/no-regression coverage: inserting a row immediately ABOVE an existing group (not
    // strictly inside it -- the row above the insertion point is outside the group) must NOT pull
    // the new row into the group, mirroring Excel (a row inserted before a group's first row does
    // not become part of that group).
    [Fact]
    public void R114_InsertRows_AtGroupTopBoundary_DoesNotExtendGroupToNewRow()
    {
        var (workbook, sheet, ctx) = Setup();
        for (uint r = 3; r <= 8; r++)
            sheet.RowOutlineLevels[r] = 1;

        var command = new InsertRowsCommand(sheet.Id, beforeRow: 3, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.RowOutlineLevels.Should().NotContainKey(3, because: "the new row sits above the group, not inside it");
        for (uint r = 4; r <= 9; r++)
            sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1);
    }

    // Sibling coverage: the identical fix applies to the Columns axis (ColOutlineLevels /
    // GroupHiddenCols), reached via InsertColumnsCommand through the same
    // ShiftOutlineAndGroupCollections choke point.
    [Fact]
    public void R114_InsertColumns_StrictlyInsideGroup_ExtendsOutlineLevelToNewColumn()
    {
        var (workbook, sheet, ctx) = Setup();
        for (uint c = 3; c <= 8; c++)
            sheet.ColOutlineLevels[c] = 1;

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 1);
        command.Apply(ctx).Success.Should().BeTrue();

        for (uint c = 3; c <= 9; c++)
            sheet.ColOutlineLevels.Should().ContainKey(c).WhoseValue.Should().Be(1, because: $"column {c} must remain part of the single extended group");
    }

    [Fact]
    public void DeleteRows_ShiftsAndRemovesRemainingAddressBearingStateAndUndoRestores()
    {
        var (workbook, sheet, ctx) = Setup();
        var style = workbook.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetStyleOnly(3, 2, style);
        sheet.SetStyleOnly(6, 2, style);
        sheet.RowOutlineLevels[6] = 3;
        sheet.GroupHiddenRows.Add(6);
        sheet.CollapsedAnchorRows.Add(7);
        workbook.WatchedCells.AddRange([Addr(sheet, 3, 3), Addr(sheet, 6, 3)]);
        sheet.CellWatchesMetadata = CellWatchMetadata("C3", "C6");
        sheet.AutoFilter = new WorksheetAutoFilterModel("A6:C8", null);
        sheet.SortState = SortState("A6:C8", "B6:B8");
        sheet.SmartTags = SmartTags("B3", "B6");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = Addr(sheet, 6, 1), Text = "keep" });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = Addr(sheet, 3, 1), Kind = DrawingShapeKind.Rectangle });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = Addr(sheet, 6, 4),
            IsLinkedToSourceRange = true,
            LinkedSourceRange = Range(sheet, 6, 1, 7, 2)
        });
        sheet.Sparklines.Add(new SparklineModel { Location = Addr(sheet, 6, 5), DataRange = Range(sheet, 6, 1, 7, 1) });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = Range(sheet, 6, 1, 8, 3),
            TargetRange = Range(sheet, 9, 1, 10, 3)
        });
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 6, 1, 8, 3)
        });
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "A6:C8"
        });
        workbook.Scenarios.Add(new WorkbookScenario("Case",
        [
            new ScenarioCellValue(Addr(sheet, 3, 2), new NumberValue(1)),
            new ScenarioCellValue(Addr(sheet, 6, 2), new NumberValue(2))
        ]));

        var command = new DeleteRowsCommand(sheet.Id, startRow: 3, count: 2);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetStyleOnly(3, 2).Should().BeNull();
        sheet.GetStyleOnly(4, 2).Should().Be(style);
        sheet.RowOutlineLevels[4].Should().Be(3);
        sheet.GroupHiddenRows.Should().Contain(4);
        sheet.CollapsedAnchorRows.Should().Contain(5);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(Addr(sheet, 4, 3));
        sheet.CellWatchesMetadata!.WatchNativeAttributes.Should().ContainKey("C4");
        sheet.CellWatchesMetadata.WatchNativeAttributes.Should().NotContainKey("C3");
        sheet.AutoFilter!.Reference.Should().Be("A4:C6");
        sheet.SortState!.Conditions.Should().ContainSingle().Which.Reference.Should().Be("B4:B6");
        sheet.SmartTags!.Cells.Select(cell => cell.Reference).Should().Equal("B4");
        sheet.TextBoxes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 4, 1));
        sheet.DrawingShapes.Should().BeEmpty();
        sheet.Pictures.Should().ContainSingle().Which.LinkedSourceRange.Should().Be(Range(sheet, 4, 1, 5, 2));
        sheet.Sparklines.Should().ContainSingle().Which.DataRange.Should().Be(Range(sheet, 4, 1, 5, 1));
        sheet.PivotTables.Should().ContainSingle().Which.TargetRange.Should().Be(Range(sheet, 7, 1, 8, 3));
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(Range(sheet, 4, 1, 6, 3));
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("A4:C6");
        workbook.Scenarios.Should().ContainSingle().Which.ChangingCells.Should().ContainSingle()
            .Which.Address.Should().Be(Addr(sheet, 4, 2));

        command.Revert(ctx);

        sheet.GetStyleOnly(3, 2).Should().Be(style);
        sheet.GetStyleOnly(6, 2).Should().Be(style);
        sheet.CollapsedAnchorRows.Should().Contain(7);
        workbook.WatchedCells.Should().Equal(Addr(sheet, 3, 3), Addr(sheet, 6, 3));
        sheet.DrawingShapes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 3, 1));
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(Range(sheet, 6, 1, 8, 3));
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("A6:C8");
    }

    [Fact]
    public void InsertColumns_ShiftsRemainingAddressBearingStateAndUndoRestores()
    {
        var (workbook, sheet, ctx) = Setup();
        var style = workbook.RegisterStyle(new CellStyle { Underline = true });
        sheet.SetStyleOnly(2, 5, style);
        sheet.ColOutlineLevels[5] = 2;
        sheet.GroupHiddenCols.Add(5);
        sheet.CollapsedAnchorCols.Add(6);
        workbook.WatchedCells.Add(Addr(sheet, 2, 5));
        sheet.CellWatchesMetadata = CellWatchMetadata("E2");
        sheet.AutoFilter = new WorksheetAutoFilterModel("B2:D5", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["East"]));
        sheet.SortState = SortState("B2:D5", "C2:C5");
        sheet.SmartTags = SmartTags("E2");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = Addr(sheet, 2, 5), Text = "note" });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = Addr(sheet, 3, 5), Kind = DrawingShapeKind.Rectangle });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = Addr(sheet, 4, 5),
            IsLinkedToSourceRange = true,
            LinkedSourceRange = Range(sheet, 2, 5, 3, 6)
        });
        sheet.Sparklines.Add(new SparklineModel { Location = Addr(sheet, 5, 5), DataRange = Range(sheet, 2, 5, 4, 5) });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = Range(sheet, 2, 5, 5, 7),
            TargetRange = Range(sheet, 7, 5, 8, 7)
        });
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 2, 5, 5, 7)
        });
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "E2:G5"
        });

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 3, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetStyleOnly(2, 6).Should().Be(style);
        sheet.ColOutlineLevels[6].Should().Be(2);
        sheet.GroupHiddenCols.Should().Contain(6);
        sheet.CollapsedAnchorCols.Should().Contain(7);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(Addr(sheet, 2, 6));
        sheet.CellWatchesMetadata!.WatchNativeAttributes.Should().ContainKey("F2");
        sheet.AutoFilter!.Reference.Should().Be("B2:E5");
        sheet.AutoFilter.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(2);
        sheet.SortState!.Conditions.Should().ContainSingle().Which.Reference.Should().Be("D2:D5");
        sheet.SmartTags!.Cells.Should().ContainSingle().Which.Reference.Should().Be("F2");
        sheet.TextBoxes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 2, 6));
        sheet.Pictures.Should().ContainSingle().Which.LinkedSourceRange.Should().Be(Range(sheet, 2, 6, 3, 7));
        sheet.Sparklines.Should().ContainSingle().Which.DataRange.Should().Be(Range(sheet, 2, 6, 4, 6));
        sheet.PivotTables.Should().ContainSingle().Which.SourceRange.Should().Be(Range(sheet, 2, 6, 5, 8));
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(Range(sheet, 2, 6, 5, 8));
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("F2:H5");

        command.Revert(ctx);

        sheet.GetStyleOnly(2, 5).Should().Be(style);
        sheet.ColOutlineLevels[5].Should().Be(2);
        sheet.CollapsedAnchorCols.Should().Contain(6);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(Addr(sheet, 2, 5));
        sheet.AutoFilter!.Reference.Should().Be("B2:D5");
        sheet.AutoFilter.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(1);
    }

    [Fact]
    public void DeleteColumns_ShiftsAndRemovesRemainingAddressBearingStateAndUndoRestores()
    {
        var (workbook, sheet, ctx) = Setup();
        var style = workbook.RegisterStyle(new CellStyle { Strikethrough = true });
        sheet.SetStyleOnly(2, 3, style);
        sheet.SetStyleOnly(2, 5, style);
        sheet.ColOutlineLevels[5] = 2;
        sheet.GroupHiddenCols.Add(5);
        sheet.CollapsedAnchorCols.Add(6);
        workbook.WatchedCells.AddRange([Addr(sheet, 2, 3), Addr(sheet, 2, 5)]);
        sheet.CellWatchesMetadata = CellWatchMetadata("C2", "E2");
        sheet.AutoFilter = new WorksheetAutoFilterModel("B2:E5", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["Deleted"]));
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(3, ["Kept"]));
        sheet.SortState = SortState("B2:E5", "E2:E5");
        sheet.SmartTags = SmartTags("C2", "E2");
        sheet.TextBoxes.Add(new TextBoxModel { Anchor = Addr(sheet, 2, 5), Text = "keep" });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = Addr(sheet, 2, 3), Kind = DrawingShapeKind.Rectangle });
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = Addr(sheet, 4, 5),
            IsLinkedToSourceRange = true,
            LinkedSourceRange = Range(sheet, 2, 5, 3, 6)
        });
        sheet.Sparklines.Add(new SparklineModel { Location = Addr(sheet, 5, 5), DataRange = Range(sheet, 2, 5, 4, 5) });
        sheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            CacheId = 1,
            SourceRange = Range(sheet, 2, 5, 5, 7),
            TargetRange = Range(sheet, 7, 5, 8, 7)
        });
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = Range(sheet, 2, 5, 5, 7)
        });
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sheet.Name,
            SourceReference = "E2:G5"
        });

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 3, count: 1);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetStyleOnly(2, 3).Should().BeNull();
        sheet.GetStyleOnly(2, 4).Should().Be(style);
        sheet.ColOutlineLevels[4].Should().Be(2);
        sheet.GroupHiddenCols.Should().Contain(4);
        sheet.CollapsedAnchorCols.Should().Contain(5);
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(Addr(sheet, 2, 4));
        sheet.CellWatchesMetadata!.WatchNativeAttributes.Should().ContainKey("D2");
        sheet.CellWatchesMetadata.WatchNativeAttributes.Should().NotContainKey("C2");
        sheet.AutoFilter!.Reference.Should().Be("B2:D5");
        sheet.AutoFilter.FilterColumns.Should().ContainSingle().Which.ColumnId.Should().Be(2);
        sheet.SortState!.Conditions.Should().ContainSingle().Which.Reference.Should().Be("D2:D5");
        sheet.SmartTags!.Cells.Select(cell => cell.Reference).Should().Equal("D2");
        sheet.TextBoxes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 2, 4));
        sheet.DrawingShapes.Should().BeEmpty();
        sheet.Pictures.Should().ContainSingle().Which.LinkedSourceRange.Should().Be(Range(sheet, 2, 4, 3, 5));
        sheet.Sparklines.Should().ContainSingle().Which.DataRange.Should().Be(Range(sheet, 2, 4, 4, 4));
        sheet.PivotTables.Should().ContainSingle().Which.SourceRange.Should().Be(Range(sheet, 2, 4, 5, 6));
        sheet.StructuredTables.Should().ContainSingle().Which.Range.Should().Be(Range(sheet, 2, 4, 5, 6));
        workbook.PivotCaches.Should().ContainSingle().Which.SourceReference.Should().Be("D2:F5");

        command.Revert(ctx);

        sheet.GetStyleOnly(2, 3).Should().Be(style);
        sheet.GetStyleOnly(2, 5).Should().Be(style);
        sheet.CollapsedAnchorCols.Should().Contain(6);
        workbook.WatchedCells.Should().Equal(Addr(sheet, 2, 3), Addr(sheet, 2, 5));
        sheet.DrawingShapes.Should().ContainSingle().Which.Anchor.Should().Be(Addr(sheet, 2, 3));
        sheet.AutoFilter!.Reference.Should().Be("B2:E5");
        sheet.AutoFilter.FilterColumns.Select(column => column.ColumnId).Should().Equal(1, 3);
    }

    [BenchmarkFact]
    public void Benchmark_InsertRowsWithStyleOnlyCells_ReportsTiming()
    {
        const int iterations = 5;
        var (workbook, sheet, ctx) = SetupStyleOnlyShiftWorkbook();

        var warmup = new InsertRowsCommand(sheet.Id, beforeRow: 20);
        warmup.Apply(ctx).Success.Should().BeTrue();
        warmup.Revert(ctx);
        sheet.GetStyleOnlyEntries().Should().HaveCount(StyleOnlyShiftRows * StyleOnlyShiftColumns);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var command = new InsertRowsCommand(sheet.Id, beforeRow: 20);
            var step = Stopwatch.StartNew();
            command.Apply(ctx).Success.Should().BeTrue();
            command.Revert(ctx);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        sheet.GetStyleOnlyEntries().Should().HaveCount(StyleOnlyShiftRows * StyleOnlyShiftColumns);
        workbook.SheetCount.Should().Be(1);
        Console.WriteLine(
            "PERF STYLE_ONLY_ROW_SHIFT " +
            $"rows={StyleOnlyShiftRows} cols={StyleOnlyShiftColumns} " +
            $"style_only_cells={StyleOnlyShiftRows * StyleOnlyShiftColumns} steps={iterations} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={timings.Average():F2} " +
            $"p95_ms={p95:F2} max_ms={ordered[^1]:F2} allocated_bytes={allocatedBytes:N0}");

        timings.Average().Should().BeGreaterThan(0);
    }

    [Fact]
    public void AddressStateStyleOnlyClear_UsesSheetClearAllPath()
    {
        var source = ModelSourceTestSupport.ReadCommandsSourceFromCurrentDirectoryOrFallback(
            "RowColumnShiftHelpers.AddressState.cs");

        source.Should().Contain("sheet.ClearStyleOnlyEntries();");
        source.Should().NotContain("GetStyleOnlyEntries().ToList()");
    }

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) Setup()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private const int StyleOnlyShiftRows = 800;
    private const int StyleOnlyShiftColumns = 80;

    private static (Workbook Workbook, Sheet Sheet, ICommandContext Context) SetupStyleOnlyShiftWorkbook()
    {
        var workbook = new Workbook("style-only shift perf");
        var sheet = workbook.AddSheet("Sheet1");
        var style = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = CellColor.FromArgb(225, 239, 218)
        });

        for (uint row = 1; row <= StyleOnlyShiftRows; row++)
        {
            for (uint col = 1; col <= StyleOnlyShiftColumns; col++)
                sheet.SetStyleOnly(row, col, style);
        }

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    private static CellAddress Addr(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static WorksheetPageBreaksMetadataModel PageBreakMetadata(uint index) =>
        new()
        {
            BreakNativeAttributes =
            {
                [index] = new Dictionary<string, string> { ["id"] = index.ToString() }
            }
        };

    private static WorksheetCellWatchesMetadataModel CellWatchMetadata(params string[] references)
    {
        var metadata = new WorksheetCellWatchesMetadataModel();
        foreach (var reference in references)
            metadata.WatchNativeAttributes[reference] = new Dictionary<string, string> { ["xr:uid"] = reference };

        return metadata;
    }

    private static WorksheetIgnoredErrorsMetadataModel IgnoredErrorMetadata(string reference) =>
        new()
        {
            ErrorNativeAttributes =
            {
                [reference] = new Dictionary<string, string> { ["numberStoredAsText"] = "1" }
            }
        };

    private static WorksheetSortStateModel SortState(string reference, string conditionReference) =>
        new()
        {
            Reference = reference,
            NativeXml = $"<sortState ref=\"{reference}\" />",
            Conditions =
            {
                new WorksheetSortConditionModel { Reference = conditionReference }
            }
        };

    private static WorksheetSmartTagsModel SmartTags(params string[] references)
    {
        var smartTags = new WorksheetSmartTagsModel { NativeXml = "<smartTags />" };
        foreach (var reference in references)
            smartTags.Cells.Add(new WorksheetCellSmartTagsModel { Reference = reference });

        return smartTags;
    }

    private static WorksheetSingleXmlCellsModel SingleXmlCells(string reference) =>
        new()
        {
            Cells =
            {
                new WorksheetSingleXmlCellModel { Reference = reference, Id = 1 }
            }
        };

    private static WorksheetDataConsolidationModel DataConsolidation(string sheetName, string reference) =>
        new()
        {
            NativeXml = "<dataConsolidate />",
            References =
            {
                new WorksheetDataConsolidationReferenceModel { Sheet = sheetName, Reference = reference }
            }
        };
}
