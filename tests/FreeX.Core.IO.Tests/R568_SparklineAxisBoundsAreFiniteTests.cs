using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r568: a sparkline group's manual axis bounds and line weight are attributes on the
/// <c>x14:sparklineGroup</c> element, read straight out of the worksheet part, so all three are
/// file-controlled. <c>XlsxSparklineMapper.ParseDoubleAttr</c> parsed them with no finite check.
///
/// <para>An infinite <c>manualMin</c>/<c>manualMax</c> is r485's unbounded-scale class -- r548
/// named chart axis min/max as the worst-consequence member of it -- and a non-finite
/// <c>lineWeight</c> is r555's ink-thickness shape. Both reach rendering.</para>
///
/// <para>Also read this round and deliberately NOT changed:
/// <c>XlsxConditionalFormatClosedXmlMapper</c> parses with NumberStyles.Float to decide whether a
/// threshold needs quoting, and its comment states it must match ClosedXML's own check EXACTLY --
/// adding a finite test there would diverge from the library it is mirroring and reopen the
/// quoting bug that comment describes. A guard is not always an improvement.</para>
/// </summary>
public sealed class R568_SparklineAxisBoundsAreFiniteTests
{
    private static MemoryStream SaveWithSparkline()
    {
        var workbook = new Workbook("Sparkline");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint col = 1; col <= 5; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));

        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 5)),
            Location = new CellAddress(sheet.Id, 1, 6),
            Kind = SparklineKind.Line,
            ManualMin = 0,
            ManualMax = 10,
            LineWeight = 1.25,
        });

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return stream;
    }

    /// Rewrites one attribute value in the saved worksheet part, the only way to produce the
    /// hostile shape: FreeX's own Save never emits it.
    private static MemoryStream Rewrite(MemoryStream saved, string oldText, string newText)
    {
        saved.Position = 0;
        var rewritten = new MemoryStream();
        using (var source = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        using (var destination = new ZipArchive(rewritten, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var destEntry = destination.CreateEntry(entry.FullName);
                using var entryStream = entry.Open();
                using var destStream = destEntry.Open();
                if (entry.FullName == "xl/worksheets/sheet1.xml")
                {
                    var text = new StreamReader(entryStream).ReadToEnd();
                    text.Should().Contain(oldText, "the fixture must actually contain the attribute");
                    text = text.Replace(oldText, newText);
                    using var writer = new StreamWriter(destStream);
                    writer.Write(text);
                }
                else
                {
                    entryStream.CopyTo(destStream);
                }
            }
        }

        rewritten.Position = 0;
        return rewritten;
    }

    private static SparklineModel ReloadWith(string oldText, string newText)
    {
        using var saved = SaveWithSparkline();
        using var rewritten = Rewrite(saved, oldText, newText);
        return new XlsxFileAdapter().Load(rewritten).GetSheetAt(0).Sparklines.Single();
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_manual_axis_bound_that_is_not_finite_is_dropped(string hostile)
    {
        var sparkline = ReloadWith("manualMax=\"10\"", "manualMax=\"" + hostile + "\"");

        (sparkline.ManualMax is null || double.IsFinite(sparkline.ManualMax.Value))
            .Should().BeTrue("ManualMax was " + sparkline.ManualMax);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("NaN")]
    public void A_line_weight_that_is_not_finite_is_dropped(string hostile)
    {
        var sparkline = ReloadWith("lineWeight=\"1.25\"", "lineWeight=\"" + hostile + "\"");

        (sparkline.LineWeight is null || double.IsFinite(sparkline.LineWeight.Value))
            .Should().BeTrue("LineWeight was " + sparkline.LineWeight);
    }

    [Fact]
    public void Ordinary_sparkline_attributes_still_round_trip()
    {
        // Non-vacuity, and it also proves the fixture reaches the mapper at all: without this a
        // guard that dropped every attribute -- or a fixture that never loaded the sparkline --
        // would satisfy every assertion above. r566 is where that mistake actually happened.
        using var saved = SaveWithSparkline();
        var sparkline = new XlsxFileAdapter().Load(saved).GetSheetAt(0).Sparklines.Single();

        sparkline.ManualMin.Should().Be(0);
        sparkline.ManualMax.Should().Be(10);
        sparkline.LineWeight.Should().BeApproximately(1.25, 1e-9);
    }
}
