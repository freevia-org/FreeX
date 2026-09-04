using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r381: a number near the top of the double range must not produce a workbook FreeX cannot reopen.
///
/// <para>ClosedXML serialises with Excel's 15 significant digits. For a magnitude within a few ulps
/// of <c>double.MaxValue</c> that rounds UP past it -- <c>1.7976931348623157E+308</c> is written as
/// <c>1.79769313486232E+308</c> -- and reading that back yields Infinity, which ClosedXML rejects.
/// The SAVE SUCCEEDED and the RELOAD threw, so the failure reached the user after they believed the
/// work was stored. That is worse than refusing to save.</para>
///
/// <para>r373 recorded this as "saving double.MaxValue throws" and judged it unreachable. Both halves
/// were wrong: the save succeeds, and it is the reload that fails. The mistake came from a probe that
/// wrapped save and load in one try block, so it could not say which had thrown -- a reminder that a
/// probe's granularity is part of what it measures.</para>
/// </summary>
public sealed class R381_ExtremeNumbersDoNotProduceAnUnopenableFileTests
{
    private static Workbook WorkbookWith(double value)
    {
        var workbook = new Workbook("Extreme");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(value));
        return workbook;
    }

    private static ScalarValue? SaveAndReload(double value)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(WorkbookWith(value), stream);
        stream.Position = 0;

        var reloaded = new XlsxFileAdapter().Load(stream);
        var sheet = reloaded.GetSheetAt(0);
        return sheet.GetCell(new CellAddress(sheet.Id, 1, 1))?.Value;
    }

    [Theory]
    [InlineData(double.MaxValue)]
    [InlineData(-double.MaxValue)]
    [InlineData(1.7976931348623151E+308)]
    public void AWorkbookHoldingAnExtremeNumberCanStillBeReopened(double value)
    {
        var reopen = () => SaveAndReload(value);

        reopen.Should().NotThrow(
            "a file that saves and then cannot be opened loses the user's work after telling them it " +
            "was stored");
    }

    [Fact]
    public void TheExtremeValueIsKeptAsTextRatherThanSilentlyChanged()
    {
        // Falling back to text preserves the digits. Clamping to a smaller number would also stop the
        // crash while quietly altering the value, which is the outcome this must not have.
        var value = SaveAndReload(double.MaxValue);

        value.Should().BeOfType<TextValue>();
        ((TextValue)value!).Value.Should().Contain("1.7976931348623157",
            "the original digits survive even though Excel cannot hold the number");
    }

    [Theory]
    [InlineData(9.99999999999999E+307)]   // Excel's documented maximum
    [InlineData(1.79769313486231E+308)]   // above Excel's max, but survives the 15-digit round-trip
    [InlineData(1.5)]
    [InlineData(-2.25E+300)]
    [InlineData(0)]
    public void AnOrdinaryNumberIsStillWrittenAsANumber(double value)
    {
        // The guard is deliberately narrow: it asks only whether the value survives the round-trip,
        // not whether it exceeds Excel's range. Anything that worked before must still work, or the
        // fix would reclassify a whole band of legitimate numbers as text.
        SaveAndReload(value).Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(value);
    }
}
