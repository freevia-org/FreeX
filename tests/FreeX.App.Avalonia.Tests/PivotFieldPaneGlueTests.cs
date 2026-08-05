using FreeX.App.Avalonia.Pivot;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free pivot field-pane glue used by the Avalonia/macOS shell: source-context
/// detection (<see cref="PivotSourceContext"/>), the validated drop → layout-command mapping
/// (<see cref="PivotFieldDragValidator"/> + <see cref="PivotFieldLayoutCommandFactory"/>), and the header
/// action → command mapping (<see cref="PivotHeaderMenuCommandFactory"/>). No running shell required.
/// </summary>
public sealed class PivotFieldPaneGlueTests
{
    // ── Source-context detection ──────────────────────────────────────────────

    [Fact]
    public void FindActivePivot_ReturnsPivot_WhenCellInsideRenderedRange()
    {
        var (_, sheet, pivot) = BuildPivotWorkbook();
        pivot.LastRenderedRange = Range(sheet.Id, 1, 5, 8, 7);

        var inside = new CellAddress(sheet.Id, 3, 6);
        PivotSourceContext.FindActivePivot(sheet, inside).Should().BeSameAs(pivot);
    }

    [Fact]
    public void FindActivePivot_FallsBackToTargetRange_WhenNotYetRendered()
    {
        var (_, sheet, pivot) = BuildPivotWorkbook();
        pivot.LastRenderedRange = null;
        pivot.TargetRange = Range(sheet.Id, 1, 5, 4, 6);

        PivotSourceContext.FindActivePivot(sheet, new CellAddress(sheet.Id, 2, 5)).Should().BeSameAs(pivot);
    }

    [Fact]
    public void FindActivePivot_ReturnsNull_WhenCellOutsidePivot()
    {
        var (_, sheet, pivot) = BuildPivotWorkbook();
        pivot.LastRenderedRange = Range(sheet.Id, 1, 5, 8, 7);

        PivotSourceContext.FindActivePivot(sheet, new CellAddress(sheet.Id, 1, 1)).Should().BeNull();
    }

    [Fact]
    public void ReadHeaders_ReturnsSourceColumnHeaders_IndexedBySourceField()
    {
        var (workbook, _, pivot) = BuildPivotWorkbook();

        PivotSourceContext.ReadHeaders(workbook, pivot)
            .Should().Equal("Region", "Product", "Amount");
    }

    [Fact]
    public void IsNumericSourceColumn_DistinguishesNumericFromTextColumns()
    {
        var (workbook, _, pivot) = BuildPivotWorkbook();

        PivotSourceContext.IsNumericSourceColumn(workbook, pivot, 0).Should().BeFalse(); // Region (text)
        PivotSourceContext.IsNumericSourceColumn(workbook, pivot, 2).Should().BeTrue();  // Amount (number)
    }

    // ── Validated drop → layout command ───────────────────────────────────────

