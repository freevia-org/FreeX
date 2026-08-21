using System.IO;
using System.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-163 finding F1: <see cref="Wordml2003Writer"/> — the "Word 2003 XML Document" Save As target
/// registered by <see cref="Wordml2003FileAdapter"/> — wrote every run's <c>w:t</c> straight from
/// <c>run.Text</c> with no sanitization at all. A run carrying an XML-1.0-illegal control character (the
/// documented real-world vector is RTF import) made <c>XDocument.Save</c> throw <c>ArgumentException</c>
/// and abort the whole save: the user chooses File &gt; Save As &gt; "Word 2003 XML Document (*.xml)" and
/// gets no file.
/// <para>
/// These tests drive the exact production Save As call path — <see cref="Wordml2003FileAdapter.Save"/>,
/// the <see cref="IDocumentFileAdapter"/> the file-dialog invokes for this format — rather than calling
/// <see cref="Wordml2003Writer.Write"/> directly, so the assertion is that the real user gesture succeeds
/// and reloads, not just that some internal method tolerates bad input.
/// </para>
/// <para>
/// Illegal characters are built with <c>(char)N</c> rather than embedded literally or escaped, so the
/// source file itself never carries a raw control byte (in particular, never a raw NUL byte).
/// </para>
/// </summary>
public class Wordml2003ControlCharSanitizationTests
{
    private static TextDocument DocumentWithRunText(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        return doc;
    }

    /// <summary>Saves via the real <see cref="Wordml2003FileAdapter"/> Save As entry point, then reloads via its own Load.</summary>
    private static TextDocument SaveAsAndReload(TextDocument document)
    {
        var adapter = Wordml2003FileAdapter.Wordml2003();
        using var ms = new MemoryStream();
        adapter.Save(document, ms);
        ms.Position = 0;
        return adapter.Load(ms);
    }

    [Fact]
    public void SaveAs_WithControlCharU0001_DoesNotThrow()
    {
        // U+0001 is XML-1.0-illegal; before the fix this made adapter.Save throw ArgumentException
        // and write nothing — the exact "user loses the save" failure in the finding.
        var doc = DocumentWithRunText("before" + (char)1 + "after");
        var adapter = Wordml2003FileAdapter.Wordml2003();
        var act = () =>
        {
            using var ms = new MemoryStream();
            adapter.Save(doc, ms);
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void SaveAs_WithControlCharU0001_ProducesAFileThatReloads()
    {
        var doc = DocumentWithRunText("before" + (char)1 + "after");
        var adapter = Wordml2003FileAdapter.Wordml2003();
        using var ms = new MemoryStream();
        adapter.Save(doc, ms);
        ms.Length.Should().BeGreaterThan(0, "Save As must still produce a non-empty file");
        ms.Position = 0;
        var act = () => adapter.Load(ms);
        act.Should().NotThrow();
    }

    [Fact]
    public void SaveAs_WithControlCharU0001_ControlCharIsStrippedOnReload()
    {
        var doc = DocumentWithRunText("before" + (char)1 + "after");
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
    public void SaveAs_WithAllC0ControlCharsExceptLegalOnes_StripsAllIllegal()
    {
        var chars = Enumerable.Range(0, 32).Select(i => (char)i).ToArray();
        var text = "A" + new string(chars) + "Z";
        var doc = DocumentWithRunText(text);
        var adapter = Wordml2003FileAdapter.Wordml2003();
        using var ms = new MemoryStream();
        var act = () => adapter.Save(doc, ms);
        act.Should().NotThrow("all illegal C0 control chars must be stripped, not throw");
        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var result = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        result.Should().StartWith("A").And.EndWith("Z");
        foreach (var c in result)
        {
            var code = (int)c;
            var isLegal = c == '\t' || c == '\n' || c == '\r' || code >= 0x20;
            isLegal.Should().BeTrue($"char U+{code:X4} must not appear in sanitized output");
        }
    }

    [Fact]
    public void SaveAs_NormalText_IsUnchanged()
    {
        // Sibling no-regression guard: ordinary text (the vastly more common case) must round-trip
        // byte-for-byte unchanged through the new sanitize step, not merely "not throw".
        var doc = DocumentWithRunText("Hello, world!");
        var reloaded = SaveAsAndReload(doc);
        var text = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        text.Should().Be("Hello, world!");
    }

    [Fact]
    public void SaveAs_TabAndNewlineArePreserved()
    {
        // U+0009 (tab) and U+000A (LF) are legal XML 1.0 characters and must survive the sanitize pass.
        var doc = DocumentWithRunText("hello\tworld");
        var reloaded = SaveAsAndReload(doc);
        var text = reloaded.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        text.Should().Contain("hello").And.Contain("world");
    }
}
