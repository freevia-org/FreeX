using System.Text;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// meta U1 (round 163, remediation wave B): round 162 taught <see cref="RtfReader"/> to capture a foreign
/// Number list's real <c>\levelnfc</c> (<see cref="ParagraphFormatting.ListNumberFormat"/>) instead of
/// silently normalizing it to FreeW's decimal default -- but <c>RtfWriter.WriteListTable</c>/<c>WriteListEntry</c>
/// still hardcoded <c>numFmt: 0</c> (decimal) for every Number list and <c>numFmt: 23</c> with a fixed bullet
/// glyph for every Bullet list, regardless of what was just read. That flipped the defect from "visibly wrong
/// the instant you open the file" (harmless, because it's obvious) into "silently destroyed the moment you
/// press Ctrl+S with no edits" (a real, invisible data-loss regression round 162 introduced). Round 162's own
/// tests (<see cref="R162_RtfListNumberFormatFidelityTests"/>) asserted only the READ side and said so in
/// their own doc comment -- a save-and-reload test could not see this bug while the writer was uniformly
/// wrong in both directions, which is exactly why it shipped.
///
/// <para>
/// As the round-163 directive requires, these tests exercise the ONLY shape that can see this bug: build a
/// foreign RTF fragment by hand (a real third-party producer's shape -- FreeW's own writer only ever emitted
/// <c>\levelnfc0</c>/the hardcoded bullet before this fix, so a round-trip test against our own output alone
/// could never catch it), load it, save it back with zero edits, reload the saved bytes, and assert the
/// marker is still there. The fixture mixes a lower-roman Number list with a Bullet list (nfc 23) in the same
/// document, mirroring <c>OdtFileAdapter</c>'s round-163 wave A fix this one follows: the fix keys the RTF
/// <c>\listtable</c> cache on the full marker identity (a Bullet's glyph, a Number's counter format), not
/// just the <see cref="ListKind"/>, so mixed lists in one document don't collide into a shared, wrong style.
/// </para>
///
/// <para>
/// Unlike ODT/DOCX/HTML, <see cref="RtfReader"/> deliberately never captures a Bullet's literal glyph from
/// RTF itself (see its <c>_listNumberFormatTable</c> doc comment: RTF's <c>\leveltext</c> is often
/// Symbol/Wingdings-font-encoded, and decoding it correctly needs font-cmap infrastructure this reader does
/// not have) -- that is a genuine, deliberate, already-tested read-side limitation this change does not
/// touch. So a save-and-reload of a Bullet list can only assert the list KIND survives, not a custom glyph.
/// What the writer must not do, though, is silently discard a marker it IS given some other way (the model's
/// <see cref="ParagraphFormatting.ListMarkerText"/> is a plain writer input, populated by OTHER readers --
/// ODT/DOCX/HTML -- so a document opened from one of those and then Saved As RTF must not lose its custom
/// bullet to FreeW's hardcoded '•'). <see cref="SaveWithExplicitMarkerText_EmitsTheLiteralGlyphNotTheDefault"/>
/// exercises exactly that half directly against the model, since RTF itself can never supply the input for a
/// full round trip of it.
/// </para>
/// </summary>
public class R163_RtfListMarkerSaveFidelityTests
{
    // A third-party-shaped RTF mixing two list kinds: \listid50 is a Bullet list (\levelnfc23), \listid99 is
    // a Number list with \levelnfc2 (lower-case Roman) -- neither id is one of RtfWriter's own fixed/dynamic
    // ids, so this can only have come from a foreign producer.
    private const string ForeignRtf =
        "{\\rtf1\\ansi\\ansicpg1252\\deff0\r\n" +
        "{\\listtable" +
        "{\\list\\listid50{\\listlevel\\levelnfc23\\levelnfcn23\\leveljc0\\li360\\fi-360{\\leveltext \\'01\\'95;}{\\levelnumbers;}}}" +
        "{\\list\\listid99{\\listlevel\\levelnfc2\\levelnfcn2\\leveljc0\\li360\\fi-360{\\leveltext \\'02%1.;}{\\levelnumbers\\'01;}}}" +
        "}\r\n" +
        "{\\listoverridetable{\\listoverride\\listid50\\listoverridecount0\\ls50}{\\listoverride\\listid99\\listoverridecount0\\ls99}}\r\n" +
        "\\uc1\r\n" +
        "\\pard\\ls50\\ilvl0 Alpha\\par\r\n" +
        "\\pard\\ls50\\ilvl0 Beta\\par\r\n" +
        "\\pard\\ls99\\ilvl0 One\\par\r\n" +
        "\\pard\\ls99\\ilvl0 Two\\par\r\n" +
        "}";

