using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Covers R31-io-defined-names-deep-3: a defined name's Hidden flag and Comment must survive a
/// full ClosedXML-rebuild save/load round trip, without disturbing an ordinary visible/commentless
/// defined name.
/// </summary>
public sealed class XlsxNamedRangeMapperHiddenCommentTests
{
    [Fact]
    public void SaveThenLoad_HiddenNamedRangeWithComment_StaysHiddenAndKeepsComment()
    {
        var workbook = new Workbook("HiddenNames");
        var sheet = workbook.AddSheet("Sheet1");

        workbook.DefineNamedRange(
            "HiddenRange",
            Range(sheet, 1, 1, 2, 1),
            new NamedRangeMetadata("Workbook", "Internal helper range", Hidden: true));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);

        loaded.NamedRanges.Should().ContainKey("HiddenRange");
        loaded.TryGetNamedRangeMetadata("HiddenRange", out var metadata).Should().BeTrue();
        metadata.Comment.Should().Be("Internal helper range");
        metadata.Hidden.Should().BeTrue();
    }

    [Fact]
    public void SaveThenLoad_VisibleNamedRangeWithoutComment_StaysVisibleAndCommentless()
    {
        // Sibling case: an ordinary defined name (no comment, not hidden) must keep working exactly
        // as before the fix.
        var workbook = new Workbook("VisibleNames");
        var sheet = workbook.AddSheet("Sheet1");

        workbook.DefineNamedRange("PlainRange", Range(sheet, 1, 1, 2, 1));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);

        loaded.NamedRanges.Should().ContainKey("PlainRange");
        loaded.TryGetNamedRangeMetadata("PlainRange", out var metadata).Should().BeTrue();
        metadata.Comment.Should().BeEmpty();
        metadata.Hidden.Should().BeFalse();
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
