using System.IO;
using System.IO.Compression;
using System.Linq;
using Free.Shared.IO;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// R299's save-twice idempotence check failed intermittently on CI because every ZIP entry carried
/// the wall-clock time at two-second resolution: two saves straddling a boundary differed in bytes
/// even though the document was identical. Every entry now carries one fixed time, asserted here
/// directly so the guarantee does not depend on hitting the timing window.
/// </summary>
public sealed class OdtDeterministicEntryTimestampTests
{
    [Fact]
    public void EveryPackageEntryCarriesTheFixedTimestamp()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Deterministic package"));

        using var saved = new MemoryStream();
        OdtFileAdapter.Odt().Save(document, saved);

        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.Entries.Should().NotBeEmpty();
        // ZIP stores a zone-less wall-clock time, and reading it back attaches the local offset, so
        // compare the wall-clock part: that is what lands in the bytes on every machine.
        archive.Entries.Select(entry => entry.LastWriteTime.DateTime)
            .Should().OnlyContain(time => time == OpenDocumentPackageWriter.EntryTimestamp.DateTime);
    }
}
