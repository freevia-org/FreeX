using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R68-io-comment-note-6-3 regression coverage for XlsxWorksheetThreadedCommentMapper: a
/// threaded comment's <c>dT</c> timestamp was truncated to whole-second precision on every save
/// because <c>FormatDateTimeOffset</c> had no fractional-second token, even when the original
/// timestamp (as loaded from a source file) carried sub-second precision. The fix only omits the
/// fractional component when the value genuinely has none, so a sub-second dT now survives a save
/// that did not touch the comment, while an existing whole-second dT keeps rendering exactly as
/// before (no added ".0000000" noise -- see XlsxWorksheetThreadedCommentMapperEditedTimestampTests
/// for the exact-string assertions this must not regress).
/// </summary>
public sealed class R68_ThreadedCommentSubSecondTimestampTests
{
    private static readonly XNamespace ThreadedCommentNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2018/threadedcomments";

    [Fact]
    public void Save_PreservesSubSecondPrecision_WhenRootDTHasFractionalSeconds()
    {
        // Arrange: CreatedAtUtc/ModifiedAtUtc carry sub-second precision, as they would if read
        // from a source dT like "2026-01-15T09:30:00.1234567Z".
        var createdAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero).AddTicks(1234567);

        var workbook = new Workbook("ThreadedSubSecondDTTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        // Assert: the persisted dT keeps the sub-second component instead of truncating it away.
        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
                archive, "xl/threadedComments/threadedComment1.xml");
            var root = threadedCommentsXml.Root!.Element(ThreadedCommentNs + "threadedComment")!;
            root.Attribute("dT")!.Value.Should().Be(
                "2026-01-15T09:30:00.1234567Z",
                "a sub-second dT must round-trip its fractional seconds instead of being truncated to whole seconds (R68-io-comment-note-6-3)");
        }

        // And the fractional precision must survive a full reload of the model too.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 2, 3);
        reloadedSheet.ThreadedComments[reloadedAddress].CreatedAtUtc.Should().Be(createdAt);
    }

    [Fact]
    public void Save_KeepsWholeSecondDTUnchanged_NoRegression()
    {
        // Sibling no-regression guard: a whole-second dT must still render with no fractional
        // component at all, exactly as before this fix (matches
        // XlsxWorksheetThreadedCommentMapperEditedTimestampTests' exact-string assertions).
        var createdAt = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedWholeSecondDTTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Total"));
        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var threadedCommentsXml = XlsxPackageTestFixtures.LoadPackageXml(
            archive, "xl/threadedComments/threadedComment1.xml");
        var root = threadedCommentsXml.Root!.Element(ThreadedCommentNs + "threadedComment")!;
        root.Attribute("dT")!.Value.Should().Be(
            "2026-01-15T09:30:00Z",
            "a whole-second dT must keep rendering with no fractional-second suffix (no regression)");
    }
}
