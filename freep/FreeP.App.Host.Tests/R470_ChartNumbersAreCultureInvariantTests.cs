using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r470: chart values must be written invariant, whatever the operator's locale.
///
/// <para>Under a comma-decimal locale (de-DE, fr-FR, most of Europe) an unqualified
/// <c>double.ToString()</c> emits <c>1,5</c>. Every OOXML numeric field is defined as invariant, so
/// such a value makes the part unreadable or silently changes the number. FreeX already carries
/// tests for this on its save path (R24, R108, R145, R275); FreeW and FreeP carried none.</para>
///
/// <para>This test deliberately targets chart series values rather than "save a deck under de-DE and
/// look for commas". That broader shape was measured first and goes VACUOUSLY GREEN: .docx and
/// .pptx express every measurement as an integer (twips, EMU, 60000ths of a degree), so a saved
/// deck's only decimal text is the <c>version="1.0"</c> of each part's XML declaration. A guard
/// asserting "no commas" there would pass forever while testing nothing. Chart values are the one
/// genuine decimal surface in the format, so the assertion below is anchored to a value that really
/// is written as a decimal - and <see cref="TheChartActuallyContainsADecimalValue"/> pins that
/// premise so this cannot decay into the vacuous version.</para>
/// </summary>
public class R470_ChartNumbersAreCultureInvariantTests
{
    private static ChartShape FractionalChart()
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        var series = new ChartSeries { Name = "Revenue" };
        series.Values.Add(1.5);
        series.Values.Add(-2.25);
        series.Values.Add(1234.75);
        chart.Series.Add(series);
        return chart;
    }

    private static string WriteChartXml(ChartShape chart)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            PptxChartWriter.WriteChartPart(archive, chart, 1);

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var chartStream = readArchive.GetEntry("ppt/charts/chart1.xml")!.Open();
        using var reader = new StreamReader(chartStream);
        return reader.ReadToEnd();
    }

    private static string WriteUnder(string culture, ChartShape chart)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return WriteChartXml(chart);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // Matches a comma-decimal sitting in an attribute value or element text.
    private const string CommaDecimal = "(?:=\"|>)(-?[0-9]{1,9},[0-9]{1,9})(?:\"|<)";

    [Fact]
    public void TheDetectorRecognisesACommaDecimal()
    {
        // The scan must be shown capable of firing before a clean result from it means anything.
        Regex.Matches("<c:v>1,5</c:v>", CommaDecimal).Should().HaveCount(1);
        Regex.Matches("<c:v>1.5</c:v>", CommaDecimal).Should().BeEmpty();
    }

    [Fact]
    public void TheChartActuallyContainsADecimalValue()
    {
        // Non-vacuity. If chart values ever stop being written as decimals, the culture assertions
        // below become meaningless, and this fails to say so rather than staying quietly green.
        var xml = WriteUnder("en-US", FractionalChart());

        xml.Should().Contain("1.5", "the authored series value is written as a decimal");
        xml.Should().Contain("1234.75");
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ru-RU")]
    public void ACommaDecimalLocaleStillWritesInvariantNumbers(string culture)
    {
        var xml = WriteUnder(culture, FractionalChart());

        Regex.Matches(xml, CommaDecimal).Should().BeEmpty(
            "OOXML numeric fields are invariant, so a locale must not change what is written");
        xml.Should().Contain("1.5").And.Contain("-2.25").And.Contain("1234.75");
    }

    [Fact]
    public void TheSameBytesAreProducedWhateverTheLocale()
    {
        // The strongest form: locale must not perturb the part at all, not merely avoid commas.
        var american = WriteUnder("en-US", FractionalChart());
        var german = WriteUnder("de-DE", FractionalChart());

        german.Should().Be(american);
    }
}
