using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r256: the pivot Configure family. Every one of these dialogs hands back the pivot's own current
/// state as its default, so re-confirming one reaches Apply with every argument equal to current
/// state -- the evidence r219 recorded when it put the family on the debt. Re-applying then writes
/// exactly what is already there, and pushing an undo entry for that clears the redo stack.
///
/// <para>r219's obstacle was that deciding "no change" also meant proving the re-render was
/// unnecessary. These tests pin the post-hoc answer instead: the no-op direction, AND the changed
/// direction where the re-render really does produce different cells -- which is the case a
/// model-only comparison would get wrong.</para>
/// </summary>
public sealed class R256_PivotReconfigureNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx, PivotTableModel Pivot) SetUpPivot()
    {
        var workbook = new Workbook("PivotNoOpTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = Range(sheet, "D3", "F9"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (workbook, sheet, ctx, pivot);
    }

    [Fact]
    public void ConfigurePivotTableViewCommand_ReapplyingTheCurrentViewStateIsANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        var outcome = new ConfigurePivotTableViewCommand(
            sheet.Id, "PivotTable1", labelFilters: [], valueFilters: [], sorts: []).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue(
            "the pivot already has no filters and no sorts, so this writes empty over empty and "
            + "re-renders the same cells");
        pivot.LabelFilters.Should().BeEmpty();
    }

    [Fact]
    public void ConfigurePivotTableViewCommand_AddingALabelFilterIsNotANoOp()
    {
        var (_, sheet, ctx, _) = SetUpPivot();

        new ConfigurePivotTableViewCommand(
            sheet.Id,
            "PivotTable1",
            labelFilters: [new PivotLabelFilterModel(0, PivotLabelFilterKind.DoesNotEqual, "C")],
            valueFilters: [],
            sorts: []).Apply(ctx)
            .IsNoOp.Should().BeFalse("filtering out a category removes a rendered row");
    }

    [Fact]
    public void ConfigurePivotTableViewCommand_ReapplyingAnExistingLabelFilterIsANoOp()
    {
        var (_, sheet, ctx, _) = SetUpPivot();
        var filter = new PivotLabelFilterModel(0, PivotLabelFilterKind.DoesNotEqual, "C");

        new ConfigurePivotTableViewCommand(sheet.Id, "PivotTable1", [filter], [], []).Apply(ctx)
            .IsNoOp.Should().BeFalse();

        // A freshly built model with identical content -- the case record equality gets wrong.
        var sameFilter = new PivotLabelFilterModel(0, PivotLabelFilterKind.DoesNotEqual, "C");
        new ConfigurePivotTableViewCommand(sheet.Id, "PivotTable1", [sameFilter], [], []).Apply(ctx)
            .IsNoOp.Should().BeTrue("an equal filter re-applied writes the same state and the same cells");
    }

    [Fact]
    public void ConfigurePivotTableFieldFiltersCommand_ReapplyingTheCurrentSelectionIsANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        var outcome = new ConfigurePivotTableFieldFiltersCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [.. pivot.RowFields],
            columnFields: [.. pivot.ColumnFields],
            pageFields: [.. pivot.PageFields],
            labelFilters: [.. pivot.LabelFilters],
            valueFilters: [.. pivot.ValueFilters],
            sorts: [.. pivot.Sorts]).Apply(ctx);

        outcome.IsNoOp.Should().BeTrue("handing the pivot its own fields back changes nothing");
    }

    [Fact]
    public void ConfigurePivotTableFieldFiltersCommand_NarrowingTheSelectedItemsIsNotANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        new ConfigurePivotTableFieldFiltersCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [new PivotFieldModel(0, SelectedItems: ["A", "B"])],
            columnFields: [.. pivot.ColumnFields],
            pageFields: [.. pivot.PageFields],
            labelFilters: [.. pivot.LabelFilters],
            valueFilters: [.. pivot.ValueFilters],
            sorts: [.. pivot.Sorts]).Apply(ctx)
            .IsNoOp.Should().BeFalse("unchecking a category removes its rendered row");
    }

    /// <summary>
    /// The SelectedItems list is <see cref="PivotFieldModel"/>'s one collection member, so this is
    /// the case that needs stripped content comparison rather than record equality: two field models
    /// with equal-content but distinct lists.
    /// </summary>
    [Fact]
    public void ConfigurePivotTableFieldFiltersCommand_ReapplyingAnEqualSelectedItemsListIsANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        new ConfigurePivotTableFieldFiltersCommand(
            sheet.Id,
            "PivotTable1",
            [new PivotFieldModel(0, SelectedItems: ["A", "B"])],
            [.. pivot.ColumnFields],
            [.. pivot.PageFields],
            [.. pivot.LabelFilters],
            [.. pivot.ValueFilters],
            [.. pivot.Sorts]).Apply(ctx)
            .IsNoOp.Should().BeFalse();

        new ConfigurePivotTableFieldFiltersCommand(
            sheet.Id,
            "PivotTable1",
            [new PivotFieldModel(0, SelectedItems: ["A", "B"])],
            [.. pivot.ColumnFields],
            [.. pivot.PageFields],
            [.. pivot.LabelFilters],
            [.. pivot.ValueFilters],
            [.. pivot.Sorts]).Apply(ctx)
            .IsNoOp.Should().BeTrue(
                "the second call's SelectedItems is a different list instance with the same contents");
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_ReapplyingTheCurrentLayoutIsANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [.. pivot.RowFields],
            columnFields: [.. pivot.ColumnFields],
            pageFields: [.. pivot.PageFields],
            dataFields: [.. pivot.DataFields]).Apply(ctx)
            .IsNoOp.Should().BeTrue("dropping every field back where it was changes nothing");
    }

    [Fact]
    public void ConfigurePivotTableLayoutCommand_MovingAFieldToColumnsIsNotANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        new ConfigurePivotTableLayoutCommand(
            sheet.Id,
            "PivotTable1",
            rowFields: [],
            columnFields: [.. pivot.RowFields],
            pageFields: [.. pivot.PageFields],
            dataFields: [.. pivot.DataFields]).Apply(ctx)
            .IsNoOp.Should().BeFalse("the pivot renders transposed");
    }

    [Fact]
    public void ClearPivotTableViewCommand_ClearingAnAlreadyClearViewIsANoOp()
    {
        var (_, sheet, ctx, _) = SetUpPivot();

        new ClearPivotTableViewCommand(sheet.Id, "PivotTable1").Apply(ctx)
            .IsNoOp.Should().BeTrue("there are no filters or sorts to clear");
    }

    [Fact]
    public void ClearPivotTableViewCommand_ClearingARealFilterIsNotANoOp()
    {
        var (_, sheet, ctx, _) = SetUpPivot();

        new ConfigurePivotTableViewCommand(
            sheet.Id,
            "PivotTable1",
            [new PivotLabelFilterModel(0, PivotLabelFilterKind.DoesNotEqual, "C")],
            [],
            []).Apply(ctx);

        new ClearPivotTableViewCommand(sheet.Id, "PivotTable1").Apply(ctx)
            .IsNoOp.Should().BeFalse("clearing the filter brings the hidden category back");
    }

    /// <summary>
    /// The case that separates the two halves of the decision, and the reason the rendered-cell
    /// comparison is not redundant with the model one: the configuration is identical, so the model
    /// comparison says "unchanged", but the SOURCE DATA moved since the last render, so re-applying
    /// the same view re-renders different values. A model-only decision would report a no-op here
    /// and drop a render that really did change the sheet.
    /// </summary>
    [Fact]
    public void ConfigurePivotTableViewCommand_SameViewOverChangedSourceDataIsNotANoOp()
    {
        var (_, sheet, ctx, _) = SetUpPivot();
        sheet.GetValue(4, 5).Should().Be(new NumberValue(10), "row A's rendered total before the source edit");

        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(999));

        new ConfigurePivotTableViewCommand(sheet.Id, "PivotTable1", [], [], []).Apply(ctx)
            .IsNoOp.Should().BeFalse(
                "the view state is unchanged but the re-render writes a different total");
        sheet.GetValue(4, 5).Should().Be(new NumberValue(999));
    }
}
