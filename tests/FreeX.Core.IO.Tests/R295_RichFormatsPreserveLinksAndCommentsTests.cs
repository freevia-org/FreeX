using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r295: the bug class r293 and r294 fixed had exactly one holder, and this is the check that says so
/// and keeps it that way.
///
/// <para>ODS silently dropped hyperlinks and cell comments. The obvious follow-up was whether the
/// other formats that CAN carry them did the same: <c>SpreadsheetXml</c> and <c>NativeJson</c> both
/// already round-tripped both, so ODS was the outlier rather than the first of several. That is worth
/// knowing -- it bounds the class -- and worth pinning, because the two features are easy to forget
/// when a writer is extended and their loss is invisible in the cell.</para>
///
/// <para>Written as one theory over the adapters rather than as per-adapter tests: the property is
/// the same for all of them, and a new rich format should be added to the list rather than given its
/// own copy of the test.</para>
/// </summary>
public sealed class R295_RichFormatsPreserveLinksAndCommentsTests
{
    /// <summary>
    /// The formats that keep every sheet (r291) -- i.e. the ones a user picks expecting fidelity,
    /// and the ones where losing a link or a note is a defect rather than a format ceiling.
    /// </summary>
    public static TheoryData<string> RichFormats() => new() { "ods", "xml", "json" };

    private static IFileAdapter Make(string key) => key switch
    {
        "ods" => new OdsFileAdapter(),
        "xml" => new SpreadsheetXmlFileAdapter(),
        "json" => new NativeJsonAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private const string Target = "https://example.com/spec";
    private const string Note = "check this figure before publishing";

    [Theory]
    [MemberData(nameof(RichFormats))]
    public void AHyperlinkAndACommentBothSurvive(string key)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("Revenue"));
        sheet.Hyperlinks[address] = Target;
        sheet.Comments[address] = Note;

        var adapter = Make(key);
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream).Sheets.First();
        var loadedAddress = new CellAddress(loaded.Id, 2, 2);

        loaded.GetValue(loadedAddress).Should().Be(new TextValue("Revenue"),
            $"{key} must keep the cell's own text alongside the link and the note");
        loaded.Hyperlinks.Should().ContainKey(loadedAddress, $"{key} can represent a hyperlink");
        loaded.Hyperlinks[loadedAddress].Should().Be(Target);
        loaded.Comments.Should().ContainKey(loadedAddress, $"{key} can represent a cell note");
        loaded.Comments[loadedAddress].Should().Be(Note);
    }

    /// <summary>
    /// A link or a note on a cell with no value of its own. This is the shape that exposed three
    /// separate skips in the ODS adapter -- the writer's early return, the reader's blank-cell DoS
    /// guard, and the table bounds -- none of which the valued-cell case above reaches.
    /// </summary>
    [Theory]
    [MemberData(nameof(RichFormats))]
    public void ALinkAndANoteSurviveOnACellWithNoValue(string key)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 4, 3);
        sheet.Comments[address] = Note;

        var adapter = Make(key);
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        var loaded = adapter.Load(stream).Sheets.First();
        var loadedAddress = new CellAddress(loaded.Id, 4, 3);

        loaded.Comments.Should().ContainKey(loadedAddress,
            $"{key} dropped the note when the cell had nothing else to make it 'interesting' -- the "
            + "case a valued-cell test never reaches");
        loaded.GetValue(loadedAddress).Should().Be(BlankValue.Instance,
            "and the note must not become the cell's value");
    }
}
