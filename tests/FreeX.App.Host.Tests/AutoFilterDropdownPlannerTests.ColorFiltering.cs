using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDropdownPlannerTests
{
    [Fact]
    public void CreateMenuPlan_CollectsDistinctColumnFillAndFontColorsForFilterByColorMenu()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sid = sheet.Id;
        var green = new CellColor(0, 176, 80);
        var yellow = new CellColor(255, 192, 0);
        var red = new CellColor(192, 0, 0);
        var greenStyle = CellStyle.Default.Clone();
        greenStyle.FillColor = green;
        var yellowStyle = CellStyle.Default.Clone();
        yellowStyle.FillColor = yellow;
        yellowStyle.FontColor = red;
        var greenStyleId = workbook.RegisterStyle(greenStyle);
        var yellowStyleId = workbook.RegisterStyle(yellowStyle);

        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("Ready"));
        sheet.SetCell(new CellAddress(sid, 3, 1), new TextValue("Blocked"));
        sheet.SetCell(new CellAddress(sid, 4, 1), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sid, 5, 1), new TextValue("Closed"));
        sheet.GetCell(2, 1)!.StyleId = greenStyleId;
        sheet.GetCell(3, 1)!.StyleId = yellowStyleId;
        sheet.GetCell(4, 1)!.StyleId = greenStyleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(workbook, sheet, plan);

        menu.Entries.Should().Contain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
        menu.Sections[1].Entries.Select(entry => entry.Kind).Should().Contain(AutoFilterMenuEntryKind.FilterByColor);
        menu.ColorOptions.Should().Equal(
            new AutoFilterColorOption("#00B050", AutoFilterColorFilterKind.CellFillColor, green),
            new AutoFilterColorOption("#FFC000", AutoFilterColorFilterKind.CellFillColor, yellow),
            new AutoFilterColorOption(UiText.Get("AutoFilter_NoFill"), AutoFilterColorFilterKind.NoFill, null),
            new AutoFilterColorOption("#C00000", AutoFilterColorFilterKind.FontColor, red));
    }

    [Fact]
    public void CreateMenuPlan_OmitsColorChoicesWhenWorkbookIsUnavailable()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(SheetId, 2, 1), new TextValue("Apple"));
        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 1)),
            FilterColumnOffset: 0);

        AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan)
            .ColorOptions.Should().BeEmpty();
        AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan)
            .Entries.Should().NotContain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
    }

    [Fact]
    public void CreateMenuPlan_OmitsFilterByColorEntryWhenWorkbookHasNoColorChoices()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Fruit"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Apple"));
        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownPlanner.CreateMenuPlan(workbook, sheet, plan);

        menu.ColorOptions.Should().BeEmpty();
        menu.Entries.Should().NotContain(entry => entry.Kind == AutoFilterMenuEntryKind.FilterByColor);
        menu.Sections[1].Entries.Select(entry => entry.Kind).Should().Equal(
            AutoFilterMenuEntryKind.ClearFilter,
            AutoFilterMenuEntryKind.FilterFamily);
    }
}