    /// <summary>Load foreign bytes, save with zero edits, and return the reloaded document plus the raw
    /// saved RTF text (so a test can assert on both the model AND the literal emitted control words).</summary>
    private static (TextDocument Reloaded, string SavedRtf) RoundTripUnedited(string foreignRtf)
    {
        var adapter = new RtfFileAdapter();
        using var loadStream = new MemoryStream(Encoding.ASCII.GetBytes(foreignRtf));
        var document = adapter.Load(loadStream);

        using var saveStream = new MemoryStream();
        adapter.Save(document, saveStream);
        var savedBytes = saveStream.ToArray();

        using var reloadStream = new MemoryStream(savedBytes);
        var reloaded = adapter.Load(reloadStream);
        return (reloaded, Encoding.ASCII.GetString(savedBytes));
    }

    [Fact]
    public void SaveAndReload_ForeignLowerRomanNumFormatSurvivesUnedited()
    {
        var (reloaded, savedRtf) = RoundTripUnedited(ForeignRtf);

        var numberItems = reloaded.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Number)
            .ToList();
        numberItems.Should().HaveCount(2);
        numberItems.Should().OnlyContain(p => p.Formatting.ListNumberFormat == ListNumberFormat.LowerRoman,
            because: "a save with zero edits must re-emit the foreign document's own lower-roman numbering, not FreeW's decimal default");

        // Assert on the literal emitted control words too (not just the re-parsed model) -- this is the
        // exact regression shape: the writer used to hardcode \levelnfc0 regardless of what was read.
        savedRtf.Should().Contain("\\levelnfc2");
    }

    /// <summary>
    /// Mixed-kind sibling check: the Bullet list in the same foreign document must keep resolving to
    /// <see cref="ListKind.Bullet"/> across the save-and-reload -- the cache-key change (from one fixed id
    /// per <see cref="ListKind"/> to one id per marker identity) must not disturb the Bullet list just
    /// because a Number list with a different identity now shares the same \listtable.
    /// </summary>
    [Fact]
    public void SaveAndReload_MixedBulletListStillResolvesAsBullet()
    {
        var (reloaded, _) = RoundTripUnedited(ForeignRtf);

        var bulletItems = reloaded.Blocks.OfType<Paragraph>()
            .Where(p => p.Formatting.ListKind == ListKind.Bullet)
            .ToList();
        bulletItems.Should().HaveCount(2);
    }

    /// <summary>
    /// The other half of "ignoring ParagraphFormatting.ListMarkerText entirely": RTF's own reader can never
    /// supply a captured glyph (see this class's doc comment), so the only way to exercise the writer's
    /// handling of it is to set it directly on the model, as ODT/DOCX/HTML readers do. Before this fix,
    /// <c>WriteListEntry</c> unconditionally wrote <c>\'b7</c> (FreeW's default round bullet) for every
    /// Bullet list; now a custom marker is re-emitted literally.
    /// </summary>
    [Fact]
    public void SaveWithExplicitMarkerText_EmitsTheLiteralGlyphNotTheDefault()
    {
        var document = new TextDocument();
        document.Blocks.Clear();
        var paragraph = new Paragraph("Item")
        {
            Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListMarkerText = "-" }
        };
        document.Blocks.Add(paragraph);

        using var saveStream = new MemoryStream();
        new RtfFileAdapter().Save(document, saveStream);
        var savedRtf = Encoding.ASCII.GetString(saveStream.ToArray());

        savedRtf.Should().Contain("{\\leveltext -;}",
            because: "a custom marker glyph carried on the model must be re-emitted literally, not silently substituted with FreeW's default");
        savedRtf.Should().NotContain("\\'b7",
            because: "FreeW's default bullet escape must not appear when every Bullet list on the document has an explicit marker");
    }

    /// <summary>
    /// Sibling no-regression: FreeW's own writer output (no captured marker at all) must keep saving and
    /// reloading to FreeW's own defaults exactly as before this fix -- the cache-key change must not make
    /// every list distinct or otherwise disturb the plain, unmarked default path.
    /// </summary>
    [Fact]
    public void OwnWriterOutput_StillRoundTripsToDefaults()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Bulleted") { Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet } });
        document.Blocks.Add(new Paragraph("Numbered") { Formatting = new ParagraphFormatting { ListKind = ListKind.Number } });

        using var saveStream = new MemoryStream();
        new RtfFileAdapter().Save(document, saveStream);
        var savedBytes = saveStream.ToArray();
        var savedRtf = Encoding.ASCII.GetString(savedBytes);

        using var reloadStream = new MemoryStream(savedBytes);
        var reloaded = new RtfFileAdapter().Load(reloadStream);

        reloaded.Blocks.OfType<Paragraph>().Single(p => p.Formatting.ListKind == ListKind.Bullet)
            .Formatting.ListMarkerText.Should().BeNull();
        reloaded.Blocks.OfType<Paragraph>().Single(p => p.Formatting.ListKind == ListKind.Number)
            .Formatting.ListNumberFormat.Should().Be(ListNumberFormat.Decimal);

        savedRtf.Should().Contain("\\'b7");
        savedRtf.Should().Contain("\\levelnfc0");
    }
}
