using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r549: the third app in the r547/r548 sweep. FreeX's WRITERS were already clean (r546 found their
/// non-finite guards in place), but its readers were not, and the picture-rotation reader shows why
/// the two sides have to be checked separately: it normalises with <c>rotation / 60000 % 360</c>, and
/// since Infinity / 60000 is Infinity while Infinity % 360 is NaN, an overflowing attribute did not
/// merely pass through -- the reader MANUFACTURED a NaN and handed it to the model.
///
/// <para>The writer refuses non-finite, so reading a crafted file was the only route to a NaN
/// rotation on a picture, and nothing downstream would have reported where it came from.</para>
/// </summary>
public class R549_NonFiniteNumbersDoNotEnterTheWorkbookTests
{
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static Workbook WorkbookWithRotatedPicture()
    {
        var workbook = new Workbook("R549");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Rotated",
            Anchor = new CellAddress(sheet.Id, 2, 3),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPng(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            RotationDegrees = 33,
        });
        return workbook;
    }

    private static MemoryStream SaveAndPatchDrawing(string find, string replace)
    {
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(WorkbookWithRotatedPicture(), saved);

        var buffer = new MemoryStream();
        buffer.Write(saved.ToArray());
        buffer.Position = 0;

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/drawings/drawing1.xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open()))
                xml = reader.ReadToEnd();

            xml.Should().Contain(find, "the fixture must actually carry the attribute under test");
            var patched = xml.Replace(find, replace);

            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("xl/drawings/drawing1.xml").Open());
            writer.Write(patched);
        }

        buffer.Position = 0;
        return buffer;
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void An_unusable_rotation_does_not_reach_the_picture(string hostile)
    {
        using var patched = SaveAndPatchDrawing("rot=\"1980000\"", "rot=\"" + hostile + "\"");

        var picture = new XlsxFileAdapter().Load(patched).GetSheetAt(0).Pictures.Should().ContainSingle().Subject;

        double.IsFinite(picture.RotationDegrees).Should().BeTrue(
            "the reader normalises with % 360, which turns an infinite angle into NaN rather than "
            + "rejecting it, and the writer refuses non-finite so nothing downstream would explain it");
    }

    [Fact]
    public void An_ordinary_rotation_still_round_trips()
    {
        // Non-vacuity: a guard that dropped every rotation would satisfy the assertions above.
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(WorkbookWithRotatedPicture(), saved);
        saved.Position = 0;

        adapter.Load(saved).GetSheetAt(0).Pictures.Single().RotationDegrees.Should().Be(33);
    }
}
