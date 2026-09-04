using System.IO.Compression;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;

// This project's tests live in FreeP.App.Compositor.Tests. Declaring FreeP.App.Presentation.Tests
// here instead made "FreeP.App.Presentation" a visible namespace, which then shadowed the
// Presentation TYPE in every sibling file that imports the model -- the whole project stopped
// compiling from one new file's namespace.
namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r314: the same presentation saved twice must produce the same file.
///
/// <para>The sister-app half of r313, which found FreeX's XLSX save was not reproducible because the
/// packaging layer randomises the root <c>officeDocument</c> relationship id. FreeW's .docx turned
/// out to be clean; this asks the same question of FreeP's .pptx rather than assuming the answer
/// generalises from either.</para>
/// </summary>
public sealed class R314_PresentationSavesAreReproducibleTests
{
    private static byte[] Save()
    {
        var presentation = new Presentation();
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
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
    public void SavingTheSamePresentationTwiceProducesTheSameParts()
    {
        var first = Parts(Save());
        var second = Parts(Save());

        first.Should().ContainKey("_rels/.rels",
            "an empty or partial package would make the comparison below vacuous");
        first.Count.Should().BeGreaterThan(3, "a .pptx has several parts; a near-empty one proves nothing");

        var differing = first.Keys.Union(second.Keys, StringComparer.Ordinal)
            .Where(name => !first.TryGetValue(name, out var a) || !second.TryGetValue(name, out var b) || a != b)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        differing.Should().BeEmpty(
            "a presentation that has not changed must save to the same bytes, or version control "
            + "reports a change that is not one and sync tools re-upload an identical file");
    }
}
