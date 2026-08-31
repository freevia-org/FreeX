using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxExternalLinkAuthoringWriterRelationshipIndexTests
{
    private static readonly XNamespace WorkbookNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ExternalLinkPathRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";

    [Fact]
    public void BuildBookKeyOrdinals_UsesFirstTrimmedCaseInsensitiveRelationshipAndSkipsUnresolvableEntries()
    {
        var workbookRelationships = new XDocument(
            new XElement(PackageRelNs + "Relationships",
                Relationship(" RIdShared ", "externalLinks/externalLink1.xml"),
                Relationship("ridshared", "externalLinks/externalLink2.xml"),
                new XElement(PackageRelNs + "Relationship", new XAttribute("Id", "missingTarget")),
                Relationship("missingSidecar", "externalLinks/externalLink3.xml"),
                Relationship("missingPathRelationship", "externalLinks/externalLink4.xml"),
                Relationship(" rIdWhitespace ", " externalLinks/externalLink5.xml ")));
        var externalReferences = new XElement(WorkbookNs + "externalReferences",
            ExternalReference(" rIDshared "),
            new XElement(WorkbookNs + "externalReference"),
            ExternalReference("missingRelationship"),
            ExternalReference("missingTarget"),
            ExternalReference("missingSidecar"),
            ExternalReference("missingPathRelationship"),
            ExternalReference(" RIDWHITESPACE "),
            ExternalReference("ridshared"));
        using var package = XlsxPackageTestFixtures.CreatePackage(
            ("xl/externalLinks/_rels/externalLink1.xml.rels", ExternalLinkRelationships("  BookA.xlsx  ")),
            ("xl/externalLinks/_rels/externalLink2.xml.rels", ExternalLinkRelationships("WrongDuplicate.xlsx")),
            ("xl/externalLinks/_rels/externalLink4.xml.rels", XlsxPackageTestFixtures.RelationshipsXml(
                XlsxPackageTestFixtures.Relationship("rId1", "wrong-type", "WrongPath.xlsx"))),
            ("xl/externalLinks/_rels/externalLink5.xml.rels", ExternalLinkRelationships("  BookE.xlsx  ", padType: true)));
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var result = XlsxExternalLinkAuthoringWriter.BuildBookKeyOrdinals(
            archive,
            workbookRelationships,
            externalReferences);

        result.Should().HaveCount(2);
        result["BookA.xlsx"].Should().Be(1,
            "the first workbook relationship with a duplicate trimmed ID must retain ownership");
        result["BookE.xlsx"].Should().Be(7,
            "unresolvable external references still retain their ordinal slots");
        result.Should().NotContainKey("WrongDuplicate.xlsx");
        result.Should().NotContainKey("WrongPath.xlsx");
        XlsxExternalLinkAuthoringWriter.BuildBookKeyOrdinals(
                archive,
                new XDocument(),
                externalReferences)
            .Should().BeEmpty("a missing workbook-relationships root remains an unresolved no-op");
    }

    [Fact]
    public void BuildBookKeyOrdinals_IndexesWorkbookRelationshipsOnce()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxExternalLinkAuthoringWriter.cs");
        var ordinalMethod = Slice(
            source,
            "internal static Dictionary<string, int> BuildBookKeyOrdinals",
            "private static Dictionary<string, XElement> BuildWorkbookRelationshipsById");
        var indexMethod = Slice(
            source,
            "private static Dictionary<string, XElement> BuildWorkbookRelationshipsById",
            "private static string? ResolveBookKeyForWorkbookRelationshipId");
        var resolveMethod = Slice(
            source,
            "private static string? ResolveBookKeyForWorkbookRelationshipId",
            "private static void RewriteFormulaBookReferences");

        ordinalMethod.Should().Contain("BuildWorkbookRelationshipsById(workbookRelsXml)");
        ordinalMethod.Should().Contain("ResolveBookKeyForWorkbookRelationshipId(archive, workbookRelationshipsById, relId)");
        indexMethod.Should().Contain("new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase)");
        indexMethod.Should().Contain("relationship.Attribute(\"Id\")?.Value?.Trim()");
        indexMethod.Should().Contain("result.TryAdd(id, relationship)");
        resolveMethod.Should().Contain("workbookRelationshipsById.TryGetValue(relId, out var relationship)");
        resolveMethod.Should().NotContain("workbookRelsXml");
        resolveMethod.Should().NotContain("Attribute(\"Id\")");
    }

    [BenchmarkFact]
    public void Benchmark_BuildBookKeyOrdinals_ThousandRelationships_ReportsTimingAndAllocations()
    {
        const int relationshipCount = 1_000;
        var workbookRelationships = new XDocument(new XElement(PackageRelNs + "Relationships"));
        var externalReferences = new XElement(WorkbookNs + "externalReferences");
        using var package = new MemoryStream();
        using (var createArchive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 1; index <= relationshipCount; index++)
            {
                var relId = $"rId{index}";
                var partName = $"externalLink{index}.xml";
                workbookRelationships.Root!.Add(Relationship(relId, $"externalLinks/{partName}"));
                externalReferences.Add(ExternalReference(relId));

                var entry = createArchive.CreateEntry($"xl/externalLinks/_rels/{partName}.rels");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(ExternalLinkRelationships($"Book{index}.xlsx"));
            }
        }

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        XlsxExternalLinkAuthoringWriter.BuildBookKeyOrdinals(
            archive,
            workbookRelationships,
            externalReferences).Should().HaveCount(relationshipCount);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var result = XlsxExternalLinkAuthoringWriter.BuildBookKeyOrdinals(
            archive,
            workbookRelationships,
            externalReferences);

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF XLSX_EXTERNAL_LINK_ORDINAL_INDEX " +
            $"relationships={relationshipCount} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
        result.Should().HaveCount(relationshipCount);
        result[$"Book{relationshipCount}.xlsx"].Should().Be(relationshipCount);
    }

    private static XElement Relationship(string id, string target) =>
        new(
            PackageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Target", target));

    private static XElement ExternalReference(string relId) =>
        new(WorkbookNs + "externalReference", new XAttribute(RelNs + "id", relId));

    private static string ExternalLinkRelationships(string target, bool padType = false) =>
        XlsxPackageTestFixtures.RelationshipsXml(
            XlsxPackageTestFixtures.Relationship(
                "rId1",
                padType ? $"  {ExternalLinkPathRelationshipType}  " : ExternalLinkPathRelationshipType,
                target));

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}
