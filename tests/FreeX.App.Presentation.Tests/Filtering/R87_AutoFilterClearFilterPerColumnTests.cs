using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R87-commands-autofilter-sort-5-2: "Clear Filter From &lt;Column&gt;" must be enabled only when
/// THIS column carries an active filter criterion, not whenever ANY column in the whole AutoFilter
/// range does. CreateMenuPlan previously fed <see cref="AutoFilterDropdownMenuPlanner.HasActiveFilter(Sheet, GridRange)"/>
/// (a whole-range check meant for the toolbar's "Clear" button) straight into the per-column entry's
/// isEnabled, so opening an unfiltered column's dropdown while a sibling column was filtered showed
/// "Clear Filter" as enabled -- real Excel greys it out in that case.
/// </summary>
public sealed class R87_AutoFilterClearFilterPerColumnTests
{
    [Fact]
    public void CreateMenuPlan_DisablesClearFilter_ForColumnWithNoFilterOfItsOwn()
    {
        // Range A1:C4 -- column A (Region) is value-filtered to "East"; column C (Extra) has no
        // filter criterion at all, even though rows are hidden elsewhere in the same range.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Extra"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("X"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("Y"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("Z"));

        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3));
        var ctx = new FakeCommandContext(workbook);

        // Only column A (Region, offset 0) is filtered -- row 3 is hidden as a result.
        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["East"]).Apply(ctx);
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);

        // Open the dropdown for column C (Extra, offset 2) -- it has no filter of its own.
        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 2);
        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, TextProvider, "(Blanks)");

        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ClearFilter)
            .IsEnabled.Should().BeFalse("column C carries no filter criterion of its own, even though a sibling column does");
    }

    /// <summary>No-regression sibling: the filtered column's OWN dropdown must still show "Clear
    /// Filter" as enabled -- guards against the per-column check becoming permanently false.</summary>
    [Fact]
    public void CreateMenuPlan_EnablesClearFilter_ForColumnWithItsOwnActiveFilter()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Amount"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Extra"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("X"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("West"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(20));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("Y"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("Z"));

        var range = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3));
        var ctx = new FakeCommandContext(workbook);

        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["East"]).Apply(ctx);

        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);
        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(sheet, plan, TextProvider, "(Blanks)");

        menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ClearFilter)
            .IsEnabled.Should().BeTrue();
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) => new(sheet.Id, row, col);

    private static readonly TestTextProvider TextProvider = new();

    private sealed class FakeCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private sealed class TestTextProvider : IAutoFilterMenuTextProvider
    {
        public string Get(string resourceKey) => resourceKey;

        public string Format(string resourceKey, string value) => $"{resourceKey}: {value}";
    }
}
