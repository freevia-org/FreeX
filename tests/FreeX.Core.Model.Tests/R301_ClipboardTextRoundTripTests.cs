using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r301: the clipboard's two halves were each pinned against hand-written literals, and nothing ran
/// one into the other.
///
/// <para><c>ClipboardSerializerTests</c> asserts what <c>Serialize</c> produces for a given grid and
/// what <c>Deserialize</c> yields for a given string -- both against expected text typed into the
/// test. Neither test feeds the writer's output to the reader, so the only property a user actually
/// depends on -- copy in FreeX, paste in FreeX, get the same cells -- was never checked. Same shape
/// as r290: both directions covered, the relationship between them not.</para>
///
/// <para>The inputs are chosen to be exactly the ones an escaping scheme gets wrong: the delimiter
/// and row separator themselves, the quote character used to escape them, and the whitespace that
/// trimming is tempted to remove.</para>
/// </summary>
public sealed class R301_ClipboardTextRoundTripTests
{
    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null);

    /// <summary>Serialises a single cell's text and reads it back.</summary>
    private static string RoundTripOne(string text)
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel([Cell(1, 1, text)], [], []);

        var serialized = ClipboardSerializer.Serialize(
            viewport,
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)));

        var rows = ClipboardSerializer.Deserialize(serialized);
        rows.Should().HaveCount(1, $"one cell must come back as one row (serialized: {serialized})");
        rows[0].Should().HaveCount(1, $"one cell must come back as one field (serialized: {serialized})");
        return rows[0][0];
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with space")]
    [InlineData("tab\there")]                 // the field delimiter itself
    [InlineData("quote\"here")]               // the escape character itself
    [InlineData("\"leading and trailing\"")]  // text that already looks quoted
    [InlineData("comma,here")]
    [InlineData("  leading spaces")]
    [InlineData("trailing spaces  ")]
    [InlineData("")]
    public void ACellsTextSurvivesSerializeThenDeserialize(string text) =>
        RoundTripOne(text).Should().Be(text,
            "copy-then-paste inside FreeX must reproduce the cell. Each half of this pipeline is "
            + "pinned against a literal, so a disagreement between the writer's escaping and the "
            + "reader's unescaping is invisible to both");

    /// <summary>
    /// A newline inside a cell is the hardest case: it is the ROW separator, so a writer that does
    /// not quote it turns one cell into two rows and every following cell shifts.
    /// </summary>
    [Theory]
    [InlineData("line\nbreak")]
    [InlineData("crlf\r\nbreak")]
    public void ACellContainingTheRowSeparatorStaysOneCell(string text) =>
        RoundTripOne(text).Should().Be(text,
            "an unquoted row separator inside a cell splits it into two rows, which silently moves "
            + "every subsequent cell in the paste");

    /// <summary>
    /// The shape as well as the text: a 2x3 block must paste back as 2x3, including its gaps.
    /// </summary>
    [Fact]
    public void AGridsShapeAndGapsSurviveTheRoundTrip()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [
                Cell(1, 1, "a"),
                Cell(1, 3, "c"),
                Cell(2, 2, "b"),
            ],
            [],
            []);

        var serialized = ClipboardSerializer.Serialize(
            viewport,
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 3)));

        var rows = ClipboardSerializer.Deserialize(serialized);

        rows.Should().HaveCount(2);
        rows[0].Should().Equal("a", "", "c");
        rows[1].Should().Equal("", "b", "");
    }
}
