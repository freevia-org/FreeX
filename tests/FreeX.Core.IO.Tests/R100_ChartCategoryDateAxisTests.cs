using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R100-io-chart-category-date-axis: a chart's category strip containing dates
/// (<see cref="DateTimeValue"/>) must be written as an Excel-compatible
/// &lt;c:cat&gt;&lt;c:numRef&gt;&lt;c:numCache&gt; date axis (with the source cell's own date
/// formatCode), not as a &lt;c:strRef&gt;&lt;c:strCache&gt; text axis with the bare OA serial number
/// printed as literal text. <see cref="IsCategoryRangeNumeric"/> (via <c>BuildChartSeries</c> and
/// <c>BuildPieFamilyChartSeries</c>) previously only recognised <see cref="NumberValue"/> as
/// numeric, so every date category column — including one Excel itself authored with a proper date
/// axis, on a plain re-save with no user edits — was silently demoted to text.
/// </summary>
public sealed class R100_ChartCategoryDateAxisTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void ColumnChart_DateCategoryColumn_WrittenAsNumRefWithDateFormatCode_NotStrRefText()
    {
        var workbook = new Workbook("ChartDateCategory");
        var sheet = workbook.AddSheet("Data");
        var dateStyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "m/d/yyyy" });

        var dates = new[]
        {
            new DateTime(2024, 1, 1),
            new DateTime(2024, 2, 1),
            new DateTime(2024, 3, 1),
        };
        for (var i = 0; i < dates.Length; i++)
        {
            var row = (uint)(2 + i);
            var dateCell = Cell.FromValue(DateTimeValue.FromDateTime(dates[i]));
            dateCell.StyleId = dateStyleId;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), dateCell);
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstColIsCategories = true,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var catElement = chartDoc.Descendants(ChartNs + "cat").Single();

        catElement.Element(ChartNs + "strRef").Should().BeNull(
            "a date category column must not be written as a text (strRef) axis");
        var numRef = catElement.Element(ChartNs + "numRef");
        numRef.Should().NotBeNull(
            "a date category column must round-trip as a numeric/date (numRef) axis, matching Excel's own output");

        var numCache = numRef!.Element(ChartNs + "numCache");
        numCache.Should().NotBeNull();
        numCache!.Element(ChartNs + "formatCode")!.Value.Should().Be("m/d/yyyy",
            "the numCache must carry the source cells' own date format, not fall back to General");

        var expectedSerials = dates
            .Select(d => DateTimeValue.FromDateTime(d).Value.ToString("G15", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var cachedValues = numCache.Elements(ChartNs + "pt").Select(pt => pt.Element(ChartNs + "v")!.Value).ToList();
        cachedValues.Should().Equal(expectedSerials,
            "the date's underlying serial number must be cached (as Excel does), not a re-derived string");
    }

    // Sibling no-regression: an ordinary text category column (the overwhelmingly common case) must
    // keep being written as strRef/strCache, unaffected by treating dates as numeric.
    [Fact]
    public void ColumnChart_TextCategoryColumn_StillWrittenAsStrRefText_NoRegression()
    {
        var workbook = new Workbook("ChartTextCategory");
        var sheet = workbook.AddSheet("Data");
        var labels = new[] { "Jan", "Feb", "Mar" };
        for (var i = 0; i < labels.Length; i++)
        {
            var row = (uint)(2 + i);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(labels[i]));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstColIsCategories = true,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var catElement = chartDoc.Descendants(ChartNs + "cat").Single();
        catElement.Element(ChartNs + "numRef").Should().BeNull();
        var strCache = catElement.Element(ChartNs + "strRef")!.Element(ChartNs + "strCache");
        strCache.Should().NotBeNull();
        strCache!.Elements(ChartNs + "pt").Select(pt => pt.Element(ChartNs + "v")!.Value)
            .Should().Equal(labels);
    }

    private static byte[] SaveToBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
