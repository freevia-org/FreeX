using FluentAssertions;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Tests.Shell;

/// <summary>
/// R90-app-window-arrange-freeze-ui-5-1: Excel numbers windows over a shared workbook as
/// "Book1:1" / "Book1:2" (colon, no surrounding spaces or dash) -- not "Book1 - 1" / "Book1 - 2".
/// <see cref="WorkbookWindowOrdering.FormatWindowTitleSuffix"/> is the single shared helper behind
/// the title bar, the Switch Windows list, and the Unhide Window dialog list on every shell, so
/// fixing it here fixes every window-naming surface at once.
/// </summary>
public sealed class R90_WorkbookWindowTitleColonSuffixTests
{
    [Theory]
    [InlineData(1, 2, ":1")]
    [InlineData(2, 2, ":2")]
    [InlineData(1, 3, ":1")]
    [InlineData(3, 3, ":3")]
    public void FormatWindowTitleSuffix_UsesExcelColonConventionNotDash(int position, int totalWindowCount, string expected)
    {
        var suffix = WorkbookWindowOrdering.FormatWindowTitleSuffix(position, totalWindowCount);

        suffix.Should().Be(expected);
        suffix.Should().NotContain(" - ", "Excel numbers windows with a colon (Book1:1), not a dash (Book1 - 1)");
    }

    // No-regression sibling: the single-window (no suffix) and out-of-range rules are unaffected by
    // switching the separator from " - " to ":".
    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(0, 0)]
    public void FormatWindowTitleSuffix_SingleWindowOrOutOfRange_StillEmpty(int position, int totalWindowCount)
    {
        WorkbookWindowOrdering.FormatWindowTitleSuffix(position, totalWindowCount).Should().BeEmpty();
    }
}
