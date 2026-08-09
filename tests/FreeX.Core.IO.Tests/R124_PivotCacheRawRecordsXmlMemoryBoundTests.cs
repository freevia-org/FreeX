using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 124 regression tests for the TryReadRawPivotCacheRecordsXml zip-bomb DoS defect
/// (src/FreeX.Core.IO/XlsxPivotCacheReader.cs:151, MED severity).
///
/// For a pivotCache whose source type is External/Consolidation/Scenario,
/// <see cref="XlsxPivotCacheReader"/>.Load captures the pivotCacheRecordsN.xml part verbatim for
/// round-trip passthrough (RawRecordsXml) via TryReadRawPivotCacheRecordsXml. That method used to
/// open the entry and do `using var reader = new StreamReader(stream); return reader.ReadToEnd();`
/// with no character/byte cap, unlike every other XML part in this codebase (which routes through
/// XlsxPackageXmlEditor.LoadXml / OpcXml.LoadXml, applying SecureXmlReaderSettings with a
/// MaxCharactersInDocument ceiling). WorkbookOpenSizeGuard only validates the zip central
/// directory's *declared* entry Length/CompressedLength -- fields an attacker fully controls -- and
/// never verifies what DeflateStream actually yields when read, so a pivotCacheRecordsN.xml part
/// whose declared header size passes the guard but whose real decompressed content is huge would
/// previously be slurped entirely into a single in-memory string, unbounded, whenever a workbook
/// declares any External/Consolidation/Scenario-sourced pivot cache (reached from the normal,
/// un-try/catch-guarded package-metadata phase of XlsxFileAdapter.LoadCore via
/// XlsxPivotTableReader.Load -&gt; XlsxPivotCacheReader.Load).
///
/// These tests call <see cref="XlsxPivotCacheReader"/>.Load directly (internal, reachable via
/// InternalsVisibleTo) with a hand-built ZipArchive -- the real reader entry point one layer above
/// the private TryReadRawPivotCacheRecordsXml helper the defect names, and the exact function
/// XlsxPivotTableReader.Load (in turn called from XlsxFileAdapter.LoadCore's package-metadata phase)
/// calls on every real .xlsx open. Going through the full public XlsxFileAdapter.Load entry point
/// would additionally require a fully package-health-valid External pivot cache/pivot table part
/// set unrelated to this defect; XlsxPivotCacheReader.Load is the narrowest real seam that still
/// exercises the actual archive-reading code path (not a hand-built model).
/// </summary>
public sealed class R124_PivotCacheRawRecordsXmlMemoryBoundTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string PivotCacheRecordsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";
    private const string CacheDefinitionPath = "xl/pivotCache/pivotCacheDefinition1.xml";
    private const string CacheRecordsPath = "xl/pivotCache/pivotCacheRecords1.xml";

    private const long AllocationCeiling = 250_000_000; // 250 MB

    // A highly-compressible pivotCacheRecords body: valid, well-formed XML overall (opens with a
    // real <pivotCacheRecords> root, closes it properly) but with ~300M characters of filler inside
    // a comment, forcing any bounded reader to give up well before reaching the end. This isn't an
    // adversarial hand-crafted DEFLATE bitstream (that targets the *declared vs real size* mismatch
    // in WorkbookOpenSizeGuard, a different finding) -- it's realistic repetitive content that
    // DEFLATE compresses to a few KB almost instantly, sufficient to prove
    // TryReadRawPivotCacheRecordsXml no longer materializes the whole thing in memory once read.
    private const int FillerCharacterCount = 300_000_000;

    [Fact]
    public void Load_ExternalCacheWithOversizedRecordsEntry_DoesNotUnboundedlyMaterializeIt()
    {
        using var package = CreateExternalPivotCachePackageWithOversizedRecords(FillerCharacterCount);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = BuildWorkbookXml();
        var workbookRels = BuildWorkbookRels();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var caches = XlsxPivotCacheReader.Load(archive, workbookXml, workbookRels, WorkbookNs, RelNs);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        caches.Should().ContainSingle();
        // A part that hits the character cap is treated conservatively (RawRecordsXml comes back
        // null instead of a truncated/corrupt passthrough) -- the correctness of that fallback isn't
        // the point of this assertion (see the no-regression sibling below), the point is that
        // reaching it must not require holding the entire ~300M-character decompressed part in
        // memory as one string, the way StreamReader.ReadToEnd() did.
        caches[0].RawRecordsXml.Should().BeNull();
        allocated.Should().BeLessThan(
            AllocationCeiling,
            "TryReadRawPivotCacheRecordsXml must route the pivotCacheRecordsN.xml read through the " +
            "same char-capped reader every other XML part in this codebase uses, instead of an " +
            "unbounded StreamReader.ReadToEnd() that would allocate close to the full " +
            $"{FillerCharacterCount:N0}-character decompressed payload (~{FillerCharacterCount * 2L:N0} bytes as a UTF-16 string)");
    }

    // --- no-regression sibling: the same seam must still correctly capture a genuine, normally-
    // sized raw pivotCacheRecords passthrough (the exact scenario
    // R91_ExternalPivotCacheRecordsPreservationTests already covers through the full public
    // XlsxFileAdapter.Load entry point) once routed through the new capped reader. ---

    [Fact]
    public void Load_ExternalCacheWithNormalSizedRecordsEntry_StillCapturesRawRecordsXmlVerbatim()
    {
        const string recordsXml =
            "<pivotCacheRecords xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"2\">" +
            "<r><s v=\"East\"/><n v=\"10\"/></r>" +
            "<r><s v=\"West\"/><n v=\"20\"/></r>" +
            "</pivotCacheRecords>";

        using var package = CreateExternalPivotCachePackage(recordsXml);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var workbookXml = BuildWorkbookXml();
        var workbookRels = BuildWorkbookRels();

        var caches = XlsxPivotCacheReader.Load(archive, workbookXml, workbookRels, WorkbookNs, RelNs);

        caches.Should().ContainSingle();
        caches[0].RawRecordsXml.Should().NotBeNullOrWhiteSpace(
            "a normal-sized External-source pivot cache's records must still be captured verbatim " +
            "after routing through the capped reader -- only the unbounded memory allocation was " +
            "the defect, not the capture itself");

        var preserved = XDocument.Parse(caches[0].RawRecordsXml!);
        var records = preserved.Root!.Elements(WorkbookNs + "r").ToList();
        records.Should().HaveCount(2);
        records[0].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("East");
        records[1].Element(WorkbookNs + "s")!.Attribute("v")!.Value.Should().Be("West");
    }

    private static XDocument BuildWorkbookXml() =>
        XDocument.Parse(
            $"""
            <workbook xmlns="{WorkbookNs}" xmlns:r="{RelNs}">
              <pivotCaches>
                <pivotCache cacheId="1" r:id="rId1"/>
              </pivotCaches>
            </workbook>
            """);

    private static Dictionary<string, string> BuildWorkbookRels() =>
        new(StringComparer.OrdinalIgnoreCase) { ["rId1"] = CacheDefinitionPath };

    private static MemoryStream CreateExternalPivotCachePackage(string recordsXml)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, CacheDefinitionPath, CacheDefinitionXml);
            WriteEntry(archive, XlsxPackagePath.GetRelationshipPartPath(CacheDefinitionPath), CacheDefinitionRelsXml);
            WriteEntry(archive, CacheRecordsPath, recordsXml);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateExternalPivotCachePackageWithOversizedRecords(int fillerCharacterCount)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, CacheDefinitionPath, CacheDefinitionXml);
            WriteEntry(archive, XlsxPackagePath.GetRelationshipPartPath(CacheDefinitionPath), CacheDefinitionRelsXml);

            var recordsEntry = archive.CreateEntry(CacheRecordsPath, CompressionLevel.Optimal);
            using var entryStream = recordsEntry.Open();
            using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            writer.Write($"<pivotCacheRecords xmlns=\"{WorkbookNs}\" count=\"0\"><!--");

            // Write the filler in reused chunks rather than materializing one giant string, so the
            // *test fixture construction itself* isn't what allocates hundreds of MB (that would
            // muddy the GC.GetAllocatedBytesForCurrentThread measurement taken only around the
            // XlsxPivotCacheReader.Load call above).
            const int chunkSize = 1_000_000;
            var chunk = new string('x', chunkSize);
            var remaining = fillerCharacterCount;
            while (remaining > 0)
            {
                var toWrite = Math.Min(chunkSize, remaining);
                writer.Write(toWrite == chunkSize ? chunk : chunk[..toWrite]);
                remaining -= toWrite;
            }

            writer.Write("--></pivotCacheRecords>");
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static readonly string CacheDefinitionXml =
        $"""
        <pivotCacheDefinition xmlns="{WorkbookNs}" xmlns:r="{RelNs}" refreshedBy="FreeX" refreshedDate="0" createdVersion="0" refreshedVersion="0" minRefreshedVersion="0" recordCount="0">
          <cacheSource type="external"/>
          <cacheFields count="0"/>
        </pivotCacheDefinition>
        """;

    private static readonly string CacheDefinitionRelsXml =
        $"""
        <Relationships xmlns="{PackageRelNs}">
          <Relationship Id="rId1" Type="{PivotCacheRecordsRelationshipType}" Target="pivotCacheRecords1.xml"/>
        </Relationships>
        """;
}
