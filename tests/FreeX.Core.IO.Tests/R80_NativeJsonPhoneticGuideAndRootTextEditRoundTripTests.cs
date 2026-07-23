using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R80-meta-1 / R80-io-comments-threaded-5-2 regression coverage: FreeX's native .fxl JSON
/// format must round-trip <see cref="Sheet.CellPhoneticGuides"/> (furigana) and
/// <see cref="ThreadedComment.RootTextEditedAtUtc"/>, exactly like the already-round-tripped
/// sibling fields (<see cref="Sheet.RichTextRuns"/>, <see cref="ThreadedComment.ModifiedAtUtc"/>)
/// they sit next to in <c>WorksheetDto</c>. Before this fix, both were silently dropped by a
/// save-then-load through <see cref="NativeJsonAdapter"/>.
/// </summary>
public sealed class R80_NativeJsonPhoneticGuideAndRootTextEditRoundTripTests
{
    [Fact]
    public void SaveThenLoad_PreservesCellPhoneticGuide()
    {
        var workbook = new Workbook("PhoneticGuideRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("漢字"));

        var guide = new CellPhoneticGuide(
            RunPhoneticXmls: ["<rPh sb=\"0\" eb=\"2\"><t>かんじ</t></rPh>"],
            PhoneticPropertiesXml: "<phoneticPr fontId=\"1\"/>");
        sheet.CellPhoneticGuides[address] = guide;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        var loadedAddress = new CellAddress(loadedSheet.Id, 1, 1);

        // Bug case: the phonetic guide must survive the round trip.
        loadedSheet.CellPhoneticGuides.Should().ContainKey(loadedAddress);
        var loadedGuide = loadedSheet.CellPhoneticGuides[loadedAddress];
        loadedGuide.RunPhoneticXmls.Should().ContainSingle()
            .Which.Should().Be(guide.RunPhoneticXmls[0]);
        loadedGuide.PhoneticPropertiesXml.Should().Be(guide.PhoneticPropertiesXml);
    }

    [Fact]
    public void SaveThenLoad_LeavesCellPhoneticGuidesEmpty_WhenSheetHasNone_NoRegression()
    {
        // Sibling no-regression case: a sheet with no phonetic guides at all must continue to
        // round-trip with an empty CellPhoneticGuides dictionary, not spuriously populated.
        var workbook = new Workbook("NoPhoneticGuide");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("Plain"));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);

        loadedSheet.CellPhoneticGuides.Should().BeEmpty();
    }

    [Fact]
    public void SaveThenLoad_PreservesThreadedCommentRootTextEditedAtUtc()
    {
        var createdAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var rootEditedAt = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedCommentRootEditRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(address, new TextValue("Total"));

        sheet.ThreadedComments[address] = new ThreadedComment("Please review total (revised)", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = rootEditedAt,
            RootTextEditedAtUtc = rootEditedAt
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        var loadedComment = loadedSheet.ThreadedComments[new CellAddress(loadedSheet.Id, 3, 3)];

        // Bug case: RootTextEditedAtUtc must survive the round trip, distinct from ModifiedAtUtc.
        loadedComment.RootTextEditedAtUtc.Should().Be(rootEditedAt);
        loadedComment.ModifiedAtUtc.Should().Be(rootEditedAt);
        loadedComment.CreatedAtUtc.Should().Be(createdAt);
    }

    [Fact]
    public void SaveThenLoad_LeavesRootTextEditedAtUtcNull_WhenNeverStamped_NoRegression()
    {
        // Sibling no-regression case: a comment whose root text was never independently edited
        // (RootTextEditedAtUtc never stamped) must keep round-tripping as null, not spuriously
        // populated from ModifiedAtUtc/CreatedAtUtc.
        var createdAt = new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero);

        var workbook = new Workbook("ThreadedCommentNoRootEdit");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 4, 4);
        sheet.SetCell(address, new TextValue("Total"));

        sheet.ThreadedComments[address] = new ThreadedComment("Please review total", "Quinn")
        {
            CreatedAtUtc = createdAt,
            ModifiedAtUtc = createdAt
        };

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        var loadedComment = loadedSheet.ThreadedComments[new CellAddress(loadedSheet.Id, 4, 4)];

        loadedComment.RootTextEditedAtUtc.Should().BeNull();
    }
}
