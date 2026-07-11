using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R25-defined-name-io-deep-3: Excel allows a defined name's first character to be a
/// letter, underscore, OR backslash ('\') — the backslash form exists for Lotus 1-2-3
/// macro-key compatibility (e.g. "\P") and still appears in real-world xls-&gt;xlsx
/// converted workbooks. FreeX's IsValidNamedRangeStart previously rejected backslash,
/// silently dropping such names from the evaluatable model.
/// </summary>
public class R25_DefinedNameBackslashStartTests
{
    [Fact]
    public void ValidateNamedRangeName_BackslashLeadingName_IsValid()
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName("\\P").Should().BeNull();
    }

    [Fact]
    public void DefineNamedRange_BackslashLeadingName_StoresRange()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));

        wb.DefineNamedRange("\\P", range);

        wb.NamedRanges.Should().ContainKey("\\P");
        wb.NamedRanges["\\P"].Should().Be(range);
    }

    [Theory]
    [InlineData("Sa\\les")] // backslash is only valid as the FIRST character, not mid-name
    [InlineData("Sales\\")] // ...nor as a trailing character
    public void ValidateNamedRangeName_BackslashNotLeading_IsStillInvalid(string name)
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName(name).Should().NotBeNull();
    }

    // ── Sibling/regression coverage: previously-working start characters must still work ──

    [Theory]
    [InlineData("Sales")]
    [InlineData("_2026_Sales")]
    public void ValidateNamedRangeName_LetterOrUnderscoreStart_StillValid(string name)
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName(name).Should().BeNull();
    }

    [Fact]
    public void ValidateNamedRangeName_DigitStart_StillRejected()
    {
        var wb = new Workbook();

        wb.ValidateNamedRangeName("1Sales").Should().Contain("start with a letter or underscore");
    }
}
