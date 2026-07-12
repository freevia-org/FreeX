using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-32 regression coverage for R32-rendering-numberformat-display-1:
/// NumberFormatter.cs's question-marks-only numeric-span shortcut discarded the '?'
/// decimal-alignment placeholders entirely (returning bare prefix+suffix) whenever a
/// visible literal preceded/followed them -- e.g. the standard Accounting zero-section
/// idiom "...\"-\"??..." rendered as "GBP -" instead of "GBP -  " (two trailing blank
/// digit-width columns so the dash lines up with the decimal point of the non-zero
/// rows). Fixed by routing the '?' span through RenderQuestionOnlyAlignment (which maps
/// each '?' to a space) instead of dropping it.
/// </summary>
public sealed class R32_NumberFormatQuestionOnlyAlignmentTests
{
    [Fact]
    public void AccountingZeroSectionDashPlaceholder_WithVisibleCurrencyPrefix_PadsTrailingBlanks()
    {
        // The real-Excel-captured golden idiom from ExcelNumberFormatMatrix.csv: a "* "
        // accounting fill followed by a quoted dash and "??" alignment placeholders for the
        // zero section. Before the fix this collapsed to "GBP -" with no trailing padding.
        const string format = "_(GBP* #,##0.00_);_(GBP* (#,##0.00);_(GBP* \"-\"??_);_(@_)";

        var result = NumberFormatter.Format(new NumberValue(0), format);

        result.Should().Be("GBP -  ");
    }

    [Fact]
    public void NoPrefixQuestionOnlyDecimalFormat_StillPadsAsBefore()
    {
        // Sibling already-working case: a pure question-mark decimal format with no visible
        // prefix/suffix (blank affixes) already routed through FormatQuestionPlaceholderNumber
        // and padded correctly -- must remain unaffected by this fix.
        var result = NumberFormatter.Format(new NumberValue(1), "0.??");

        result.Should().Be("1.  ");
    }
}
