using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class DataValidationPreviewPlannerTests
{
    [Fact]
    public void Create_PreviewsApplicableInlineListRuleWithoutMutatingValidationCollection()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 2, 2);
        var selection = new GridRange(target, new CellAddress(sheet.Id, 3, 4));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "Open,Closed,Blocked",
            PromptTitle = "Status",
            PromptMessage = "Choose one status.",
            ErrorTitle = "Invalid status",
            ErrorMessage = "Pick a listed status.",
        });
        var versionBeforePreview = sheet.DataValidations.Version;

        var plan = DataValidationPreviewPlanner.Create(workbook, sheet, target, selection);

        plan.HasApplicableRule.Should().BeTrue();
        plan.Text.Should().Contain("Cell: B2");
        plan.Text.Should().Contain("Selection: B2:D3");
        plan.Text.Should().Contain("Rule: List");
        plan.Text.Should().Contain("Applies to: =$B$2");
        plan.Text.Should().Contain("Criteria: List");
        plan.Text.Should().Contain("Source: Open,Closed,Blocked");
        plan.Text.Should().Contain("In-cell dropdown: Shown");
        plan.Text.Should().Contain("List items: Open, Closed, Blocked");
        plan.Text.Should().Contain("Input message: Status - Choose one status.");
        plan.Text.Should().Contain("Error alert: Stop - Invalid status - Pick a listed status.");
        sheet.DataValidations.Version.Should().Be(versionBeforePreview);
        sheet.DataValidations.Should().ContainSingle();
    }

    [Fact]
    public void Create_ReportsNoValidationForUncoveredCell()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var activeCell = new CellAddress(sheet.Id, 3, 3);
        var selection = new GridRange(activeCell, activeCell);

        var plan = DataValidationPreviewPlanner.Create(workbook, sheet, activeCell, selection);

        plan.HasApplicableRule.Should().BeFalse();
        plan.Text.Should().Contain("Cell: C3");
        plan.Text.Should().Contain("Selection: C3");
        plan.Text.Should().Contain("No data validation applies to C3.");
    }

    [Fact]
    public void Create_UsesAdditionalRangesAndFormatsScalarCriteria()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var primary = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 5, 3);
        var rule = new DataValidation
        {
            AppliesTo = new GridRange(primary, primary),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ShowErrorMessage = false,
        };
        rule.AdditionalRanges.Add(new GridRange(target, new CellAddress(sheet.Id, 6, 4)));
        sheet.DataValidations.Add(rule);

        var plan = DataValidationPreviewPlanner.Create(workbook, sheet, target, new GridRange(target, target));

        plan.HasApplicableRule.Should().BeTrue();
        plan.Text.Should().Contain("Cell: C5");
        plan.Text.Should().Contain("Applies to: =$A$1, =$C$5:$D$6");
        plan.Text.Should().Contain("Criteria: Whole number between 1 and 10");
        plan.Text.Should().Contain("Ignore blank: No");
        plan.Text.Should().Contain("Error alert: Not shown");
    }

    [Fact]
    public void Create_ListRuleWithHiddenDropdownStillReportsListItems()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 2, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "Yes,No,Maybe",
            ShowDropdown = false,
        });

        var plan = DataValidationPreviewPlanner.Create(workbook, sheet, target, new GridRange(target, target));

        plan.HasApplicableRule.Should().BeTrue();
        plan.Text.Should().Contain("In-cell dropdown: Hidden");
        plan.Text.Should().Contain("List items: Yes, No, Maybe");
        plan.Text.Should().NotContain("List items: none available");
    }

    [Fact]
    public void Create_ListRuleWithShownDropdownStillReportsListItems()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 2, 2);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "Yes,No,Maybe",
            ShowDropdown = true,
        });

        var plan = DataValidationPreviewPlanner.Create(workbook, sheet, target, new GridRange(target, target));

        plan.HasApplicableRule.Should().BeTrue();
        plan.Text.Should().Contain("In-cell dropdown: Shown");
        plan.Text.Should().Contain("List items: Yes, No, Maybe");
    }

    [Fact]
    public void Create_TruncatesLongListItemPreview()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var target = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "One,Two,Three,Four,Five,Six,Seven,Eight,Nine,Ten",
        });

        var plan = DataValidationPreviewPlanner.Create(workbook, sheet, target, new GridRange(target, target));

        plan.Text.Should().Contain("List items: One, Two, Three, Four, Five, Six, Seven, Eight (and 2 more)");
    }

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
