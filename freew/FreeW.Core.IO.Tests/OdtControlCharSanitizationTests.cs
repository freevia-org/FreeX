using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-164 finding F1: <see cref="OdtFileAdapter"/> — the "OpenDocument Text" Save As target registered
/// by <see cref="DocumentFileAdapterCatalog"/> — wrote every run's text straight into <c>content.xml</c>
/// (via <c>AppendText</c>/<c>new XText</c>) with no sanitization at all, the same bug class round 162 fixed
/// in <see cref="DocxWriter"/> and round 163 fixed in <see cref="Wordml2003Writer"/>. A run carrying an
/// XML-1.0-illegal control character (a C0 control code or a lone surrogate — arriving via paste, a legacy
/// document import, or malformed clipboard content) made <c>XDocument.Save</c> throw
/// <c>ArgumentException</c> and abort the whole save: the user chooses File &gt; Save As &gt;
/// "OpenDocument Text (*.odt)" and gets no file.
/// <para>
/// These tests drive the exact production Save As call path — <see cref="OdtFileAdapter.Save"/> via the
/// <c>OdtFileAdapter.Odt()</c> factory <see cref="DocumentFileAdapterCatalog"/> registers — rather than any
/// internal helper, so the assertion is that the real user gesture succeeds and reloads.
/// </para>
/// <para>
/// Illegal characters are built with <c>(char)N</c> rather than embedded literally or escaped, so the
/// source file itself never carries a raw control byte (in particular, never a raw NUL byte).
/// </para>
/// </summary>
public class OdtControlCharSanitizationTests
{
    private static TextDocument DocumentWithRunText(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    /// <summary>Saves via the real <see cref="OdtFileAdapter"/> Save As entry point, then reloads via its own Load.</summary>
    private static TextDocument SaveAsAndReload(TextDocument document)
    {
        var adapter = OdtFileAdapter.Odt();
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        ms.Position = 0;
        return adapter.Load(ms);
    }

    [Fact]
    public void SaveAs_WithControlCharU000B_DoesNotThrow()
    {
        // U+000B (vertical tab) is XML-1.0-illegal; before the fix this made adapter.Save throw
        // ArgumentException and write nothing — the exact "user loses the save" failure in the finding.
        var doc = DocumentWithRunText("before" + (char)0x0B + "after");
        var adapter = OdtFileAdapter.Odt();
        var act = () =>
        {
            using var ms = new MemoryStream();
            adapter.Save(doc, ms);
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void SaveAs_WithControlCharU000B_ProducesAFileThatReloads()
    {
        var doc = DocumentWithRunText("before" + (char)0x0B + "after");
        var adapter = OdtFileAdapter.Odt();
        using var ms = new MemoryStream();
        adapter.Save(doc, ms);
        ms.Length.Should().BeGreaterThan(0, "Save As must still produce a non-empty file");
        ms.Position = 0;
        var act = () => adapter.Load(ms);
        act.Should().NotThrow();
    }

    [Fact]
    public void SaveAs_WithControlCharU000B_ControlCharIsStrippedOnReload()
    {
        var doc = DocumentWithRunText("before" + (char)0x0B + "after");
        var reloaded = SaveAsAndReload(doc);
        var text = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        text.Should().Be("beforeafter", "the illegal control char must be dropped, not crash the save");
    }

    [Fact]
    public void SaveAs_WithNullByteU0000_IsStripped()
    {
        var doc = DocumentWithRunText("a" + (char)0 + "b");
        var reloaded = SaveAsAndReload(doc);
        var text = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        text.Should().Be("ab");
    }

    [Fact]
    public void SaveAs_WithLoneHighSurrogate_IsStripped()
    {
        // A lone high surrogate (no matching low surrogate) is XML-1.0-illegal on its own.
        var doc = DocumentWithRunText("a" + (char)0xD800 + "b");
        var adapter = OdtFileAdapter.Odt();
        using var ms = new MemoryStream();
        var act = () => adapter.Save(doc, ms);
        act.Should().NotThrow();
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var text = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        text.Should().Be("ab");
    }

    [Fact]
    public void SaveAs_WithControlCharInDocumentTitle_DoesNotThrow()
    {
        // meta.xml carries free-text document properties (Title/Subject/Comments/Keywords), a second
        // route to the same crash independent of run text in content.xml.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text"));
        doc.Properties.Title = "Report" + (char)1 + "Title";
        var adapter = OdtFileAdapter.Odt();
        var act = () =>
        {
            using var ms = new MemoryStream();
            adapter.Save(doc, ms);
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void SaveAs_WithAllC0ControlCharsExceptLegalOnes_StripsAllIllegal()
    {
        // \t/\n/\r among these are legal XML but are also OdtFileAdapter's own structural markers
        // (text:tab / text:line-break), which split the paragraph into several Run objects on reload —
        // so this asserts against the paragraph's concatenated PlainText, not a single Run.
        var chars = Enumerable.Range(0, 32).Select(i => (char)i).ToArray();
        var text = "A" + new string(chars) + "Z";
        var doc = DocumentWithRunText(text);
        var adapter = OdtFileAdapter.Odt();
        using var ms = new MemoryStream();
        var act = () => adapter.Save(doc, ms);
        act.Should().NotThrow("all illegal C0 control chars must be stripped, not throw");
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var result = reloaded.Blocks.OfType<Paragraph>().First().PlainText;
        result.Should().StartWith("A").And.EndWith("Z");
        foreach (var c in result)
        {
            var code = (int)c;
            var isLegal = c == '\t' || c == '\n' || code >= 0x20;
            isLegal.Should().BeTrue($"char U+{code:X4} must not appear in sanitized output");
        }
    }

    [Fact]
    public void SaveAs_NormalText_IsUnchanged()
    {
        // Sibling no-regression guard: ordinary text (the vastly more common case) must round-trip
        // unchanged through the new sanitize step, not merely "not throw".
        var doc = DocumentWithRunText("Hello, world!");
        var reloaded = SaveAsAndReload(doc);
        var text = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        text.Should().Be("Hello, world!");
    }

    [Fact]
    public void SaveAs_TabAndNewlineArePreserved()
    {
        // U+0009 (tab) and U+000A (LF) are legal XML 1.0 characters and must survive the sanitize pass
        // (and OdtFileAdapter's own text:tab / text:line-break structural encoding, which splits the
        // paragraph into multiple Run objects — hence asserting on the concatenated PlainText).
        var doc = DocumentWithRunText("hello\tworld");
        var reloaded = SaveAsAndReload(doc);
        var text = reloaded.Blocks.OfType<Paragraph>().First().PlainText;
        text.Should().Contain("hello").And.Contain("world");
    }
}
