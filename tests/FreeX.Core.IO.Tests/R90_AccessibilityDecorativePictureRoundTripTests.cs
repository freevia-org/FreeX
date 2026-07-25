using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R90-app-accessibility-checker-5-2: a picture's "Mark as decorative" flag (<see
/// cref="PictureModel.IsDecorative"/>) must round-trip through a real .xlsx save/load -- otherwise
/// simply opening and resaving a workbook containing a decorative picture in FreeX permanently loses
/// the marking, and the picture becomes a real "Missing alternative text" finding (in both FreeX and
/// real Excel) on every subsequent open. Drives the real product entry point,
/// <see cref="XlsxFileAdapter.Save"/>/<see cref="XlsxFileAdapter.Load"/>.
/// </summary>
public sealed class R90_AccessibilityDecorativePictureRoundTripTests
{
    [Fact]
    public void SaveThenLoad_PreservesDecorativeFlag_AndAccessibilityCheckerStaysExempt()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("DecorativePictureRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Divider",
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            IsDecorative = true
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedPicture = reloaded.GetSheet("Sheet1")!.Pictures
            .Should().ContainSingle("the picture must survive the round-trip").Subject;
        reloadedPicture.IsDecorative.Should().BeTrue(
            "the 'Mark as decorative' extension must round-trip through save/load");

        var issues = AccessibilityCheckerService.FindIssues(reloaded);
        issues.Should().NotContain(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    [Fact]
    public void SaveThenLoad_LeavesOrdinaryPictureFlaggedForAltTextIssue_WhenNotMarkedDecorative()
    {
        // No-regression sibling: an ordinary (non-decorative) picture with no explicit alt text/title
        // must still round-trip as non-decorative and still be flagged after reload -- the fix must
        // not make every picture exempt. The writer always synthesizes a fallback cNvPr name (e.g.
        // "Picture 1") when none is authored, so the reloaded picture's accessible-text candidate is
        // that generic default name -- it comes back as GenericAltText rather than MissingAltText, but
        // either way it must still be a real, non-exempt accessibility finding.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("OrdinaryPictureRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64,
            IsDecorative = false
        });

        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var reloaded = adapter.Load(stream);

        var reloadedPicture = reloaded.GetSheet("Sheet1")!.Pictures
            .Should().ContainSingle().Subject;
        reloadedPicture.IsDecorative.Should().BeFalse();

        var issues = AccessibilityCheckerService.FindIssues(reloaded);
        issues.Should().ContainSingle(i =>
            i.Kind == AccessibilityIssueKind.MissingAltText || i.Kind == AccessibilityIssueKind.GenericAltText);
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];
}
