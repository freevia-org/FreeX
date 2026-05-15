using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class DataValidationTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var wb = new Workbook("test");
        var sh = wb.AddSheet("Sheet1");
        return (wb, sh);
    }

    private static GridRange MakeSingleCellRange(Sheet sheet, uint row, uint col)
    {
        var addr = new CellAddress(sheet.Id, row, col);
        return new GridRange(addr, addr);
    }

    // ─── List validation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_List_AcceptsValueInList()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Apple"));

        result.Should().BeNull("Apple is in the allowed list");
    }

    [Fact]
    public void Validate_List_RejectsValueNotInList()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"));

        result.Should().NotBeNull("Mango is not in the allowed list");
    }

    [Fact]
    public void Validate_List_BlankAllowed_WhenAllowBlankTrue()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, BlankValue.Instance);

        result.Should().BeNull("blank is allowed when AllowBlank=true");
    }

    [Fact]
    public void Validate_List_BlankRejected_WhenAllowBlankFalse()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = false,
        };

        var result = DataValidationService.Validate(dv, BlankValue.Instance);

        result.Should().NotBeNull("blank should be rejected when AllowBlank=false");
    }

    [Fact]
    public void Validate_List_CaseInsensitive()
    {
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("apple"));

        result.Should().BeNull("matching should be case-insensitive");
    }

    // ─── WholeNumber validation ───────────────────────────────────────────────

    [Fact]
    public void Validate_ListRangeSource_AcceptsValueInReferencedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Banana"), sheet, entryAddress, workbook);

        result.Should().BeNull("Excel list validation accepts values from a referenced source range");
    }

    [Fact]
    public void Validate_ListRangeSource_RejectsValueOutsideReferencedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"), sheet, entryAddress, workbook);

        result.Should().NotBeNull("Mango is not present in the referenced list source range");
    }

    [Fact]
    public void Validate_ListNamedRangeSource_AcceptsValueInNamedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        workbook.DefineNamedRange("FruitList", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=FruitList",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Cherry"), sheet, entryAddress, workbook);

        result.Should().BeNull("Excel list validation accepts values from a named range source");
    }

    [Fact]
    public void Validate_ListNamedRangeSource_RejectsValueOutsideNamedRange()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        workbook.DefineNamedRange("FruitList", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1)));
        var entryAddress = new CellAddress(sheet.Id, 5, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 5, 1),
            Type = DvType.List,
            Formula1 = "=FruitList",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Mango"), sheet, entryAddress, workbook);

        result.Should().NotBeNull("Mango is not present in the named list source range");
    }

    [Fact]
    public void GetListItems_ReturnsRangeSourceItemsForVisibleDropdown()
    {
        var (workbook, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Banana"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Cherry"));
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$3",
            ShowDropdown = true
        };

        var items = DataValidationService.GetListItems(dv, sheet, workbook);

        items.Should().Equal("Apple", "Banana", "Cherry");
    }

    [Fact]
    public void GetListItems_ReturnsEmptyWhenDropdownArrowIsHidden()
    {
        var (_, sheet) = MakeWorkbook();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            ShowDropdown = false
        };

        var items = DataValidationService.GetListItems(dv, sheet);

        items.Should().BeEmpty("Excel hides the in-cell dropdown when the rule suppresses the arrow");
    }

    [Fact]
    public void FormatListSourceRange_UsesAbsoluteA1Reference()
    {
        var (_, sheet) = MakeWorkbook();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        var source = DataValidationService.FormatListSourceRange(range);

        source.Should().Be("=$A$1:$A$3");
    }

    [Fact]
    public void FormatListSourceRange_IncludesQuotedSheetNameWhenRequested()
    {
        var (_, sheet) = MakeWorkbook();
        sheet.Name = "Lookup Values";
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3));

        var source = DataValidationService.FormatListSourceRange(range, sheet.Name);

        source.Should().Be("='Lookup Values'!$B$2:$C$4");
    }

    [Fact]
    public void FormatListSourceRange_OmitsSheetNameForCurrentSheet()
    {
        var (_, sheet) = MakeWorkbook();
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        var source = DataValidationService.FormatListSourceRange(range, sheet.Name, sheet.Name);

        source.Should().Be("=$A$1:$A$3");
    }

    [Fact]
    public void GetInvalidEntryAction_ReturnsBlockForStopAlert()
    {
        var dv = new DataValidation { AlertStyle = DvAlertStyle.Stop, ShowErrorMessage = true };

        DataValidationService.GetInvalidEntryAction(dv)
            .Should().Be(DataValidationInvalidEntryAction.Block);
    }

    [Fact]
    public void GetInvalidEntryAction_ReturnsAllowForHiddenErrorAlert()
    {
        var dv = new DataValidation { AlertStyle = DvAlertStyle.Stop, ShowErrorMessage = false };

        DataValidationService.GetInvalidEntryAction(dv)
            .Should().Be(DataValidationInvalidEntryAction.Allow);
    }

    [Fact]
    public void Validate_WholeNumber_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5));

        result.Should().BeNull("5 is between 1 and 10");
    }

    [Fact]
    public void Validate_WholeNumber_Between_RejectsOutOfRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(15));

        result.Should().NotBeNull("15 is outside the range 1-10");
    }

    [Fact]
    public void Validate_WholeNumber_RejectsDecimalValueInsideRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5.5));

        result.Should().NotBeNull("Excel whole-number validation rejects decimal values even when they are in range");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_AcceptsLargerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(1));

        result.Should().BeNull("1 > 0");
    }

    [Fact]
    public void Validate_WholeNumber_GreaterThan_RejectsSmallerValue()
    {
        var dv = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new NumberValue(-1));

        result.Should().NotBeNull("-1 is not > 0");
    }

    // ─── TextLength validation ────────────────────────────────────────────────

    [Fact]
    public void Validate_TextLength_LessThan_AcceptsShortText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "10",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hi"));

        result.Should().BeNull("'Hi' has length 2 which is < 10");
    }

    [Fact]
    public void Validate_TextLength_LessThan_RejectsLongText()
    {
        var dv = new DataValidation
        {
            Type = DvType.TextLength,
            Operator = DvOperator.LessThan,
            Formula1 = "5",
            AllowBlank = true,
        };

        var result = DataValidationService.Validate(dv, new TextValue("Hello World"));

        result.Should().NotBeNull("'Hello World' has length 11 which is not < 5");
    }

    // ─── Any validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_Any_AlwaysAccepts()
    {
        var dv = new DataValidation { Type = DvType.Any };

        DataValidationService.Validate(dv, new NumberValue(99)).Should().BeNull();
        DataValidationService.Validate(dv, new TextValue("x")).Should().BeNull();
        DataValidationService.Validate(dv, BlankValue.Instance).Should().BeNull();
    }

    // ─── GetApplicable ────────────────────────────────────────────────────────

    [Fact]
    public void Validate_CustomFormula_AcceptsWhenFormulaEvaluatesTrueForEditedCell()
    {
        var (_, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.Custom,
            Formula1 = "=MOD(A1,2)=0",
            ErrorMessage = "Enter an even number."
        };

        var result = DataValidationService.Validate(dv, new NumberValue(4), sheet, addr);

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_CustomFormula_RejectsWhenFormulaEvaluatesFalseForEditedCell()
    {
        var (_, sheet) = MakeWorkbook();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.Custom,
            Formula1 = "=MOD(A1,2)=0",
            ErrorMessage = "Enter an even number."
        };

        var result = DataValidationService.Validate(dv, new NumberValue(5), sheet, addr);

        result.Should().Be("Enter an even number.");
    }

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
        prompt!.Title.Should().Be("Choose a code");
        prompt.Message.Should().Be("Pick A, B, or C.");
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

    [Fact]
    public void SetDataValidationCommand_Apply_AddsRule()
    {
        var (wb, sheet) = MakeWorkbook();

        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, dv));

        sheet.DataValidations.Should().ContainSingle().Which.Id.Should().Be(dv.Id);
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RemovesRule()
    {
        var (wb, sheet) = MakeWorkbook();

        var dv = new DataValidation
        {
            AppliesTo = MakeSingleCellRange(sheet, 1, 1),
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, dv));
        sheet.DataValidations.Should().HaveCount(1);

        bus.Undo(wb.Id);
        sheet.DataValidations.Should().BeEmpty("revert should remove the rule");
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RestoresPreviousRule()
    {
        var (wb, sheet) = MakeWorkbook();

        var range = MakeSingleCellRange(sheet, 1, 1);
        var original = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        };
        // Pre-seed a rule with the same Id
        sheet.DataValidations.Add(original);

        var replacement = new DataValidation
        {
            Id = original.Id,           // same Id = replace
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));
        sheet.DataValidations.Should().ContainSingle().Which.Formula1.Should().Be("X,Y,Z");

        bus.Undo(wb.Id);
        sheet.DataValidations.Should().ContainSingle().Which.Formula1.Should().Be("A,B",
            "revert should restore the original rule");
    }

    // ─── Decimal validation ───────────────────────────────────────────────────

    [Fact]
    public void SetDataValidationCommand_Apply_ReplacesExistingRuleOnSameRange()
    {
        var (wb, sheet) = MakeWorkbook();
        var range = MakeSingleCellRange(sheet, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        var replacement = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("X,Y,Z",
                "applying validation to the same range should replace the previous rule instead of stacking rules");
    }

    [Fact]
    public void SetDataValidationCommand_Revert_RestoresRuleReplacedBySameRange()
    {
        var (wb, sheet) = MakeWorkbook();
        var range = MakeSingleCellRange(sheet, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        var replacement = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new SetDataValidationCommand(sheet.Id, replacement));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("A,B",
                "undo should restore the rule that was replaced for the same range");
    }

    [Fact]
    public void ClearDataValidationCommand_Apply_RemovesRulesIntersectingSelection()
    {
        var (wb, sheet) = MakeWorkbook();
        var targetRange = MakeSingleCellRange(sheet, 1, 1);
        var unrelatedRange = MakeSingleCellRange(sheet, 3, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = targetRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = unrelatedRange,
            Type = DvType.List,
            Formula1 = "X,Y",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, targetRange));

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("X,Y",
                "Clear All should remove validation from the selected range without touching unrelated validation rules");
    }

    [Fact]
    public void ClearDataValidationCommand_Revert_RestoresClearedRules()
    {
        var (wb, sheet) = MakeWorkbook();
        var targetRange = MakeSingleCellRange(sheet, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = targetRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, targetRange));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.Formula1.Should().Be("A,B",
                "undo should restore validation rules cleared from the selection");
    }

    [Fact]
    public void ClearDataValidationCommand_Apply_PreservesUnselectedPartsOfLargerRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var clearRange = MakeSingleCellRange(sheet, 2, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = originalRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, clearRange));

        sheet.DataValidations.Should().HaveCount(2);
        sheet.DataValidations.Select(rule => rule.AppliesTo.ToString())
            .Should().BeEquivalentTo(["A1:A1", "A3:A3"],
                "only the selected middle cell should lose validation");
        sheet.DataValidations.Should().OnlyContain(rule => rule.Formula1 == "A,B");
    }

    [Fact]
    public void ClearDataValidationCommand_Revert_RestoresPartiallyClearedRule()
    {
        var (wb, sheet) = MakeWorkbook();
        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var clearRange = MakeSingleCellRange(sheet, 2, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = originalRange,
            Type = DvType.List,
            Formula1 = "A,B",
        });

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        bus.Execute(wb.Id, new ClearDataValidationCommand(sheet.Id, clearRange));
        bus.Undo(wb.Id);

        sheet.DataValidations.Should().ContainSingle()
            .Which.AppliesTo.Should().Be(originalRange,
                "undo should remove split fragments and restore the original validation range");
    }

    [Fact]
    public void Validate_Decimal_Between_AcceptsInRange()
    {
        var dv = new DataValidation
        {
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "0.5",
            Formula2 = "9.5",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(5.0)).Should().BeNull("5.0 is between 0.5 and 9.5");
        DataValidationService.Validate(dv, new NumberValue(10.0)).Should().NotBeNull("10.0 is outside the range");
    }

    // ─── minimal test helpers ─────────────────────────────────────────────────

    [Fact]
    public void Validate_Date_Between_ParsesIsoDateBounds()
    {
        var dv = new DataValidation
        {
            Type = DvType.Date,
            Operator = DvOperator.Between,
            Formula1 = "2026-05-01",
            Formula2 = "2026-05-31",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 5, 15)))
            .Should().BeNull("May 15 is within the May date validation window");
        DataValidationService.Validate(dv, DateTimeValue.FromDateTime(new DateTime(2026, 6, 1)))
            .Should().NotBeNull("June 1 is outside the May date validation window");
    }

    [Fact]
    public void Validate_Time_Between_ParsesClockTimeBounds()
    {
        var dv = new DataValidation
        {
            Type = DvType.Time,
            Operator = DvOperator.Between,
            Formula1 = "09:00",
            Formula2 = "17:30",
            AllowBlank = true,
        };

        DataValidationService.Validate(dv, new NumberValue(new TimeSpan(10, 30, 0).TotalDays))
            .Should().BeNull("10:30 is within the workday validation window");
        DataValidationService.Validate(dv, new NumberValue(new TimeSpan(18, 0, 0).TotalDays))
            .Should().NotBeNull("18:00 is outside the workday validation window");
    }

    private sealed class TestCommandContext(Workbook wb) : ICommandContext
    {
        public Workbook Workbook => wb;
        public Sheet GetSheet(SheetId id) => wb.GetSheet(id)!;
    }
}
