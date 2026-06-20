using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

public class RtfRoundTripTests
{
    private static TextDocument DocOf(params string[] paragraphs)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var text in paragraphs)
            document.Blocks.Add(new Paragraph(text));
        return document;
    }

    private static byte[] Save(TextDocument document)
    {
        var adapter = new RtfFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        return ms.ToArray();
    }

    private static TextDocument Load(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return new RtfFileAdapter().Load(ms);
    }

    private static string[] Lines(TextDocument document) =>
        document.Blocks.OfType<Paragraph>().Select(p => p.PlainText).ToArray();

    [Fact]
    public void RoundTrip_PreservesParagraphText()
    {
        var reloaded = Load(Save(DocOf("First paragraph", "Second paragraph", "Third")));
        Lines(reloaded).Should().Contain("First paragraph");
        Lines(reloaded).Should().Contain("Second paragraph");
        Lines(reloaded).Should().Contain("Third");
    }

    [Fact]
    public void RoundTrip_PreservesNonAscii()
    {
        // Exercises the \uN / code-page escape path — non-ASCII must survive byte-for-byte at the char level.
        var reloaded = Load(Save(DocOf("café — naïve — ☕ — Ωμέγα")));
        Lines(reloaded).Should().Contain("café — naïve — ☕ — Ωμέγα");
    }

    [Fact]
    public void Save_IsDeterministic()
    {
        var document = DocOf("Determinism", "matters");
        Save(document).Should().Equal(Save(document));
    }

    [Fact]
    public void Adapter_ExposesRtfOpenSaveFormat()
    {
        IDocumentFileAdapter adapter = new RtfFileAdapter();
        adapter.Formats.Should().ContainSingle();
        adapter.Formats[0].Extension.Should().Be(".rtf");
        adapter.Formats[0].CanOpen.Should().BeTrue();
        adapter.Formats[0].CanSave.Should().BeTrue();
    }
}
