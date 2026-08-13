using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class DataValidationPresetPlannerTests
{
    [Fact]
    public void GetRuleTypeMetadata_CoversDialogRuleTypesAndFormulaNeeds()
    {
        var metadata = DataValidationPresetPlanner.GetRuleTypeMetadata();

        metadata.Select(item => item.Type).Should().Equal(
            DvType.Any,
            DvType.WholeNumber,
            DvType.Decimal,
            DvType.List,
            DvType.Date,
            DvType.Time,
            DvType.TextLength,
            DvType.Custom);
        metadata.Should().ContainEquivalentOf(new DataValidationRuleTypeMetadata(
            DvType.List,
            "List",
            ShowsOperator: false,
            ShowsDropdown: true,
            RequiresFormula1: true,
            RequiresFormula2: false));
        DataValidationPresetPlanner.GetDisplayName(DvType.TextLength).Should().Be("Text length");
        DataValidationPresetPlanner.RequiresSecondFormula(DvType.WholeNumber, DvOperator.Between).Should().BeTrue();
        DataValidationPresetPlanner.RequiresSecondFormula(DvType.WholeNumber, DvOperator.Equal).Should().BeFalse();
        DataValidationPresetPlanner.RequiresSecondFormula(DvType.List, DvOperator.Between).Should().BeFalse();
    }

    [Fact]
    public void CreateSelectionSummary_ReportsUniformRuleAndReturnsClonedActiveRule()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var selection = new GridRange(a1, b1);
        var rule = new DataValidation
        {
            AppliesTo = new GridRange(a1, a1),
            Type = DvType.List,
            Formula1 = "Open,Closed",
            PromptTitle = "Status"
        };
        rule.AdditionalRanges.Add(new GridRange(b1, b1));
        sheet.DataValidations.Add(rule);
        var versionBeforeSummary = sheet.DataValidations.Version;

        var summary = DataValidationPresetPlanner.CreateSelectionSummary(workbook, sheet, a1, selection);

        summary.State.Should().Be(DataValidationSelectionState.Uniform);
        summary.ActiveCellReference.Should().Be("A1");
        summary.SelectionReference.Should().Be("A1:B1");
        summary.ScannedCellCount.Should().Be(2);
        summary.TotalCellCount.Should().Be(2);
        summary.IsComplete.Should().BeTrue();
        summary.HasActiveCellRule.Should().BeTrue();
        summary.Text.Should().Be("Selection A1:B1 uses List data validation.");
        summary.ActiveCellRule.Should().NotBeSameAs(rule);
        summary.ActiveCellRule.Should().NotBeNull();
        summary.ActiveCellRule!.Id.Should().Be(rule.Id);
        summary.ActiveCellRule.Formula1.Should().Be("Open,Closed");
        summary.ActiveCellRule.AdditionalRanges.Should().ContainSingle().Which.Should().Be(new GridRange(b1, b1));

        summary.ActiveCellRule.Formula1 = "Changed";
        rule.Formula1.Should().Be("Open,Closed");
        sheet.DataValidations.Version.Should().Be(versionBeforeSummary);
    }

    [Fact]
    public void CreateSelectionSummary_DistinguishesPartialAndMixedSelections()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var selection = new GridRange(a1, b1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(a1, a1),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9"
        });

        var partial = DataValidationPresetPlanner.CreateSelectionSummary(workbook, sheet, a1, selection);

        partial.State.Should().Be(DataValidationSelectionState.Partial);
        partial.Text.Should().Be("1 of 2 selected cells use Whole number data validation.");

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(b1, b1),
            Type = DvType.List,
            Formula1 = "Yes,No"
        });

        var mixed = DataValidationPresetPlanner.CreateSelectionSummary(workbook, sheet, a1, selection);

        mixed.State.Should().Be(DataValidationSelectionState.Mixed);
        mixed.Text.Should().Be("Selection A1:B1 has mixed data validation rules.");
    }

    [Fact]
    public void CreateSelectionSummary_GuardsLargeSelectionsWithActiveCellPreview()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(a1, a1),
            Type = DvType.List,
            Formula1 = "Yes,No"
        });

        var summary = DataValidationPresetPlanner.CreateSelectionSummary(
            workbook,
            sheet,
            a1,
            new GridRange(a1, c1),
            maxCellsToScan: 2);

        summary.State.Should().Be(DataValidationSelectionState.TooLargeToSummarize);
        summary.ScannedCellCount.Should().Be(0);
        summary.TotalCellCount.Should().Be(3);
        summary.IsComplete.Should().BeFalse();
        summary.HasActiveCellRule.Should().BeTrue();
        summary.Text.Should().Be("Selection A1:C1 is too large to summarize exactly; the active cell uses List data validation.");
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
