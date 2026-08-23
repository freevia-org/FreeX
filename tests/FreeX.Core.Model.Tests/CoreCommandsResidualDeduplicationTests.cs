using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class CoreCommandsResidualDeduplicationTests
{
    [Fact]
    public void GridRangeSubtraction_PreservesInclusiveEdgesAndStableSplitOrder()
    {
        var sheet = SheetId.New();
        var source = Range(sheet, 2, 2, 5, 5);
        var remove = Range(sheet, 3, 3, 4, 4);

        GridRangeSubtraction.Subtract(source, remove).Should().Equal(
            Range(sheet, 2, 2, 2, 5),
            Range(sheet, 5, 2, 5, 5),
            Range(sheet, 3, 2, 4, 2),
            Range(sheet, 3, 5, 4, 5));
    }

    [Fact]
    public void GridRangeSubtraction_HandlesDisjointWholeEdgeAndEnclosingRemoval()
    {
        var sheet = SheetId.New();
        var otherSheet = SheetId.New();
        var source = Range(sheet, 2, 2, 5, 5);

        GridRangeSubtraction.Subtract(source, Range(sheet, 8, 8, 9, 9)).Should().Equal(source);
        GridRangeSubtraction.Subtract(source, Range(otherSheet, 2, 2, 5, 5)).Should().Equal(source);
        GridRangeSubtraction.Subtract(source, source).Should().BeEmpty();
        GridRangeSubtraction.Subtract(source, Range(sheet, 0, 0, 9, 9)).Should().BeEmpty();
        GridRangeSubtraction.Subtract(source, Range(sheet, 2, 2, 2, 5)).Should().Equal(
            Range(sheet, 3, 2, 5, 5));
        GridRangeSubtraction.Subtract(source, Range(sheet, 2, 2, 5, 2)).Should().Equal(
            Range(sheet, 2, 3, 5, 5));
    }

    [Theory]
    [InlineData("apple", "Apple", -1)]
    [InlineData("Apple", "apple", 1)]
    [InlineData("apple", "banana", -1)]
    [InlineData("banana", "apple", 1)]
    [InlineData("same", "same", 0)]
    [InlineData("a", "aa", -1)]
    public void CaseSensitiveSortComparison_UsesAlphabeticPrimaryAndLowercaseTieBreak(
        string left,
        string right,
        int expectedSign)
    {
        Math.Sign(CaseSensitiveSortComparison.Compare(left, right)).Should().Be(expectedSign);
    }

    [Fact]
    public void CellEditCompanionSnapshot_RestoresCellStyleAndAllCompanionEntries()
    {
        var workbook = new Workbook("snapshot");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        var style = workbook.RegisterStyle(new CellStyle { Bold = true });
        var cell = Cell.FromValue(new TextValue("before"));
        cell.StyleId = style;
        var runs = new[] { new CellTextRun("before", true, null, null, null, null, null, null) };
        var metadata = new HyperlinkMetadata(ScreenTip: "before tip");
        var guide = new CellPhoneticGuide(["<rPh sb=\"0\" eb=\"1\"><t>x</t></rPh>"], null);
        sheet.SetCell(address, cell);
        sheet.RichTextRuns[address] = runs;
        sheet.Hyperlinks[address] = "https://before.example";
        sheet.HyperlinkMetadata[address] = metadata;
        sheet.CellPhoneticGuides[address] = guide;

        var snapshot = CellEditCompanionSnapshot.Capture(sheet, address);
        sheet.SetCell(address, new TextValue("after"));
        sheet.RichTextRuns.Remove(address);
        sheet.Hyperlinks.Remove(address);
        sheet.HyperlinkMetadata.Remove(address);
        sheet.CellPhoneticGuides.Remove(address);

        snapshot.Restore(sheet);

        sheet.GetCell(address)!.Value.Should().Be(new TextValue("before"));
        sheet.GetCell(address)!.StyleId.Should().Be(style);
        sheet.RichTextRuns[address].Should().BeSameAs(runs);
        sheet.Hyperlinks[address].Should().Be("https://before.example");
        sheet.HyperlinkMetadata[address].Should().Be(metadata);
        sheet.CellPhoneticGuides[address].Should().Be(guide);
    }

    [Fact]
    public void CellEditCommands_UndoRestoreCompanionEntriesThroughSharedSnapshot()
    {
        foreach (var usePaste in new[] { false, true })
        {
            var workbook = new Workbook(usePaste ? "paste" : "edit");
            var sheet = workbook.AddSheet("Sheet1");
            var address = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(address, new TextValue("before"));
            sheet.Hyperlinks[address] = "https://before.example";
            sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(ScreenTip: "before");
            sheet.RichTextRuns[address] = [new CellTextRun("before", true, null, null, null, null, null, null)];
            var context = new TestCommandContext(workbook);
            IWorkbookCommand command = usePaste
                ? new PasteCellsCommand(sheet.Id, [(address, Cell.FromValue(new TextValue("after")))])
                : new EditCellsCommand(sheet.Id, address, new TextValue("after"));

            command.Apply(context).Success.Should().BeTrue();
            command.Revert(context);

            sheet.GetValue(address).Should().Be(new TextValue("before"));
            sheet.Hyperlinks[address].Should().Be("https://before.example");
            sheet.HyperlinkMetadata[address].ScreenTip.Should().Be("before");
            sheet.RichTextRuns[address].Single().Text.Should().Be("before");
        }
    }

    [Fact]
    public void RemainingCellEditCommands_UndoRestoreTupleSpecificCompanionState()
    {
        var workbook = new Workbook("remaining snapshots");
        var sourceSheet = workbook.AddSheet("Source");
        var groupedSheet = workbook.AddSheet("Grouped");
        var context = new TestCommandContext(workbook);
        var source = new CellAddress(sourceSheet.Id, 2, 2);
        var grouped = new CellAddress(groupedSheet.Id, 2, 2);
        var style = workbook.RegisterStyle(new CellStyle { Italic = true });
        groupedSheet.SetStyleOnly(grouped.Row, grouped.Col, style);
        groupedSheet.Hyperlinks[grouped] = "https://grouped.example";
        groupedSheet.HyperlinkMetadata[grouped] = new HyperlinkMetadata(ScreenTip: "grouped");
        groupedSheet.RichTextRuns[grouped] = [new CellTextRun("grouped", true, null, null, null, null, null, null)];
        groupedSheet.CellPhoneticGuides[grouped] = new CellPhoneticGuide(["guide"], null);
        var groupedCommand = new GroupedEditCellsCommand(
            [groupedSheet.Id],
            sourceSheet.Id,
            [(source, Cell.FromValue(new TextValue("replacement")))]);

        groupedCommand.Apply(context).Success.Should().BeTrue();
        groupedCommand.Revert(context);

        groupedSheet.GetCell(grouped).Should().BeNull();
        groupedSheet.GetStyleOnly(grouped.Row, grouped.Col).Should().Be(style);
        groupedSheet.Hyperlinks[grouped].Should().Be("https://grouped.example");
        groupedSheet.HyperlinkMetadata[grouped].ScreenTip.Should().Be("grouped");
        groupedSheet.RichTextRuns[grouped].Single().Text.Should().Be("grouped");
        groupedSheet.CellPhoneticGuides[grouped].RunPhoneticXmls.Should().Equal("guide");

        var destination = new CellAddress(groupedSheet.Id, 4, 4);
        groupedSheet.SetCell(destination, new NumberValue(10));
        groupedSheet.Hyperlinks[destination] = "https://paste.example";
        groupedSheet.CellPhoneticGuides[destination] = new CellPhoneticGuide(["paste-guide"], null);
        var pasteCommand = new PasteSpecialCellsCommand(
            groupedSheet.Id,
            new GridRange(source, source),
            [(source, Cell.FromValue(new NumberValue(5)))],
            destination,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        pasteCommand.Apply(context).Success.Should().BeTrue();
        pasteCommand.Revert(context);

        groupedSheet.GetValue(destination).Should().Be(new NumberValue(10));
        groupedSheet.Hyperlinks[destination].Should().Be("https://paste.example");
        groupedSheet.CellPhoneticGuides[destination].RunPhoneticXmls.Should().Equal("paste-guide");
    }

    [Fact]
    public void ChartFormulaFieldTransformer_TransformsEveryOwnedFormulaSlot()
    {
        var chart = new ChartModel
        {
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(0, "val", "cat", "title", "bubble")
            ],
            SeriesRangeDataLabels =
            [
                new ChartSeriesRangeDataLabels(0, "label", 1, [])
            ],
            ErrorBarPlusRangeFormula = "plus",
            ErrorBarMinusRangeFormula = "minus",
        };

        ChartFormulaFieldTransformer.Transform(chart, formula => formula is null ? null : $"{formula}-mapped");

        chart.VerbatimSeriesFormulas![0].Should().Be(
            new ChartSeriesVerbatimFormulas(0, "val-mapped", "cat-mapped", "title-mapped", "bubble-mapped"));
        chart.SeriesRangeDataLabels[0].Formula.Should().Be("label-mapped");
        chart.ErrorBarPlusRangeFormula.Should().Be("plus-mapped");
        chart.ErrorBarMinusRangeFormula.Should().Be("minus-mapped");
    }

    [Fact]
    public void TypedPivotSnapshots_RestoreOnlyTheirOwnedState()
    {
        var sheet = SheetId.New();
        var pivot = new PivotTableModel
        {
            SourceRange = Range(sheet, 1, 1, 4, 3),
            TargetRange = Range(sheet, 1, 6, 4, 8),
            LastRenderedRange = Range(sheet, 1, 6, 3, 8)
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItem: "old"));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.PageFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Old data", "sum"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Equals, "old"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Top, 3));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Ascending));
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Old field", "=1"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "Old item", "=1"));

        var filter = PivotFilterStateSnapshot.Capture(pivot);
        var layout = PivotLayoutStateSnapshot.Capture(pivot);
        var view = PivotViewStateSnapshot.Capture(pivot);
        var calculated = PivotCalculatedItemsStateSnapshot.Capture(pivot);
        var fields = PivotFieldLayoutStateSnapshot.Capture(pivot);

        ClearPivotState(pivot);
        filter.Restore(pivot);
        pivot.RowFields.Single().SelectedItem.Should().Be("old");
        pivot.LabelFilters.Should().ContainSingle();

        ClearPivotState(pivot);
        layout.Restore(pivot);
        pivot.DataFields.Single().Name.Should().Be("Old data");
        pivot.LabelFilters.Should().ContainSingle();

        ClearPivotState(pivot);
        view.Restore(pivot);
        pivot.RowFields.Should().BeEmpty();
        pivot.LabelFilters.Should().ContainSingle();

        ClearPivotState(pivot);
        calculated.Restore(pivot);
        pivot.CalculatedFields.Single().Name.Should().Be("Old field");
        pivot.CalculatedItems.Single().Name.Should().Be("Old item");

        ClearPivotState(pivot);
        fields.Restore(pivot);
        pivot.RowFields.Single().SelectedItem.Should().Be("old");
        pivot.DataFields.Should().BeEmpty();
        pivot.LastRenderedRange.Should().Be(Range(sheet, 1, 6, 3, 8));
    }

    [Fact]
    public void ResidualOwners_AreAdoptedWithoutLocalPolicyCopies()
    {
        foreach (var file in new[]
                 {
                     "ApplyConditionalFormatCommand.cs",
                     "PasteConditionalFormatsCommand.cs",
                     "SetDataValidationCommand.cs",
                     "PasteDataValidationCommand.cs",
                     "FormatPainterDataValidationCommand.cs"
                 })
        {
            var source = ModelSourceTestSupport.ReadCommandsSource(file);
            source.Should().Contain("GridRangeSubtraction.Subtract(", file);
            source.Should().NotContain("IEnumerable<GridRange> Subtract", file);
        }

        ModelSourceTestSupport.ReadCommandsSource("Commands.cs").Should().Contain("CellEditCompanionSnapshot");
        ModelSourceTestSupport.ReadCommandsSource("PasteCellsCommand.cs").Should().Contain("CellEditCompanionSnapshot");
        ModelSourceTestSupport.ReadCommandsSource("GroupedEditCellsCommand.cs").Should().Contain("CellEditCompanionSnapshot");
        ModelSourceTestSupport.ReadCommandsSource("PasteSpecialCommand.cs").Should().Contain("CellEditCompanionSnapshot");
        ModelSourceTestSupport.ReadCommandsSource("CustomSortOrder.cs").Should().Contain("CaseSensitiveSortComparison.Compare");
        ModelSourceTestSupport.ReadCommandsSource("SortCommand.cs").Should().Contain("CaseSensitiveSortComparison.Compare");

        foreach (var file in new[]
                 {
                     "ConfigurePivotTableFieldFiltersCommand.cs",
                     "ConfigurePivotTableLayoutCommand.cs",
                     "ConfigurePivotTableViewCommand.cs",
                     "PivotTableCalculatedAndSourceCommands.cs",
                     "PivotTableActionCommands.cs"
                 })
        {
            ModelSourceTestSupport.ReadCommandsSource(file)
                .Should().Contain("PivotTableCommandRefreshTransaction", file);
        }

        ModelSourceTestSupport.ReadCommandsSource("PivotTableSlicerCommands.cs")
            .Should().Contain("PivotTableTargetStateSnapshot.Capture");
        ModelSourceTestSupport.ReadCommandsSource("PivotTableSlicerTimelineCommands.cs")
            .Should().Contain("PivotTableTargetStateSnapshot.Capture");

        foreach (var file in new[]
                 {
                     "ConvertToRangeStructuredReferenceLowering.cs",
                     "DuplicateSheetDrawingCloner.cs",
                     "RowColumnShiftHelpers.PrintAndCharts.cs",
                 })
        {
            ModelSourceTestSupport.ReadCommandsSource(file)
                .Should().Contain("ChartFormulaFieldTransformer.Transform(", file);
        }
    }

    private static void ClearPivotState(PivotTableModel pivot)
    {
        pivot.RowFields.Clear();
        pivot.ColumnFields.Clear();
        pivot.PageFields.Clear();
        pivot.DataFields.Clear();
        pivot.LabelFilters.Clear();
        pivot.ValueFilters.Clear();
        pivot.Sorts.Clear();
        pivot.CalculatedFields.Clear();
        pivot.CalculatedItems.Clear();
        pivot.LastRenderedRange = null;
    }

    private static GridRange Range(SheetId sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet, startRow, startCol), new CellAddress(sheet, endRow, endCol));
}
