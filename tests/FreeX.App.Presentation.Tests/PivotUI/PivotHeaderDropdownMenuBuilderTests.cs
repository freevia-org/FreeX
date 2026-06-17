using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotHeaderDropdownMenuBuilderTests
{
    private static readonly string[] Headers = ["Region", "Product", "Quarter", "Amount"];

    private static PivotTableModel BuildPivot()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.PageFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        return pivot;
    }

    [Fact]
    public void BuildTargets_OrdersPageThenRowThenColumn()
    {
        var targets = PivotHeaderDropdownMenuBuilder.BuildTargets(BuildPivot(), Headers);

        targets.Select(t => t.Area).Should().Equal(
            PivotHeaderArea.Page, PivotHeaderArea.Row, PivotHeaderArea.Column);
        targets.Select(t => t.SourceFieldIndex).Should().Equal(1, 0, 2);
    }

    [Fact]
    public void BuildTargets_SkipsFieldsWithDropDownsDisabled()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.RowFields.Add(new PivotFieldModel(0, ShowDropDowns: false));
        pivot.RowFields.Add(new PivotFieldModel(1));

        var targets = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers);

        targets.Select(t => t.SourceFieldIndex).Should().Equal(1);
    }

    [Fact]
    public void BuildTargets_SkipsOutOfRangeFields()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.RowFields.Add(new PivotFieldModel(99));

        PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers).Should().BeEmpty();
    }

    [Fact]
    public void BuildTargets_ReturnsEmptyWhenFieldHeadersHidden()
    {
        var pivot = BuildPivot();
        pivot.ShowFieldHeaders = false;

        PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers).Should().BeEmpty();
    }

    [Fact]
    public void BuildTargets_MarksFieldWithSortAsActive()
    {
        var pivot = BuildPivot();
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 0));

        var rowTarget = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Row);

        rowTarget.IsActive.Should().BeTrue();
    }

    [Fact]
    public void BuildTargets_MarksFieldWithLabelFilterAsActive()
    {
        var pivot = BuildPivot();
        pivot.LabelFilters.Add(new PivotLabelFilterModel(2, PivotLabelFilterKind.Contains, "Q"));

        var columnTarget = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Column);

        columnTarget.IsActive.Should().BeTrue();
    }

    [Fact]
    public void BuildTargets_MarksFieldWithExplicitSelectionAsActive()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "Widget"));

        PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers).Single().IsActive.Should().BeTrue();
    }

    [Fact]
    public void BuildTargets_AllSelectionIsNotActive()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.PageFields.Add(new PivotFieldModel(1, SelectedItem: "(All)"));

        PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers).Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public void BuildMenu_RowFieldHasSortFilterExpandMoveSettingsAndRemove()
    {
        var pivot = BuildPivot();
        var target = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Row);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);
        var actions = menu.Items.Where(i => !i.IsSeparator).Select(i => i.Action).ToList();

        actions.Should().Contain(PivotHeaderMenuAction.SortAscending);
        actions.Should().Contain(PivotHeaderMenuAction.LabelFilter);
        actions.Should().Contain(PivotHeaderMenuAction.ValueFilter);
        actions.Should().Contain(PivotHeaderMenuAction.ExpandField);
        actions.Should().Contain(PivotHeaderMenuAction.CollapseField);
        actions.Should().Contain(PivotHeaderMenuAction.FieldSettings);
        actions.Should().Contain(PivotHeaderMenuAction.RemoveField);
        actions.Should().NotContain(PivotHeaderMenuAction.ValueFieldSettings);
    }

    [Fact]
    public void BuildMenu_PageFieldOmitsExpandCollapse()
    {
        var pivot = BuildPivot();
        var target = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Page);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);
        var actions = menu.Items.Select(i => i.Action).ToList();

        actions.Should().NotContain(PivotHeaderMenuAction.ExpandField);
        actions.Should().NotContain(PivotHeaderMenuAction.CollapseField);
    }

    [Fact]
    public void BuildMenu_ValueAreaTargetUsesValueFieldSettings()
    {
        var pivot = BuildPivot();
        var target = new PivotHeaderDropdownTargetModel("P", "Sum of Amount", 3, PivotHeaderArea.Value, false, 0);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);
        var actions = menu.Items.Where(i => !i.IsSeparator).Select(i => i.Action).ToList();

        actions.Should().Contain(PivotHeaderMenuAction.ValueFieldSettings);
        actions.Should().NotContain(PivotHeaderMenuAction.FieldSettings);
    }

    [Fact]
    public void BuildMenu_SortDirectionIsCheckedAndClearEnabledWhenSortApplied()
    {
        var pivot = BuildPivot();
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 0));
        var target = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Row);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);

        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.SortDescending).IsChecked.Should().BeTrue();
        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.SortAscending).IsChecked.Should().BeFalse();
        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.ClearSort).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildMenu_ClearSortAndClearFilterDisabledWhenNoneApplied()
    {
        var pivot = BuildPivot();
        var target = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Row);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);

        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.ClearSort).IsEnabled.Should().BeFalse();
        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.ClearFilter).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildMenu_MoveToCurrentAreaIsDisabled()
    {
        var pivot = BuildPivot();
        var target = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Row);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);

        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.MoveToRows).IsEnabled.Should().BeFalse();
        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.MoveToColumns).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildMenu_ExpandCollapseDisabledWhenButtonsHidden()
    {
        var pivot = BuildPivot();
        pivot.ShowExpandCollapseButtons = false;
        var target = PivotHeaderDropdownMenuBuilder.BuildTargets(pivot, Headers)
            .Single(t => t.Area == PivotHeaderArea.Row);

        var menu = PivotHeaderDropdownMenuBuilder.BuildMenu(pivot, target);

        menu.Items.Single(i => i.Action == PivotHeaderMenuAction.ExpandField).IsEnabled.Should().BeFalse();
    }
}
