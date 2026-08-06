using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R92-app-pivot-drilldown-5-2: clicking a pivot cell while building a formula must honour the
/// "Generate GetPivotData" option (parity with Excel's File &gt; Options &gt; Formulas &gt; "Use
/// GetPivotData functions for PivotTable references"). Drives the real click-to-reference entry
/// point (<c>MainWindow.TryApplyFormulaRangeSelection</c>, reached from <c>SheetGrid_MouseDown</c>)
/// via <see cref="MainWindowFormulaBarSyncTests"/>'s WPF harness -- the actual consumer, not a
/// hand-built model of it.
/// </summary>
public sealed class R92_PivotClickGetPivotDataToggleTests
{
    [Fact]
    public void ClickPivotValueCell_OptionOn_InsertsGetPivotDataFormula()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create(
                new AppOptions { GenerateGetPivotData = true });

            SetUpRowPivot(harness);

            harness.SelectActiveCell(1, 8);
            harness.SetFormulaEditCell(1, 8);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            // F4 (row 4, col 6) is the "West" value cell inside the pivot's TargetRange (E2:F5).
            harness.ApplyFormulaRangeSelection(4, 6, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be(
                "=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")",
                "with the option ON (Excel's default), clicking a pivot cell inserts a GETPIVOTDATA call");
        });
    }

    [Fact]
    public void ClickPivotValueCell_OptionOff_InsertsPlainA1Reference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create(
                new AppOptions { GenerateGetPivotData = false });

            SetUpRowPivot(harness);

            harness.SelectActiveCell(1, 8);
            harness.SetFormulaEditCell(1, 8);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            harness.ApplyFormulaRangeSelection(4, 6, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be(
                "=F4",
                "with the option OFF, clicking a pivot cell while building a formula must insert a " +
                "plain A1-style reference instead of GETPIVOTDATA -- e.g. so the formula can be " +
                "filled/copied across multiple pivot cells");
        });
    }

    /// <summary>
    /// No-regression sibling: clicking an ORDINARY (non-pivot) cell must still insert a plain
    /// reference regardless of the toggle -- the option only ever changes behavior for clicks that
    /// land inside a pivot's TargetRange.
    /// </summary>
    [Fact]
    public void ClickOrdinaryCell_OptionOn_StillInsertsPlainA1Reference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowFormulaBarSyncTests.MainWindowHarness.Create(
                new AppOptions { GenerateGetPivotData = true });

            SetUpRowPivot(harness);

            harness.SelectActiveCell(1, 8);
            harness.SetFormulaEditCell(1, 8);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            // A1 is outside the pivot's TargetRange (E2:F5) even though it's inside SourceRange.
            harness.ApplyFormulaRangeSelection(1, 1, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=A1");
        });
    }

    private static void SetUpRowPivot(MainWindowFormulaBarSyncTests.MainWindowHarness harness)
    {
        var sheet = harness.FirstSheet;
        SetCells(
            sheet,
            ("A1", new TextValue("Region")),
            ("B1", new TextValue("Amount")),
            ("E2", new TextValue("Region")),
            ("F2", new TextValue("Sum of Amount")),
            ("E3", new TextValue("East")),
            ("F3", new NumberValue(25)),
            ("E4", new TextValue("West")),
            ("F4", new NumberValue(45)),
            ("E5", new TextValue("Grand Total")),
            ("F5", new NumberValue(70)));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1:B5"),
            TargetRange = Range(sheet, "E2:F5")
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
    }

    private static GridRange Range(Sheet sheet, string reference)
    {
        var parts = reference.Split(':');
        return new GridRange(CellAddress.Parse(parts[0], sheet.Id), CellAddress.Parse(parts[^1], sheet.Id));
    }

    private static void SetCells(Sheet sheet, params (string Address, ScalarValue Value)[] cells)
    {
        foreach (var (address, value) in cells)
            sheet.SetCell(CellAddress.Parse(address, sheet.Id), value);
    }
}
