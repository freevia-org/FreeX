using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R26-sheet-lifecycle-deep-2: a duplicated sheet's Conditional Format / Data Validation
/// formulas must follow the copy for explicit SAME-sheet-qualified references (matching Excel's
/// Move-or-Copy rebasing, and the analogous fix already applied to chart verbatim range text in
/// <c>DuplicateSheetDrawingCloner</c>), while leaving unqualified references and references that
/// explicitly name a DIFFERENT sheet untouched.
/// </summary>
public partial class SheetCloneTests
{
    [Fact]
    public void Sheet_Clone_RebasesConditionalFormatSameSheetQualifiedFormulaToCopy()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        src.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 1, 2), new CellAddress(src.Id, 1, 2)),
            RuleType = CfRuleType.Formula,
            FormulaText = "Sheet1!A1>3"
        });

        // Mirrors DuplicateSheetCommand's auto-generated copy name, which requires quoting
        // (space + parentheses) once rebased into the formula text.
        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        copy.ConditionalFormats.Should().ContainSingle()
            .Which.FormulaText.Should().Be("'Sheet1 (2)'!A1>3");
    }

    [Fact]
    public void Sheet_Clone_LeavesConditionalFormatUnqualifiedFormulaUnchanged()
    {
        // Sibling already-working case: an unqualified formula implicitly means "this sheet"
        // both before and after duplication, so Clone must not touch it.
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        src.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 1, 2), new CellAddress(src.Id, 1, 2)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>3"
        });

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        copy.ConditionalFormats.Should().ContainSingle().Which.FormulaText.Should().Be("A1>3");
    }

    [Fact]
    public void Sheet_Clone_LeavesConditionalFormatOtherSheetQualifiedFormulaUnchanged()
    {
        // Sibling already-working case: a reference qualified with a DIFFERENT sheet's name must
        // keep pointing at that other sheet, not follow the duplicate.
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        src.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 1, 2), new CellAddress(src.Id, 1, 2)),
            RuleType = CfRuleType.Formula,
            FormulaText = "Sheet2!A1>3"
        });

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        copy.ConditionalFormats.Should().ContainSingle().Which.FormulaText.Should().Be("Sheet2!A1>3");
    }

    [Fact]
    public void Sheet_Clone_RebasesDataValidationSameSheetQualifiedFormulasToCopy()
    {
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        var validation = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 3, 2), new CellAddress(src.Id, 3, 2)),
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "Sheet1!A1",
            Formula2 = "Sheet1!A2"
        };
        src.DataValidations.Add(validation);

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        var cloned = copy.DataValidations.Should().ContainSingle().Subject;
        cloned.Formula1.Should().Be("'Sheet1 (2)'!A1");
        cloned.Formula2.Should().Be("'Sheet1 (2)'!A2");
    }

    [Fact]
    public void Sheet_Clone_LeavesDataValidationListLiteralFormulaUnchanged()
    {
        // Sibling already-working case: a List validation's literal item text (no sheet
        // qualifier at all) must survive the clone untouched.
        var wb = new Workbook("T");
        var src = wb.AddSheet("Sheet1");
        src.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 3, 2), new CellAddress(src.Id, 3, 2)),
            Type = DvType.List,
            Formula1 = "\"A,B\""
        });

        var copy = src.Clone(SheetId.New(), "Sheet1 (2)");

        copy.DataValidations.Should().ContainSingle().Which.Formula1.Should().Be("\"A,B\"");
    }

    [Fact]
    public void Sheet_Clone_RebasesQuotedSourceSheetQualifierAndDoesNotRequireQuotingSimpleCopyName()
    {
        // Source sheet name needs quoting (embedded space); copy name is a simple identifier
        // that does NOT need quoting, so the rebased qualifier must come out bare.
        var wb = new Workbook("T");
        var src = wb.AddSheet("My Sheet");
        src.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(src.Id, 1, 2), new CellAddress(src.Id, 1, 2)),
            RuleType = CfRuleType.Formula,
            FormulaText = "'My Sheet'!A1>3"
        });

        var copy = src.Clone(SheetId.New(), "Copy1");

        copy.ConditionalFormats.Should().ContainSingle().Which.FormulaText.Should().Be("Copy1!A1>3");
    }
}
