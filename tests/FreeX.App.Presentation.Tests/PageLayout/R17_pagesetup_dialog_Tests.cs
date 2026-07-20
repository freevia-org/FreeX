using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// R17-pagesetup-multiregion-2: opening Page Setup on a sheet with a multi-region print area
/// (e.g. "A1:C10,E1:G10") must show every region in <see cref="PageSetupDialogFields.PrintAreaText"/>,
/// and submitting the dialog unchanged (a no-op OK) must preserve every region on the sheet. Before the
/// fix, <c>FromSheet</c> read only <c>sheet.PrintArea</c> (the first region), and the built command
/// issued a single-range <see cref="SetPrintAreaCommand"/> that collapsed <c>sheet.PrintAreas</c> down to
/// that one region as soon as the user clicked OK.
/// </summary>
public sealed class R17_pagesetup_dialog_Tests
{
    private static (Workbook Workbook, Sheet Sheet) CreateSheetWithMultiRegionPrintArea()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var area1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3));
        var area2 = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 10, 7));
        sheet.SetPrintAreas([area1, area2]);
        return (workbook, sheet);
    }

    [Fact]
    public void FromSheet_MultiRegionPrintArea_ShowsBothRegionsInPrintAreaText()
    {
        var (_, sheet) = CreateSheetWithMultiRegionPrintArea();

        var fields = PageSetupDialogModel.FromSheet(sheet);

        fields.PrintAreaText.Should().Be("A1:C10,E1:G10");
    }

    [Fact]
    public void TryBuildCommandPlan_UnchangedMultiRegionPrintArea_AppliesWithoutCollapsingRegions()
    {
        var (workbook, sheet) = CreateSheetWithMultiRegionPrintArea();
        var ctx = new PageSetupTestCommandContext(workbook);

        // Simulate opening Page Setup and clicking OK without touching anything (a no-op submit).
        var fields = PageSetupDialogModel.FromSheet(sheet);

        var result = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        result.Success.Should().BeTrue(result.Error);

        result.Plan!.PageSetupCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3)),
            new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 10, 7)));
    }

    [Fact]
    public void TryBuildCommandPlan_UnchangedMultiRegionPrintArea_PreservesBothRegionsOnTargetSheet()
    {
        var (workbook, sheet) = CreateSheetWithMultiRegionPrintArea();
        var ctx = new PageSetupTestCommandContext(workbook);

        var fields = PageSetupDialogModel.FromSheet(sheet);
        var build = PageSetupDialogModel.TryBuildCommandPlan(sheet, fields);

        build.Success.Should().BeTrue(build.Error);
        var plan = build.Plan!;

        plan.PrintAreas.Should().HaveCount(2);

        plan.PrintAreaCommand.Apply(ctx).Success.Should().BeTrue();

        sheet.PrintAreas.Should().HaveCount(2);
        sheet.PrintAreas.Should().ContainInOrder(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 10, 3)),
            new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 10, 7)));
    }

    private sealed class PageSetupTestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
