using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r177, found by opening FreeX-written files in a real LibreOffice: a boolean TRUE saved to .slk or
/// .dif arrived as FALSE/0 in every other implementation.
/// <para>SYLK's K field takes a number or a quoted string, and DIF's numeric slot takes a number. FreeX
/// wrote a bare <c>KTRUE</c> token and a flat <c>0,0</c> respectively, so a reader that does not
/// implement the boolean spelling falls back to the number and reads 0 -- turning every TRUE into FALSE
/// with no warning. FreeX's own round-trip tests could not see it, because its readers DO implement the
/// boolean spelling and read the value straight back.</para>
/// <para>Both writers now put the boolean's numeric equivalent (1/0) where a plain reader will look,
/// while keeping the type-carrying form alongside it -- the TRUE()/FALSE() expression for SYLK (exactly
/// how Excel and LibreOffice spell a boolean constant there) and the TRUE/FALSE indicator line for DIF.
/// Verified against LibreOffice: both now read 1.</para>
/// </summary>
public sealed class R177_SlkDifBooleanInteropTests
{
    [Fact]
    public void Slk_BooleanTrue_IsWrittenAsNumericOneWithATrueExpression()
    {
        var text = Save(new SlkFileAdapter(), new BoolValue(true));

        // FAIL-BEFORE: this line was "C;Y1;X1;KTRUE", which other readers score as 0.
        text.Should().Contain(";K1", "the K field must carry a number a plain reader can use");
        text.Should().Contain(";ETRUE()", "the boolean type rides along in the expression, as in Excel/LibreOffice");
        text.Should().NotContain("KTRUE", "a bare token in the K field is read as 0 elsewhere");
    }

    [Fact]
    public void Slk_BooleanFalse_IsWrittenAsNumericZeroWithAFalseExpression()
    {
        var text = Save(new SlkFileAdapter(), new BoolValue(false));

        text.Should().Contain(";K0");
        text.Should().Contain(";EFALSE()");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Slk_BooleanRoundTripsAsABooleanNotAFormula(bool value)
    {
        // The type must survive FreeX's own reload: the K1;ETRUE() pair is a boolean CONSTANT, so it
        // must come back as a BoolValue, not as a =TRUE() formula cell.
        var reloaded = RoundTrip(new SlkFileAdapter(), new BoolValue(value));

        reloaded.Value.Should().Be(new BoolValue(value));
        reloaded.HasFormula.Should().BeFalse("a boolean constant must not reload as a formula");
    }

    [Fact]
    public void Slk_GenuineFormulaContainingTrue_StaysAFormula()
    {
        // Guard against the boolean-expression recogniser swallowing real formulas: only a bare
        // TRUE()/FALSE() is a boolean constant.
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        var cell = Cell.FromFormula("=IF(TRUE(),1,2)");
        cell.Value = new NumberValue(1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var reloaded = RoundTripWorkbook(new SlkFileAdapter(), workbook).GetSheetAt(0).GetCell(1, 1)!;

        reloaded.HasFormula.Should().BeTrue();
        reloaded.FormulaText.Should().Contain("IF(");
    }

    [Theory]
    [InlineData(true, "0,1")]
    [InlineData(false, "0,0")]
    public void Dif_Boolean_PutsItsNumericEquivalentInTheNumericSlot(bool value, string expectedPair)
    {
        var text = Save(new DifFileAdapter(), new BoolValue(value));

        // Asserted as the two-line CHUNK, not as a bare "0,1" substring: the DIF header already
        // contains "0,1" (the TABLE topic's vector count), so a substring check would pass even with
        // the flat "0,0" this fixes -- it would be testing the header, not the boolean.
        var indicator = value ? "TRUE" : "FALSE";
        text.Should().Contain($"{expectedPair}\r\n{indicator}",
            "the numeric slot is the fallback for readers without TRUE/FALSE support, and it must " +
            "carry the boolean's numeric equivalent rather than a flat 0");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dif_BooleanRoundTripsAsABoolean(bool value)
    {
        RoundTrip(new DifFileAdapter(), new BoolValue(value)).Value.Should().Be(new BoolValue(value));
    }

    private static string Save(IFileAdapter adapter, ScalarValue value)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(value));

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Cell RoundTrip(IFileAdapter adapter, ScalarValue value)
    {
        var workbook = new Workbook("Untitled");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(value));
        return RoundTripWorkbook(adapter, workbook).GetSheetAt(0).GetCell(1, 1)!;
    }

    private static Workbook RoundTripWorkbook(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }
}
