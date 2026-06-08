using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSheetSelectionServiceTests
{
    [Fact]
    public void EnsureActiveSheet_AddsSheetWhenWorkbookIsEmpty()
    {
        var workbook = new Workbook();

        var selection = new WorkbookSheetSelectionService().EnsureActiveSheet(workbook);

        selection.Sheet.Name.Should().Be("Sheet1");
        selection.Index.Should().Be(0);
        workbook.ActiveSheetIndex.Should().Be(0);
        selection.Tabs.Should().ContainSingle()
            .Which.Should().Be(new WorkbookSheetTab(selection.Sheet.Id, "Sheet1", IsActive: true));
    }

    [Fact]
    public void EnsureActiveSheet_ClampsInvalidActiveSheetIndexToFirstVisibleSheet()
    {
        var workbook = new Workbook();
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var visible = workbook.AddSheet("Visible");
        workbook.ActiveSheetIndex = 99;

        var selection = new WorkbookSheetSelectionService().EnsureActiveSheet(workbook);

        selection.Sheet.Should().BeSameAs(visible);
        selection.Index.Should().Be(1);
        workbook.ActiveSheetIndex.Should().Be(1);
        selection.Tabs.Should().ContainSingle()
            .Which.Should().Be(new WorkbookSheetTab(visible.Id, "Visible", IsActive: true));
    }

    [Fact]
    public void SelectSheet_ActivatesVisibleSheetAndBuildsTabs()
    {
        var workbook = new Workbook();
        var summary = workbook.AddSheet("Summary");
        summary.TabColor = new CellColor(0, 112, 192);
        var details = workbook.AddSheet("Details");
        details.TabColor = new CellColor(0, 176, 80);
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsVeryHidden = true;

        var selection = new WorkbookSheetSelectionService().SelectSheet(workbook, details.Id);

        selection.Sheet.Should().BeSameAs(details);
        selection.Index.Should().Be(1);
        workbook.ActiveSheetIndex.Should().Be(1);
        selection.Tabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Summary", IsActive: false, summary.TabColor),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true, details.TabColor));
    }

    [Fact]
    public void SelectSheet_PropagatesGroupedVisibleTabState()
    {
        var workbook = new Workbook();
        var summary = workbook.AddSheet("Summary");
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        var grouped = new HashSet<SheetId> { summary.Id, details.Id, hidden.Id };

        var selection = new WorkbookSheetSelectionService().SelectSheet(workbook, details.Id, grouped);

        selection.Tabs.Should().Equal(
            new WorkbookSheetTab(summary.Id, "Summary", IsActive: false, TabColor: null, IsGrouped: true),
            new WorkbookSheetTab(details.Id, "Details", IsActive: true, TabColor: null, IsGrouped: true));
    }

    [Fact]
    public void SelectSheet_IgnoresHiddenSheetWhenVisibleSheetsExist()
    {
        var workbook = new Workbook();
        var visible = workbook.AddSheet("Visible");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        workbook.ActiveSheetIndex = 0;

        var selection = new WorkbookSheetSelectionService().SelectSheet(workbook, hidden.Id);

        selection.Sheet.Should().BeSameAs(visible);
        selection.Index.Should().Be(0);
        workbook.ActiveSheetIndex.Should().Be(0);
    }

    [Fact]
    public void EnsureActiveSheet_AllowsHiddenSheetsWhenNoVisibleSheetsExist()
    {
        var workbook = new Workbook();
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        workbook.ActiveSheetIndex = 0;

        var selection = new WorkbookSheetSelectionService().EnsureActiveSheet(workbook);

        selection.Sheet.Should().BeSameAs(hidden);
        selection.Index.Should().Be(0);
        selection.Tabs.Should().ContainSingle()
            .Which.Should().Be(new WorkbookSheetTab(hidden.Id, "Hidden", IsActive: true));
    }
}
