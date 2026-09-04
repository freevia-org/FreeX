using System.IO.Compression;
using FluentAssertions;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r314: the same document saved twice must produce the same file.
///
/// <para>r313 found that FreeX's XLSX save was not reproducible: the OPC packaging layer gives the
/// root <c>officeDocument</c> relationship a RANDOM id, so two saves of an unchanged workbook
/// differed. FreeW writes .docx through the same layer, and the shared tier is known to mirror FreeX
/// and then drift, so the question is whether the sister app has the same defect rather than whether
/// it might.</para>
///
/// <para>Volatile parts are excluded by name rather than by hoping they are stable: a package
/// records created/modified timestamps, and comparing those measures the clock.</para>
/// </summary>
public sealed class R314_DocumentSavesAreReproducibleTests
{
    private static byte[] Save()
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("r314 reproducibility"));

        using var stream = new MemoryStream();
        new DocxFileAdapter().Save(document, stream);
        return stream.ToArray();
    }

    private static Dictionary<string, string> Parts(byte[] saved)
    {
        using var stream = new MemoryStream(saved);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains("core.xml", StringComparison.OrdinalIgnoreCase))
                continue;

            using var partStream = entry.Open();
            using var buffer = new MemoryStream();
            partStream.CopyTo(buffer);
            parts[entry.FullName] = Convert.ToBase64String(buffer.ToArray());
        }

        return parts;
    }

    [Fact]
    public void SavingTheSameDocumentTwiceProducesTheSameParts()
    {
        var first = Parts(Save());
        var second = Parts(Save());

        var differing = first.Keys.Union(second.Keys, StringComparer.Ordinal)
            .Where(name => !first.TryGetValue(name, out var a) || !second.TryGetValue(name, out var b) || a != b)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        first.Should().ContainKey("_rels/.rels",
            "an empty or partial package would make the comparison below vacuous");
        first.Count.Should().BeGreaterThan(3, "a .docx has several parts; a near-empty one proves nothing");

        differing.Should().BeEmpty(
            "a document that has not changed must save to the same bytes, or version control reports "
            + "a change that is not one and sync tools re-upload an identical file");
    }
}
