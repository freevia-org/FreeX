using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-11 fix bucket R4 regression tests.
///   - R11-xlsx-drawings-1: authored TIFF/WebP pictures must keep their real bytes/content-type on save
///     (not get silently mislabelled as PNG).
///   - R11-xlsx-charts-1: chart-level c:dLblPos on line/scatter/bubble charts must be gated to a value
///     Excel actually accepts for that plot-group family (only "ctr"), never "outEnd"/"inEnd".
/// </summary>
public sealed class FreeXR11B4Tests
{
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ── R11-xlsx-drawings-1 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AuthoredTiffPicture_SavedAsXlsx_KeepsTiffBytesAndContentType()
    {
        var workbook = new Workbook("TiffPictureR11B4");
        var sheet = workbook.AddSheet("Data");
        var tiffBytes = FakeTiffBytes();
        sheet.Pictures.Add(new PictureModel
        {
            Name = "TiffPhoto",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = tiffBytes,
            ContentType = "image/tiff",
            Width = 96,
            Height = 64,
            AltText = "Authored TIFF picture"
        });

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // The media part must NOT be a .png entry containing raw TIFF bytes — it must be saved with a
        // TIFF-appropriate extension so its content type actually matches its bytes.
        archive.GetEntry("xl/media/freexPicture1.png").Should().BeNull(
            "a TIFF picture must not be written under a .png media path — that mislabels non-PNG bytes as image/png");
        var mediaEntry = archive.Entries.SingleOrDefault(entry => entry.FullName.StartsWith("xl/media/freexPicture1."));
        mediaEntry.Should().NotBeNull("the authored picture must still be written as a single freexPicture1.* media part");

        using (var mediaStream = mediaEntry!.Open())
        using (var memory = new MemoryStream())
        {
            mediaStream.CopyTo(memory);
            memory.ToArray().Should().Equal(tiffBytes, "the raw TIFF bytes must round-trip unchanged");
        }

        var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
        var extension = mediaEntry.FullName[(mediaEntry.FullName.LastIndexOf('.') + 1)..];
        var defaultContentType = contentTypesXml.Root!
            .Elements(ContentTypesNs + "Default")
            .Where(element => string.Equals((string?)element.Attribute("Extension"), extension, System.StringComparison.OrdinalIgnoreCase))
            .Select(element => (string?)element.Attribute("ContentType"))
            .FirstOrDefault();
        defaultContentType.Should().Be("image/tiff",
            "the media part's registered content type must match the actual TIFF bytes, not fall back to image/png");
    }

    [Fact]
    public void AuthoredTiffPicture_ReloadedAfterSave_StillReportsTiffContentType()
    {
        var workbook = new Workbook("TiffPictureReloadR11B4");
        var sheet = workbook.AddSheet("Data");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "TiffPhoto",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = FakeTiffBytes(),
            ContentType = "image/tiff",
            Width = 96,
            Height = 64,
            AltText = "Authored TIFF picture"
        });

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var picture = reloaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;

        // Before the fix this came back as "image/png" (the reader infers content type from the media
        // part's file extension, which was wrongly ".png").
        picture.ContentType.Should().Be("image/tiff");
        picture.ImageBytes.Should().Equal(FakeTiffBytes());
    }

    private static byte[] FakeTiffBytes() =>
    [
        0x49, 0x49, 0x2A, 0x00, // little-endian TIFF byte-order marker + magic number
        0x08, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06
    ];

    // ── R11-xlsx-charts-1 ────────────────────────────────────────────────────────────────────

    // Bubble is intentionally NOT an InlineData case here: GateDataLabelPosition collapses
    // Line/3-D line/Scatter/Bubble through the identical `isLineScatterOrBubble => "ctr"` branch
    // (XlsxChartXmlWriter.Format.cs), so Scatter already exercises the exact code path bubble uses,
    // and the simple 2-column category/value fixture below does not carry the X/Y/size series a
    // bubble chart needs to emit an xl/charts/chart1.xml part.
    [Theory]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.ThreeDLine)]
    [InlineData(ChartType.Scatter)]
    public void ChartDataLabels_OutsideEndOnLineScatterOrBubble_IsGatedToCenter(ChartType chartType)
    {
        // Excel only accepts ctr/l/r/t/b for c:dLblPos on line/3-D line/scatter/bubble plot groups;
        // FreeX's model never authors l/r/t/b, so outEnd/inEnd/bestFit must all collapse to ctr.
        using var saved = SaveWorkbookWithChartAndDataLabels(chartType, ChartDataLabelPosition.OutsideEnd);

        var dLblPos = ReadChartDLbls(saved).Element(ChartNs + "dLblPos");
        dLblPos.Should().NotBeNull();
        dLblPos!.Attribute("val")!.Value.Should().Be("ctr",
            "outEnd/inEnd are invalid c:dLblPos values for line/scatter/bubble charts and make Excel repair the file");
    }

    private static MemoryStream SaveWorkbookWithChartAndDataLabels(ChartType chartType, ChartDataLabelPosition position)
    {
        var workbook = new Workbook("ChartDataLabelPositionGateR11B4");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Title = chartType.ToString(),
            ShowDataLabels = true,
            DataLabelPosition = position
        };
        sheet.Charts.Add(chart);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static XElement ReadChartDLbls(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml", "xl/charts/chart1.xml");
        var dLbls = chartXml.Descendants(ChartNs + "dLbls").FirstOrDefault();
        dLbls.Should().NotBeNull("ShowDataLabels=true must always write a dLbls element");
        return dLbls!;
    }
}