    [Fact]
    public void TryCreate_AddsNumericFieldToValues_WithSumDefault()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        // Start with Region on rows and Amount in values so the move keeps a data field.
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);

        // Drop Amount (numeric) a second time into values.
        var request = new PivotFieldDropRequest(2, PivotFieldBucket.Values);
        var result = validator.Validate(pivot, headers, request);
        result.IsAllowed.Should().BeTrue();
        result.DefaultSummaryFunction.Should().Be("sum");

        var command = PivotFieldLayoutCommandFactory.TryCreate(sheet.Id, pivot, headers, result);
        command.Should().NotBeNull();

        var areas = PivotFieldLayoutCommandFactory.BuildAreas(
            pivot, headers, result.ResultingLayout!, result.DefaultSummaryFunction);
        areas.RowFields.Select(f => f.SourceFieldIndex).Should().Equal(0);
        areas.DataFields.Select(d => d.SourceFieldIndex).Should().Equal(2, 2);
        areas.DataFields[1].SummaryFunction.Should().Be("sum");
        areas.DataFields[1].Name.Should().Be("Sum of Amount");
    }

    [Fact]
    public void TryCreate_MovingFieldToRows_ProducesLayoutCommand()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);

        // Move Product (available, text) onto the columns axis.
        var request = new PivotFieldDropRequest(1, PivotFieldBucket.Columns);
        var result = validator.Validate(pivot, headers, request);

        var command = PivotFieldLayoutCommandFactory.TryCreate(sheet.Id, pivot, headers, result);
        command.Should().BeOfType<ConfigurePivotTableLayoutCommand>();

        var areas = PivotFieldLayoutCommandFactory.BuildAreas(
            pivot, headers, result.ResultingLayout!, result.DefaultSummaryFunction);
        areas.RowFields.Select(f => f.SourceFieldIndex).Should().Equal(0);
        areas.ColumnFields.Select(f => f.SourceFieldIndex).Should().Equal(1);
        areas.DataFields.Select(d => d.SourceFieldIndex).Should().Equal(2);
    }

    [Fact]
    public void TryCreate_ReturnsNull_WhenRemovingTheLastValueField()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);

        // Removing the only data field would leave the pivot with no values: the factory declines.
        var request = new PivotFieldDropRequest(2, PivotFieldBucket.Available);
        var result = validator.Validate(pivot, headers, request);

        PivotFieldLayoutCommandFactory.TryCreate(sheet.Id, pivot, headers, result).Should().BeNull();
    }

    [Fact]
    public void Layout_AppliedToWorkbook_PivotKeepsTheMovedField()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);

        var request = new PivotFieldDropRequest(1, PivotFieldBucket.Columns);
        var result = validator.Validate(pivot, headers, request);
        var command = PivotFieldLayoutCommandFactory.TryCreate(sheet.Id, pivot, headers, result)!;

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        pivot.ColumnFields.Select(f => f.SourceFieldIndex).Should().Equal(1);
        pivot.RowFields.Select(f => f.SourceFieldIndex).Should().Equal(0);
    }

    // ── Header action → command ───────────────────────────────────────────────

    [Fact]
    public void HeaderAction_SortAscending_BuildsViewCommandWithLabelSort()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);
        var target = RowTarget(pivot, headers, sourceFieldIndex: 0);

        var result = PivotHeaderMenuCommandFactory.Create(
            sheet.Id, pivot, headers, target, PivotHeaderMenuAction.SortAscending, validator);

        result.Kind.Should().Be(PivotHeaderCommandKind.View);
        result.Command.Should().BeOfType<ConfigurePivotTableViewCommand>();
        var sort = result.Sorts.Should().ContainSingle().Subject;
        sort.Target.Should().Be(PivotSortTarget.Label);
        sort.FieldIndex.Should().Be(0);
        sort.Direction.Should().Be(PivotSortDirection.Ascending);
    }

    [Fact]
    public void HeaderAction_ClearSort_IsNoOp_WhenNoSortSet()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);
        var target = RowTarget(pivot, headers, sourceFieldIndex: 0);

        var result = PivotHeaderMenuCommandFactory.Create(
            sheet.Id, pivot, headers, target, PivotHeaderMenuAction.ClearSort, validator);

        result.IsNoOp.Should().BeTrue();
        result.Command.Should().BeNull();
    }

    [Fact]
    public void HeaderAction_ClearFilter_RemovesUnboundValueFilterLikeWpf()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(
            DataFieldIndex: 0,
            Kind: PivotValueFilterKind.GreaterThan,
            ComparisonValue: 100,
            SourceFieldIndex: null));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);
        var target = RowTarget(pivot, headers, sourceFieldIndex: 0);

        var result = PivotHeaderMenuCommandFactory.Create(
            sheet.Id, pivot, headers, target, PivotHeaderMenuAction.ClearFilter, validator);

        result.Kind.Should().Be(PivotHeaderCommandKind.View);
        result.ValueFilters.Should().BeEmpty();
        result.Command.Should().BeOfType<ConfigurePivotTableViewCommand>();
    }

    [Fact]
    public void HeaderAction_MoveToColumns_RoutesThroughLayoutCommand()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);
        var target = RowTarget(pivot, headers, sourceFieldIndex: 0);

        var result = PivotHeaderMenuCommandFactory.Create(
            sheet.Id, pivot, headers, target, PivotHeaderMenuAction.MoveToColumns, validator);

        result.Kind.Should().Be(PivotHeaderCommandKind.Layout);
        result.Command.Should().BeOfType<ConfigurePivotTableLayoutCommand>();
        result.Areas!.ColumnFields.Select(f => f.SourceFieldIndex).Should().Equal(0);
        result.Areas.RowFields.Should().BeEmpty();
    }

    [Fact]
    public void HeaderAction_RemoveField_RoutesThroughLayoutCommand()
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);
        var target = RowTarget(pivot, headers, sourceFieldIndex: 0);

        var result = PivotHeaderMenuCommandFactory.Create(
            sheet.Id, pivot, headers, target, PivotHeaderMenuAction.RemoveField, validator);

        result.Kind.Should().Be(PivotHeaderCommandKind.Layout);
        result.Areas!.RowFields.Should().BeEmpty();
        result.Areas.ColumnFields.Select(f => f.SourceFieldIndex).Should().Equal(1);
    }

    [Theory]
    [InlineData(PivotHeaderMenuAction.LabelFilter)]
    [InlineData(PivotHeaderMenuAction.ValueFilter)]
    [InlineData(PivotHeaderMenuAction.MoreSortOptions)]
    [InlineData(PivotHeaderMenuAction.FieldSettings)]
    [InlineData(PivotHeaderMenuAction.ValueFieldSettings)]
    public void HeaderAction_DialogBackedActions_RemainDeferredAtFactoryBoundary(PivotHeaderMenuAction action)
    {
        var (workbook, sheet, pivot) = BuildPivotWorkbook();
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        var headers = PivotSourceContext.ReadHeaders(workbook, pivot);
        var validator = NumericAwareValidator(workbook, pivot);
        var target = RowTarget(pivot, headers, sourceFieldIndex: 0);

        var result = PivotHeaderMenuCommandFactory.Create(sheet.Id, pivot, headers, target, action, validator);

        result.IsDeferred.Should().BeTrue();
        result.DeferredReason.Should().NotBeNullOrWhiteSpace();
        result.Command.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PivotFieldDragValidator NumericAwareValidator(Workbook workbook, PivotTableModel pivot) =>
        new(sourceFieldIndex => PivotSourceContext.IsNumericSourceColumn(workbook, pivot, sourceFieldIndex));

    private static PivotHeaderDropdownTargetModel RowTarget(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        int sourceFieldIndex) =>
        new(
            pivot.Name,
            PivotFieldListPaneBuilder.FieldCaption(headers, sourceFieldIndex),
            sourceFieldIndex,
            PivotHeaderArea.Row,
            IsActive: false);

    // Source A1:C4 (Region/Product/Amount header + 3 data rows); the pivot output target lives at E1.
    private static (Workbook Workbook, Sheet Sheet, PivotTableModel Pivot) BuildPivotWorkbook()
    {
        var workbook = new Workbook("Pivot");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Product"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(30));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            SourceRange = Range(sheet.Id, 1, 1, 4, 3),
            TargetRange = Range(sheet.Id, 1, 5, 4, 6),
        };
        sheet.PivotTables.Add(pivot);

        return (workbook, sheet, pivot);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
