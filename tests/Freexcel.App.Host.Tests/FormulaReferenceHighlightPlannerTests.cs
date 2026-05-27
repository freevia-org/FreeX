using FluentAssertions;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaReferenceHighlightPlannerTests
{
    private static readonly SheetId CurrentSheet = SheetId.New();
    private static readonly SheetId OtherSheet = SheetId.New();

    [Fact]
    public void GetHighlights_ReturnsColoredSpansAndRangesForFormulaReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(A1:B2,C3)",
            CurrentSheet,
            ResolveSheet);

        highlights.Should().HaveCount(2);
        highlights[0].TextStart.Should().Be(5);
        highlights[0].TextLength.Should().Be(5);
        highlights[0].Text.Should().Be("A1:B2");
        highlights[0].PaletteIndex.Should().Be(0);
        highlights[0].Range.Should().Be(Range("A1", "B2"));

        highlights[1].TextStart.Should().Be(11);
        highlights[1].TextLength.Should().Be(2);
        highlights[1].Text.Should().Be("C3");
        highlights[1].PaletteIndex.Should().Be(1);
        highlights[1].Range.Should().Be(Range("C3", "C3"));
    }

    [Fact]
    public void GetHighlights_HandlesAbsoluteReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=$A$1+$B2+C$3",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("$A$1", "$B2", "C$3");
        highlights.Select(h => h.Range!.Value).Should().Equal(
            Range("A1", "A1"),
            Range("B2", "B2"),
            Range("C3", "C3"));
    }

    [Fact]
    public void GetHighlights_ColorsSheetQualifiedReferencesAndMapsKnownSheets()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(Sheet2!B4,'Sheet 2'!C5)",
            CurrentSheet,
            ResolveSheet);

        highlights.Should().HaveCount(2);
        highlights[0].Text.Should().Be("Sheet2!B4");
        highlights[0].SheetName.Should().Be("Sheet2");
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(OtherSheet, 4, 2),
            new CellAddress(OtherSheet, 4, 2)));

        highlights[1].Text.Should().Be("'Sheet 2'!C5");
        highlights[1].SheetName.Should().Be("Sheet 2");
        highlights[1].Range.Should().Be(new GridRange(
            new CellAddress(OtherSheet, 5, 3),
            new CellAddress(OtherSheet, 5, 3)));
    }

    [Fact]
    public void GetHighlights_SkipsReferencesInsideStringLiterals()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=IF(A1=\"B2\",C3,\"D4:E5\")",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("A1", "C3");
    }

    [Fact]
    public void GetHighlights_IgnoresTextThatIsNotAFormula()
    {
        FormulaReferenceHighlightPlanner.GetHighlights(
                "A1:B2",
                CurrentSheet,
                ResolveSheet)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void GetHighlights_IgnoresInvalidCellReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(XFE1,A1048577,A1)",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("A1");
    }

    [Fact]
    public void GetHighlights_IgnoresStructuredReferenceColumnSelectorsThatLookLikeCells()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(Sales[A1],Sales[[#Data],[B2]],[@C3],D4)",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("D4");
        highlights[0].Range.Should().Be(Range("D4", "D4"));
    }

    [Fact]
    public void GetHighlights_ResumesGridReferenceColoringAfterStructuredReferences()
    {
        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=Sales[[#All],[A1]]+Sheet2!B2",
            CurrentSheet,
            ResolveSheet);

        highlights.Select(h => h.Text).Should().Equal("Sheet2!B2");
        highlights[0].SheetName.Should().Be("Sheet2");
        highlights[0].Range.Should().Be(new GridRange(
            new CellAddress(OtherSheet, 2, 2),
            new CellAddress(OtherSheet, 2, 2)));
    }

    [Fact]
    public void GetHighlights_ColorsStructuredReferencesAndResolvesTableRanges()
    {
        var (workbook, sheet, formulaCell) = CreateSalesWorkbookWithTotals();

        var highlights = FormulaReferenceHighlightPlanner.GetHighlights(
            "=SUM(Sales[Amount],Sales[[#Headers],[Tax]],[@Amount],Sales[[#This Row],[Amount]:[Tax]])",
            sheet.Id,
            ResolveSheet,
            ResolveStructuredReference);

        highlights.Select(h => h.Text).Should().Equal(
            "Sales[Amount]",
            "Sales[[#Headers],[Tax]]",
            "[@Amount]",
            "Sales[[#This Row],[Amount]:[Tax]]");
        highlights.Select(h => h.PaletteIndex).Should().Equal(0, 1, 2, 3);
        highlights[0].Range.Should().Be(Range("C3", "C4", sheet.Id));
        highlights[1].Range.Should().Be(Range("D2", "D2", sheet.Id));
        highlights[2].Range.Should().Be(Range("C3", "C3", sheet.Id));
        highlights[3].Range.Should().Be(Range("C3", "D3", sheet.Id));

        GridRange? ResolveStructuredReference(string tableName, string selector)
        {
            var trimmedSelector = selector.Trim();
            if (trimmedSelector.StartsWith('@') && trimmedSelector.Length > 1)
            {
                var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
                    workbook,
                    sheet,
                    formulaCell,
                    string.IsNullOrWhiteSpace(tableName) ? null : tableName,
                    trimmedSelector[1..].Trim());
                return address is null ? null : new GridRange(address.Value, address.Value);
            }

            return StructuredReferenceResolver.Resolve(
                workbook,
                sheet,
                tableName,
                trimmedSelector,
                formulaCell);
        }
    }

    private static SheetId? ResolveSheet(string sheetName) =>
        sheetName is "Sheet2" or "Sheet 2" ? OtherSheet : null;

    private static GridRange Range(string start, string end) =>
        new(CellAddress.Parse(start, CurrentSheet), CellAddress.Parse(end, CurrentSheet));

    private static GridRange Range(string start, string end, SheetId sheetId) =>
        new(CellAddress.Parse(start, sheetId), CellAddress.Parse(end, sheetId));

    private static (Workbook Workbook, Sheet Sheet, CellAddress FormulaCell) CreateSalesWorkbookWithTotals()
    {
        var workbook = new Workbook("StructuredReferenceHighlightTest");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Tax"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new NumberValue(3));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 5, 4)),
            HasAutoFilter = true,
            TotalsRowShown = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Tax", TotalsRowFunction: "sum"));
        sheet.StructuredTables.Add(table);

        return (workbook, sheet, new CellAddress(sheet.Id, 3, 3));
    }
}
