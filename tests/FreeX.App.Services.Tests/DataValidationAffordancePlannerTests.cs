using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Unit tests for <see cref="DataValidationAffordancePlanner"/>:
/// (1) ShouldShowDropdownArrow — correctly honours the normalised ShowDropdown flag
///     (FreeX: true = show; the OOXML showDropDown attribute is INVERTED — absent/false = show,
///     present/true = hide — but the model already normalises this on load).
/// (2) GetInputMessagePrompt — delegates to DataValidationService.GetInputPrompt and returns a
///     non-null value only when ShowInputMessage is true and at least one of title/message is non-empty.
/// (3) GetArrowButtonRect — returns a 16 px wide rect flush with the cell's right edge.
/// </summary>
public sealed class DataValidationAffordancePlannerTests
{
    // ─── ShouldShowDropdownArrow ─────────────────────────────────────────────

    [Fact]
    public void ShouldShowDropdownArrow_IsTrue_ForListRuleWithShowDropdownTrue()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            Type = DvType.List,
            Formula1 = "A,B,C",
            ShowDropdown = true   // FreeX normalised: true = show
        });

        DataValidationAffordancePlanner.ShouldShowDropdownArrow(sheet, target)
            .Should().BeTrue("List rule with ShowDropdown=true should show the arrow button");
    }

    [Fact]
    public void ShouldShowDropdownArrow_IsFalse_ForListRuleWithShowDropdownFalse()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            Type = DvType.List,
            Formula1 = "A,B,C",
            ShowDropdown = false   // FreeX normalised: false = hide (OOXML showDropDown="1")
        });

        DataValidationAffordancePlanner.ShouldShowDropdownArrow(sheet, target)
            .Should().BeFalse("ShowDropdown=false means the user suppressed the dropdown");
    }

    [Fact]
    public void ShouldShowDropdownArrow_IsFalse_ForNonListRule()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
            ShowDropdown = true   // ShowDropdown is irrelevant for non-list rules
        });

        DataValidationAffordancePlanner.ShouldShowDropdownArrow(sheet, target)
            .Should().BeFalse("only List rules show a dropdown arrow");
    }

    [Fact]
    public void ShouldShowDropdownArrow_IsFalse_ForCellWithNoRules()
    {
        var sheet = MakeSheet();
        var target = new CellAddress(sheet.Id, 1, 1);

        DataValidationAffordancePlanner.ShouldShowDropdownArrow(sheet, target)
            .Should().BeFalse("no DV rule means no dropdown arrow");
    }

    [Fact]
    public void ShouldShowDropdownArrow_IsFalse_ForCellOnDifferentSheet()
    {
        var (sheet, _) = MakeSheetWithRule(new DataValidation
        {
            Type = DvType.List,
            Formula1 = "X,Y",
            ShowDropdown = true
        });

        // Query a cell on a completely different sheet
        var foreignCell = new CellAddress(SheetId.New(), 1, 1);

        DataValidationAffordancePlanner.ShouldShowDropdownArrow(sheet, foreignCell)
            .Should().BeFalse("rules on sheet A do not apply to sheet B");
    }

    [Fact]
    public void ShouldShowDropdownArrow_IsTrue_WhenAtLeastOneListRuleShowsDropdown()
    {
        var sheet = MakeSheet();
        var target = new CellAddress(sheet.Id, 2, 3);

        // Rule 1: WholeNumber — no dropdown
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0"
        });
        // Rule 2: List with dropdown visible
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(target, target),
            Type = DvType.List,
            Formula1 = "Red,Green,Blue",
            ShowDropdown = true
        });

        DataValidationAffordancePlanner.ShouldShowDropdownArrow(sheet, target)
            .Should().BeTrue("at least one matching List rule with ShowDropdown=true is sufficient");
    }

    // ─── GetInputMessagePrompt ───────────────────────────────────────────────

    [Fact]
    public void GetInputMessagePrompt_ReturnsPrompt_WhenShowInputMessageTrueAndBothPresent()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            ShowInputMessage = true,
            PromptTitle = "Enter a value",
            PromptMessage = "Type a number between 1 and 10."
        });

        var prompt = DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, target);

        prompt.Should().NotBeNull("ShowInputMessage=true with non-empty title+message should yield a prompt");
        prompt!.Value.Title.Should().Be("Enter a value");
        prompt.Value.Message.Should().Be("Type a number between 1 and 10.");
    }

    [Fact]
    public void GetInputMessagePrompt_ReturnsPrompt_WhenOnlyTitlePresent()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            ShowInputMessage = true,
            PromptTitle = "Hint",
            PromptMessage = ""
        });

        var prompt = DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, target);

        prompt.Should().NotBeNull("a title alone is enough to show the tooltip");
        prompt!.Value.Title.Should().Be("Hint");
    }

    [Fact]
    public void GetInputMessagePrompt_ReturnsPrompt_WhenOnlyMessagePresent()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            ShowInputMessage = true,
            PromptTitle = "",
            PromptMessage = "Please fill in this field."
        });

        var prompt = DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, target);

        prompt.Should().NotBeNull("a message alone is enough to show the tooltip");
        prompt!.Value.Message.Should().Be("Please fill in this field.");
    }

    [Fact]
    public void GetInputMessagePrompt_ReturnsNull_WhenShowInputMessageFalse()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            ShowInputMessage = false,
            PromptTitle = "Enter a value",
            PromptMessage = "Between 1 and 10."
        });

        DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, target)
            .Should().BeNull("ShowInputMessage=false means suppress the tooltip");
    }

    [Fact]
    public void GetInputMessagePrompt_ReturnsNull_WhenBothTitleAndMessageEmpty()
    {
        var (sheet, target) = MakeSheetWithRule(new DataValidation
        {
            ShowInputMessage = true,
            PromptTitle = "",
            PromptMessage = ""
        });

        DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, target)
            .Should().BeNull("an empty title and message yields nothing to show");
    }

    [Fact]
    public void GetInputMessagePrompt_ReturnsNull_ForCellWithNoRules()
    {
        var sheet = MakeSheet();
        var target = new CellAddress(sheet.Id, 5, 5);

        DataValidationAffordancePlanner.GetInputMessagePrompt(sheet, target)
            .Should().BeNull("no DV rule → no tooltip");
    }

    // ─── GetArrowButtonRect ──────────────────────────────────────────────────

    [Fact]
    public void GetArrowButtonRect_IsFlushWithRightEdge_AndArrowButtonWidthWide()
    {
        var rect = DataValidationAffordancePlanner.GetArrowButtonRect(
            cellLeft: 100, cellTop: 20, cellWidth: 120, cellHeight: 18);

        rect.Width.Should().Be(DataValidationAffordancePlanner.ArrowButtonWidth,
            "the button should be exactly ArrowButtonWidth (16) pixels wide");
        rect.Height.Should().Be(18, "height matches the cell height");
        rect.Left.Should().Be(100 + 120 - DataValidationAffordancePlanner.ArrowButtonWidth,
            "the button should be flush with the right edge of the cell");
        rect.Top.Should().Be(20, "the button should be flush with the top edge of the cell");
    }

    [Fact]
    public void GetArrowButtonRect_ClampsWidthToCellWidth_ForVeryNarrowCell()
    {
        // Cell is only 8px wide — narrower than ArrowButtonWidth
        var rect = DataValidationAffordancePlanner.GetArrowButtonRect(
            cellLeft: 50, cellTop: 10, cellWidth: 8, cellHeight: 15);

        rect.Width.Should().Be(8,
            "the button width is clamped to the cell width when the cell is narrower than ArrowButtonWidth");
        rect.Left.Should().Be(50, "left edge matches cell left when button fills the whole cell");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Sheet MakeSheet()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook.Sheets.Single();
    }

    private static (Sheet Sheet, CellAddress Target) MakeSheetWithRule(DataValidation ruleTemplate)
    {
        var sheet = MakeSheet();
        var target = new CellAddress(sheet.Id, 1, 1);
        ruleTemplate.AppliesTo = new GridRange(target, target);
        sheet.DataValidations.Add(ruleTemplate);
        return (sheet, target);
    }
}
