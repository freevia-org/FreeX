using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Regression tests for two defects in the Word 97-2003 (.doc) writer that the existing suite
/// could not see, because every document it exercises is only a few hundred characters long.
/// <list type="bullet">
/// <item>The CFB directory entry hardcoded each stream's declared size to the 4096-byte mini-stream
/// cutoff. Only a document small enough to pad UP to exactly 4096 was self-consistent; anything
/// larger declared 8 sectors while the FAT chain ran the stream's real length, and no reader would
/// open the file.</item>
/// <item>The FAT was a single 512-byte sector (128 entries), capping a file at 64 KB. Past that the
/// writer indexed off the end of its array and threw instead of saving.</item>
/// <item>Per-document offsets lived in STATIC fields, so two concurrent saves overwrote each
/// other's stream positions.</item>
/// </list>
/// </summary>
public sealed class R144_LegacyDocWriterConcurrencyTests
{
    private static TextDocument DocumentOfChars(int chars)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph(new string('x', chars)));
        return doc;
    }

    [Theory]
    [InlineData(500)]     // fits the old hardcoded 4096 declaration -- passed even before the fix
    [InlineData(1000)]    // first size that exceeded it
    [InlineData(8000)]
    [InlineData(30000)]   // past the old single-FAT-sector 64 KB ceiling: used to THROW on save
    [InlineData(120000)]
    public void Save_DocumentOfAnySize_ProducesAFileThatLoadsBack(int chars)
    {
        var adapter = new LegacyDocFileAdapter();
        using var ms = new MemoryStream();

        adapter.Save(DocumentOfChars(chars), ms);
        ms.Position = 0;

        adapter.Load(ms).Blocks.Should().NotBeEmpty(
            "a saved .doc must be readable back regardless of how long the document is");
    }

    [Fact]
    public void Save_ManyDocumentsConcurrently_EveryFileStillLoads()
    {
        // Deliberately varied sizes: identical documents produce identical offsets, so a torn
        // write would still be self-consistent and the race would hide.
        var documents = Enumerable.Range(1, 24).Select(i => DocumentOfChars(i * 400)).ToArray();

        var adapter = new LegacyDocFileAdapter();
        var failures = new ConcurrentBag<string>();

        Parallel.ForEach(documents, doc =>
        {
            try
            {
                using var ms = new MemoryStream();
                adapter.Save(doc, ms);
                ms.Position = 0;
                if (adapter.Load(ms).Blocks.Count == 0)
                    failures.Add("loaded document had no blocks");
            }
            catch (Exception ex)
            {
                failures.Add(ex.GetBaseException().Message);
            }
        });

        failures.Should().BeEmpty(
            "a .doc save must not depend on no other save running at the same time");
    }
}
