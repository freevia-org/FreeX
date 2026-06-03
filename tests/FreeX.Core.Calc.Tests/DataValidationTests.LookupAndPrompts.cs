using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class DataValidationTests
{
    [Fact]
    public void GetApplicable_ReturnsOnlyRulesContainingAddress()
    {
        var (_, sheet) = MakeWorkbook();

        var dv1 = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "A,B,C",
        };
        var dv2 = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 2, 2),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
        };
        sheet.DataValidations.Add(dv1);
        sheet.DataValidations.Add(dv2);

        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 2, 2);
        var addr3 = new CellAddress(sheet.Id, 3, 3);

        DataValidationService.GetApplicable(sheet, addr1).Should().ContainSingle()
            .Which.Should().Be(dv1, "only dv1 covers A1");
        DataValidationService.GetApplicable(sheet, addr2).Should().ContainSingle()
            .Which.Should().Be(dv2, "only dv2 covers B2");
        DataValidationService.GetApplicable(sheet, addr3).Should().BeEmpty("no rule covers C3");
    }

    [Fact]
    public void GetApplicable_ReturnsRulesContainingAddressInAdditionalRanges()
    {
        var (_, sheet) = MakeWorkbook();
        var rule = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "A,B,C"
        };
        rule.AdditionalRanges.Add(MakeSingleCellRange(sheet, 3, 3));
        sheet.DataValidations.Add(rule);

        DataValidationService.GetApplicable(sheet, new CellAddress(sheet.Id, 3, 3))
            .Should().ContainSingle()
            .Which.Should().Be(rule);
    }

    // ─── SetDataValidationCommand ─────────────────────────────────────────────

    [Fact]
    public void GetInputPrompt_ReturnsFirstVisiblePromptForAddress()
    {
        var (_, sheet) = MakeWorkbook();
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 2, 1),
            ShowInputMessage = true,
            PromptTitle = "Other",
            PromptMessage = "Not for A1."
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "A,B,C",
            ShowInputMessage = true,
            PromptTitle = "Choose a code",
            PromptMessage = "Pick A, B, or C."
        });

        var prompt = DataValidationService.GetInputPrompt(sheet, new CellAddress(sheet.Id, 1, 1));

        prompt.Should().NotBeNull();
        prompt.Value.Title.Should().Be("Choose a code");
        prompt.Value.Message.Should().Be("Pick A, B, or C.");
    }

    [Fact]
    public void GetInputPrompt_IgnoresHiddenOrEmptyPrompts()
    {
        var (_, sheet) = MakeWorkbook();
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            ShowInputMessage = false,
            PromptTitle = "Hidden",
            PromptMessage = "Do not show."
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            ShowInputMessage = true
        });

        var prompt = DataValidationService.GetInputPrompt(sheet, new CellAddress(sheet.Id, 1, 1));

        prompt.Should().BeNull();
    }
}
