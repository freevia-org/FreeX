using System.IO.Compression;
using System.Text;
using FluentAssertions;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r449: a .docx whose main part cannot be read must be reported, not opened as a blank page.
///
/// <para>Sibling of r448 in FreeP, found by the same probe: write a valid document with the real
/// writer, mutate one zip entry, read it back. FreeW's reader is markedly more robust than FreeP's --
/// eight of nine mutations already threw or round-tripped intact -- but one did not.</para>
///
/// <para><c>documentXml.Root?.Element(w:body)</c> is null-tolerant, so a word/document.xml whose root
/// is unrecognised (a partially written save, an unexpected namespace) read no blocks at all. The
/// "no blocks, add an empty paragraph" fallback immediately after then manufactured a convincing
/// blank page out of it. The user opens their document, sees a single empty paragraph, and saving
/// overwrites the original with that.</para>
///
/// <para>The fallback itself is right -- a Word document always has at least one paragraph. What was
/// wrong is reaching it by way of a body that was never found.</para>
/// </summary>
public sealed class R449_DamagedDocumentDoesNotOpenAsBlankTests
{
    private static byte[] DocumentBytes(int paragraphCount)
    {
        var document = new TextDocument();
        document.Blocks.Clear();

        for (var index = 0; index < paragraphCount; index++)
            document.Blocks.Add(new Paragraph("paragraph " + index));

        using var stream = new MemoryStream();
        new DocxFileAdapter().Save(document, stream);
        return stream.ToArray();
    }

    private static byte[] Rewrite(byte[] original, Func<string, byte[], byte[]?> mutate)
    {
        using var source = new MemoryStream(original);
        using var reader = new ZipArchive(source, ZipArchiveMode.Read);
        var output = new MemoryStream();

        using (var writer = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in reader.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);

                var replacement = mutate(entry.FullName, buffer.ToArray());
                if (replacement is null)
                    continue;

                var created = writer.CreateEntry(entry.FullName);
                using var createdStream = created.Open();
                createdStream.Write(replacement, 0, replacement.Length);
            }
        }

        return output.ToArray();
    }

    private static byte[] WithUnrecognisedRoot(byte[] original) =>
        Rewrite(original, (name, data) =>
            name.EndsWith("word/document.xml", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes("<unrecognised/>")
                : data);

    private static TextDocument Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return new DocxFileAdapter().Load(stream);
    }

    [Fact]
    public void ADocumentPartWithNoBodyIsReportedAsDamaged()
    {
        var damaged = WithUnrecognisedRoot(DocumentBytes(4));

        var open = () => Load(damaged);

        open.Should().Throw<InvalidDataException>(
                "opening this as a blank page and letting the user save over it destroys the " +
                "content the file still holds")
            .WithMessage("*damaged*");
    }

    [Fact]
    public void AnUndamagedDocumentIsUnaffected()
    {
        var document = Load(DocumentBytes(4));

        document.Blocks.OfType<Paragraph>().Should().HaveCount(4, "the ordinary path must not change");
    }

    [Fact]
    public void AnEmptyDocumentStillOpens()
    {
        // The guard must be narrow. A genuinely empty document still carries <w:body>, so it must
        // open exactly as before -- and it must still arrive with the one paragraph the fallback
        // provides, because a Word document always has at least one.
        var empty = DocumentBytes(0);

        var document = Load(empty);

        document.Blocks.Should().ContainSingle("an empty document is legitimate, not damaged")
            .Which.Should().BeOfType<Paragraph>();
    }

    [Fact]
    public void TheEmptyDocumentReallyDoesCarryABody()
    {
        // The premise of the test above, asserted rather than assumed: if the writer omitted w:body
        // for an empty document, the guard would reject a file this very writer produces, and the
        // narrowness claim would be false.
        using var stream = new MemoryStream(DocumentBytes(0));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.Entries.Single(e =>
            e.FullName.EndsWith("word/document.xml", StringComparison.OrdinalIgnoreCase));

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        reader.ReadToEnd().Should().Contain("body", "the narrowness of the guard depends on this");
    }
}
