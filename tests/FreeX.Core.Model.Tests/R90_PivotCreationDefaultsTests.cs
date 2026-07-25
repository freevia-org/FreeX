using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R90-render-pivot-layout-5-1 / R90-render-pivot-layout-5-3: a freshly created pivot table must default
/// to Excel's real out-of-the-box report -- subtotals ON, placed at the TOP of each group, in COMPACT
/// report layout -- not the previous FreeX defaults of subtotals entirely off, Bottom placement, and
/// Tabular layout. Driven through the real product entry point (<see cref="AddPivotTableCommand"/>, the
/// command the Insert PivotTable UI flow invokes), not a direct <see cref="PivotTableModel"/> construction,
/// so the fix is verified where a real user's action actually reaches it.
/// </summary>
public sealed class R90_PivotCreationDefaultsTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static string? Text(Sheet sheet, string a1) =>
        (sheet.GetCell(Addr(sheet, a1))?.Value as TextValue)?.Value;

    private static void SeedRegionQuarterData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Region"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Quarter"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B2"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("East"));
        sheet.SetCell(Addr(sheet, "B3"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(15));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B4"), new TextValue("Q1"));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A5"), new TextValue("West"));
        sheet.SetCell(Addr(sheet, "B5"), new TextValue("Q2"));
        sheet.SetCell(Addr(sheet, "C5"), new NumberValue(25));
    }

    [Fact]
    public void AddPivotTableCommand_NewTwoRowFieldPivot_DefaultsToExcelSubtotalsOnTopCompact()
    {
        var workbook = new Workbook("PivotCreationDefaultsTest");
        var sheet = workbook.AddSheet("Data");
        SeedRegionQuarterData(sheet);
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "C5"),
            Range(sheet, "E2", "H12"),
            "PivotTable1",
            rowFieldIndexes: [0, 1],
            dataFieldIndexes: [2]);

        command.Apply(ctx).Success.Should().BeTrue();

        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;

        // Model-level defaults (R90-render-pivot-layout-5-1 / -5-3): Excel's real PivotTable defaults.
        pivot.ShowSubtotals.Should().BeTrue(
            "Excel's Insert PivotTable default is subtotals ON, not off entirely");
        pivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top,
            "Excel 2007+'s default subtotal placement is at the top of each group");
        pivot.ReportLayout.Should().Be(PivotReportLayout.Compact,
            "Excel's out-of-the-box default report form is Compact, not Tabular");

        // Rendered-output check through the real command path: Compact layout uses the generic
        // "Row Labels" header, and the Region subtotal ("East Total") is rendered ABOVE East's own
        // Quarter detail rows (Top placement), not below them.
        Text(sheet, "E2").Should().Be("Row Labels");
        Text(sheet, "E3").Should().Be("East Total");
        Text(sheet, "E4").Should().Be("East");
    }

    [Fact]
    public void ConfigurePivotTableOptionsCommand_StillOverridesDefaultsToBottomTabularNoSubtotals()
    {
        // No-regression sibling: the new defaults must remain USER-OVERRIDABLE via the options command
        // (the real Format/PivotTable Options entry point) -- the fix must not hardcode subtotals-on or
        // Compact layout so that a user who explicitly wants the classic look can still get it.
        var workbook = new Workbook("PivotCreationDefaultsOverrideTest");
        var sheet = workbook.AddSheet("Data");
        SeedRegionQuarterData(sheet);
        var ctx = new TestCommandContext(workbook);

        var command = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "C5"),
            Range(sheet, "E2", "H12"),
            "PivotTable1",
            rowFieldIndexes: [0, 1],
            dataFieldIndexes: [2]);
        command.Apply(ctx).Success.Should().BeTrue();
        var pivot = sheet.PivotTables.Should().ContainSingle().Subject;

        var optionsCommand = new ConfigurePivotTableOptionsCommand(
            sheet.Id,
            "PivotTable1",
            showRowGrandTotals: true,
            showColumnGrandTotals: true,
            showSubtotals: false,
            subtotalPlacement: PivotSubtotalPlacement.Bottom,
            repeatItemLabels: true,
            blankLineAfterItems: false,
            styleName: "PivotStyleLight16",
            reportLayout: PivotReportLayout.Tabular);

        optionsCommand.Apply(ctx).Success.Should().BeTrue();

        pivot.ShowSubtotals.Should().BeFalse();
        pivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Bottom);
        pivot.ReportLayout.Should().Be(PivotReportLayout.Tabular);
        Text(sheet, "E2").Should().Be("Region");
        sheet.GetCell(Addr(sheet, "E5"))?.Value.Should().NotBe(new TextValue("East Total"),
            "with subtotals off, no 'East Total' row should be rendered at all");
    }
}
