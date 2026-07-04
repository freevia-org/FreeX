using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

// Regression coverage for J28 (#SPILL!/#CALC! silently rewritten to #N/A on save) and P3
// (#CIRCULAR! — a FreeX-invented, non-OOXML code — needs an Excel-faithful save mapping).
//
// ClosedXML 0.105.0's XLError enum only defines the 7 classic Excel error codes (NullValue,
// DivisionByZero, IncompatibleValue, CellReference, NameNotRecognized, NumberInvalid,
// NoValueAvailable); there is no member that serializes as "#SPILL!", "#CALC!", or "#CIRCULAR!".
// MapValueInverse must therefore special-case these before falling through to
// MapErrorValueInverse's classic-error switch, instead of silently downgrading everything
// unrecognized to #N/A.
public sealed class XlsxClosedXmlCellMapperErrorRoundTripTests
{
    [Theory]
    [InlineData("#SPILL!")]
    [InlineData("#CALC!")]
    public void MapValueInverse_PreservesSpillAndCalcAsVisibleTextInsteadOfNA(string code)
    {
        var mapped = XlsxClosedXmlCellMapper.MapValueInverse(new ErrorValue(code));

        // Never silently become a different, valid-but-wrong error (#N/A).
        mapped.IsError.Should().BeFalse();
        mapped.IsText.Should().BeTrue();
        mapped.GetText().Should().Be(code);
    }

    [Theory]
    [InlineData("#SPILL!")]
    [InlineData("#CALC!")]
    public void MapValueInverse_SpillAndCalc_RoundTripThroughRealWorkbookSaveAsTextNotNA(string code)
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
            cell.Value = XlsxClosedXmlCellMapper.MapValueInverse(new ErrorValue(code));
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var reloadedCell = reloaded.Worksheet("Sheet1").Cell("A1");

        reloadedCell.Value.IsError.Should().BeFalse();
        var remapped = XlsxClosedXmlCellMapper.MapValue(reloadedCell);
        remapped.Should().BeOfType<TextValue>().Which.Value.Should().Be(code);

        // The specific historical bug: it must NOT have become "#N/A".
        remapped.Should().NotBeOfType<ErrorValue>();
    }

    [Fact]
    public void MapValueInverse_Circular_MapsToZeroNotNA()
    {
        // Decision (P3): #CIRCULAR! is a FreeX-only sentinel that RecalcEngine.AddCyclicCell stamps
        // for a non-iterative circular reference. Real Excel never writes "#CIRCULAR!" to an xlsx —
        // with iterative calculation off, it persists a plain 0 in the cell. Match that on save; the
        // in-app "#CIRCULAR!" grid display is unaffected because it reads the live ScalarValue, not
        // this serialization path.
        var mapped = XlsxClosedXmlCellMapper.MapValueInverse(ErrorValue.Circular);

        mapped.IsError.Should().BeFalse();
        mapped.IsNumber.Should().BeTrue();
        mapped.GetNumber().Should().Be(0d);
    }

    [Fact]
    public void MapValueInverse_Circular_RoundTripsThroughRealWorkbookSaveAsZero()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
            cell.Value = XlsxClosedXmlCellMapper.MapValueInverse(ErrorValue.Circular);
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var reloadedCell = reloaded.Worksheet("Sheet1").Cell("A1");

        reloadedCell.Value.IsError.Should().BeFalse();
        var remapped = XlsxClosedXmlCellMapper.MapValue(reloadedCell);
        remapped.Should().BeOfType<NumberValue>().Which.Value.Should().Be(0d);
    }

    // Classic Excel error codes must still round-trip as themselves — this fix must not regress the
    // existing 7-code mapping.
    [Theory]
    [InlineData("#NULL!")]
    [InlineData("#DIV/0!")]
    [InlineData("#VALUE!")]
    [InlineData("#REF!")]
    [InlineData("#NAME?")]
    [InlineData("#NUM!")]
    [InlineData("#N/A")]
    public void MapValueInverse_ClassicErrorCodes_StillRoundTripAsThemselves(string code)
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
            cell.Value = XlsxClosedXmlCellMapper.MapValueInverse(new ErrorValue(code));
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var reloadedCell = reloaded.Worksheet("Sheet1").Cell("A1");

        reloadedCell.Value.IsError.Should().BeTrue();
        var remapped = XlsxClosedXmlCellMapper.MapValue(reloadedCell);
        remapped.Should().BeOfType<ErrorValue>().Which.Code.Should().Be(code);
    }
}
