using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R100: R99's <c>DuplicateSheetCommand.RewriteClonedTableReferences</c> fixed ordinary cell
/// formulas and each cloned table's own CalculatedColumnFormula/TotalsRowFormula after
/// <c>UniquifyClonedTables</c> renames a duplicated table (e.g. Table1 -> Table1_2), but left three
/// sibling collections untouched even though <see cref="Sheet.Clone"/> copies them onto the SAME
/// duplicated sheet with formula text intact: Conditional Formats, Data Validations, and charts.
/// Table-name resolution (StructuredReferenceResolver) is workbook-global by name, so without a
/// rewrite these three kept silently resolving to the SOURCE sheet's still-named table instead of
/// the copy's own renamed one -- exactly the bug class R99 fixed for cell formulas.
/// </summary>
public sealed class R100_DuplicateSheetTableRewriteSiblingCollectionsTests
{
    private static (Workbook wb, Sheet sheet) CreateSheetWithTable(string tableName = "Table1")
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = tableName,
            DisplayName = tableName,
            Range = range,
            HasAutoFilter = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Item"),
                new StructuredTableColumnModel(2, "Price")
            }
        };
        sheet.StructuredTables.Add(table);
        return (wb, sheet);
    }

    [Fact]
    public void DuplicateSheetCommand_RewritesCopysConditionalFormatFormulaToCopysOwnRenamedTable()
    {
        var (wb, sheet) = CreateSheetWithTable();
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "Table1[Price]>100"
        });

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;
        copiedTable.Name.Should().NotBe("Table1"); // sanity: the uniquify rename actually happened

        var copiedCf = copy.ConditionalFormats.Should().ContainSingle().Subject;
        copiedCf.FormulaText.Should().Be($"{copiedTable.Name}[Price]>100");

        // Source sheet's own rule must be completely untouched.
        sheet.ConditionalFormats.Should().ContainSingle().Which.FormulaText.Should().Be("Table1[Price]>100");
    }

    [Fact]
    public void DuplicateSheetCommand_RewritesCopysDataValidationFormulasToCopysOwnRenamedTable()
    {
        var (wb, sheet) = CreateSheetWithTable();
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.List,
            Formula1 = "Table1[Category]",
            Formula2 = "Table1[Price]"
        });

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;

        var copiedDv = copy.DataValidations.Should().ContainSingle().Subject;
        copiedDv.Formula1.Should().Be($"{copiedTable.Name}[Category]");
        copiedDv.Formula2.Should().Be($"{copiedTable.Name}[Price]");

        // Source sheet's own rule must be completely untouched.
        var sourceDv = sheet.DataValidations.Should().ContainSingle().Subject;
        sourceDv.Formula1.Should().Be("Table1[Category]");
        sourceDv.Formula2.Should().Be("Table1[Price]");
    }

    [Fact]
    public void DuplicateSheetCommand_RewritesCopysChartVerbatimSeriesFormulaToCopysOwnRenamedTable()
    {
        var (wb, sheet) = CreateSheetWithTable();
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(0, "Table1[Values]", null, "Table1[[#Headers],[Values]]")
            ]
        });

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        var copiedTable = copy.StructuredTables.Should().ContainSingle().Subject;

        var copiedChart = copy.Charts.Should().ContainSingle().Subject;
        var copiedVerbatim = copiedChart.VerbatimSeriesFormulas.Should().ContainSingle().Subject;
        copiedVerbatim.ValFormula.Should().Be($"{copiedTable.Name}[Values]");
        copiedVerbatim.TxFormula.Should().Be($"{copiedTable.Name}[[#Headers],[Values]]");

        // Source sheet's own chart must be completely untouched.
        var sourceChart = sheet.Charts.Should().ContainSingle().Subject;
        var sourceVerbatim = sourceChart.VerbatimSeriesFormulas.Should().ContainSingle().Subject;
        sourceVerbatim.ValFormula.Should().Be("Table1[Values]");
        sourceVerbatim.TxFormula.Should().Be("Table1[[#Headers],[Values]]");
    }

    /// <summary>
    /// No-regression sibling: a Conditional Format / Data Validation / chart formula that
    /// references a same-sheet cell range (no table involved at all) must still be copied over
    /// unchanged (aside from the pre-existing sheet-name rebase this rewrite pass must not
    /// disturb) when the duplicated sheet has no structured table -- the renames list is empty, so
    /// the new rewrite passes must be guaranteed no-ops.
    /// </summary>
    [Fact]
    public void DuplicateSheetCommand_NoTables_LeavesConditionalFormatDataValidationAndChartFormulasUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>100"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "10"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            VerbatimSeriesFormulas =
            [
                new ChartSeriesVerbatimFormulas(0, "(Sheet1!$A$1:$A$3,Sheet1!$C$1:$C$3)", null, null)
            ]
        });

        var ctx = new TestCommandContext(wb);
        var command = new DuplicateSheetCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        var copy = wb.Sheets[1];
        copy.StructuredTables.Should().BeEmpty();
        copy.ConditionalFormats.Should().ContainSingle().Which.FormulaText.Should().Be("A1>100");
        var copiedDv = copy.DataValidations.Should().ContainSingle().Subject;
        copiedDv.Formula1.Should().Be("1");
        copiedDv.Formula2.Should().Be("10");
        // The chart's verbatim multi-area formula IS sheet-name-rebased onto the copy (pre-existing
        // R95/R16 behavior this fix must not disturb), just not table-renamed (there is no table).
        // The copy's name contains a space ("Sheet1 (2)"), so the rebased qualifier is quoted.
        copy.Charts.Should().ContainSingle().Which.VerbatimSeriesFormulas.Should().ContainSingle()
            .Which.ValFormula.Should().Be($"('{copy.Name}'!$A$1:$A$3,'{copy.Name}'!$C$1:$C$3)");
    }
}
