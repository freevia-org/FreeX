using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// meta F3 (round 162): RtfReader captured only <see cref="ListKind"/>/<see cref="ParagraphFormatting.ListLevel"/>
/// for a list paragraph -- the first level's <c>\levelnfc</c> was parsed just far enough to classify
/// Bullet vs Number vs MultiLevel, then discarded, so a foreign lower-roman/letter numbered list silently
/// opened showing FreeW's decimal default. This builds a raw RTF fragment by hand (a real third-party
/// producer's shape -- FreeW's own writer only ever emits <c>\levelnfc0</c>, so a save-and-reload test
/// could never see this bug) with a <c>\levelnfc2</c> (lower-roman) list and loads it directly.
///
/// <para>
/// Only <see cref="ListNumberFormat"/> is captured here, not <see cref="ParagraphFormatting.ListMarkerText"/>
/// (a bullet's literal glyph): RTF encodes that via <c>\leveltext</c> as a length-prefixed byte string that,
/// for the common Word-authored round/square bullet, is Symbol/Wingdings-font-encoded -- decoding it
/// correctly needs a font-cmap mapping this reader has no infrastructure for, and guessing would risk
/// displaying the wrong glyph, worse than the existing default. See RtfReader's <c>_listNumberFormatTable</c>
/// doc comment for the same note in the source.
/// </para>
/// </summary>
public class R162_RtfListNumberFormatFidelityTests
{
    // A third-party-shaped RTF: \listid 99 (not one of RtfWriter's own fixed 1/2/3 ids), a single
    // \levelnfc2 (lower-case Roman numeral) level, two paragraphs referencing it via \ls99\ilvl0.
    private const string ForeignRtf =
        "{\\rtf1\\ansi\\ansicpg1252\\deff0\r\n" +
        "{\\listtable{\\list\\listid99{\\listlevel\\levelnfc2\\levelnfcn2\\leveljc0\\li360\\fi-360" +
        "{\\leveltext \\'02%1.;}{\\levelnumbers\\'01;}}}}\r\n" +
        "{\\listoverridetable{\\listoverride\\listid99\\listoverridecount0\\ls99}}\r\n" +
        "\\uc1\r\n" +
        "\\pard\\ls99\\ilvl0 One\\par\r\n" +
        "\\pard\\ls99\\ilvl0 Two\\par\r\n" +
        "}";

    private static TextDocument LoadForeignRtf(string rtf)
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(rtf));
        return new RtfFileAdapter().Load(stream);
    }

    [Fact]
    public void Load_CapturesForeignLowerRomanNumberFormat()
    {
        var document = LoadForeignRtf(ForeignRtf);
        var numberItems = document.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Number)
            .ToList();

        numberItems.Should().HaveCount(2);
        numberItems.Should().OnlyContain(p => p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman,
            because: "\\levelnfc2 is lower-case Roman, not FreeW's decimal default, and must not be silently normalized to it");
    }

    /// <summary>
    /// Sibling no-regression: FreeW's own writer always emits <c>\levelnfc0</c> (decimal) for a Number
    /// list, and a plain paragraph with no list identity at all must still resolve to the Decimal default.
    /// </summary>
    [Fact]
    public void OwnWriterOutput_StillRoundTripsToDecimalNumberFormat()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("First") { Formatting = new ParagraphFormatting { ListKind = ListKind.Number } });

        using var saveStream = new MemoryStream();
        new RtfFileAdapter().Save(document, saveStream);
        saveStream.Position = 0;
        var reloaded = new RtfFileAdapter().Load(saveStream);

        reloaded.Blocks.OfType<Paragraph>().Single(p => p.Formatting.ListKind == ListKind.Number)
            .Formatting.ListNumberFormat.Should().Be(ListNumberFormat.Decimal);
    }
}
