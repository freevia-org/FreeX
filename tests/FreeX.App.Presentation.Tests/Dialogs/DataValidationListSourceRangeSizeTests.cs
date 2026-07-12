using FluentAssertions;
using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Dialogs;

/// <summary>
/// R29-dialogs-validation-logic-2: List Data Validation must not reject a valid range-reference
/// source purely because it spans a large number of cells — Excel places no such limit on a List
/// source range (a full-column reference is a legal source).
/// </summary>
public sealed class DataValidationListSourceRangeSizeTests
{
    private static DvValidationResult ValidateListFormula1(string formula1) =>
        DataValidationDialogModel.ForType(DvType.List).Validate(new DvCriteriaInput
        {
            Type = DvType.List,
            Formula1 = formula1
        });

    [Fact]
    public void RangeSpanningOver10000Cells_IsAccepted()
    {
        // The bug: a same-sheet column reference spanning 10,001 cells was rejected outright by an
        // arbitrary MaximumListSourceItems=10_000 cap that has no counterpart in real Excel.
        ValidateListFormula1("=$A$1:$A$10001").IsValid.Should().BeTrue();
    }

    [Fact]
    public void RangeSpanningManyMoreCellsThanTheOldCap_IsAccepted()
    {
        // A much larger reference (e.g. a near-full-column reference) is equally legal in Excel.
        ValidateListFormula1("=$A$1:$A$500000").IsValid.Should().BeTrue();
    }

    [Fact]
    public void SmallRange_StillAccepted()
    {
        // Sibling already-working case: a small range reference must remain valid after the fix.
        ValidateListFormula1("=$A$1:$A$5").IsValid.Should().BeTrue();
    }

    [Fact]
    public void RangeAtOldCapBoundary_StillAccepted()
    {
        // Sibling already-working case: exactly at the old 10,000-cell boundary must still be valid.
        ValidateListFormula1("=$A$1:$A$10000").IsValid.Should().BeTrue();
    }

    [Fact]
    public void InlineCommaSeparatedList_StillAccepted()
    {
        // Sibling already-working case: a literal inline list is unaffected by this fix.
        ValidateListFormula1("Red,\"Blue, Green\"").IsValid.Should().BeTrue();
    }

    [Fact]
    public void MalformedFormula_IsStillRejected()
    {
        // The fix must not swing to accepting arbitrary garbage: an unparsable formula (unterminated
        // string literal) is still invalid.
        var result = ValidateListFormula1("=\"unterminated");

        result.IsValid.Should().BeFalse();
        result.FirstError!.Target.Should().Be(DvValidationTarget.Formula1);
        result.FirstError!.Kind.Should().Be(DvValidationErrorKind.InvalidListCriteria);
    }

    [Fact]
    public void BlankSource_IsStillRejected()
    {
        // Sibling already-working case: an empty source is still required.
        var result = ValidateListFormula1("   ");

        result.IsValid.Should().BeFalse();
        result.FirstError!.Kind.Should().Be(DvValidationErrorKind.SourceRequired);
    }
}
