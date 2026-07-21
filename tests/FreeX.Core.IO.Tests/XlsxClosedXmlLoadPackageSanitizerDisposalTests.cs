using System.IO.Compression;
using System.Xml;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

// R60-missing-dispose-sweep-1: XlsxClosedXmlLoadPackageSanitizer.Create allocates its own
// transient in-memory copy of the source package (`sanitized`) whenever it needs to rewrite
// untrusted XML into a new stream. The rewrite loop runs ~20 Normalize*/Remove* helpers directly
// against that untrusted content; any exception raised inside it (e.g. an XmlException from a
// malformed part one of the sanitizers is meant to recover from) used to propagate with no
// enclosing try/finally around `sanitized`, leaking the transient copy. These tests exercise that
// exact throw-mid-rewrite path via the test-only `TransientSanitizedStreamCreatedForTests` hook
// the sanitizer exposes (a no-op in production; only ever set by these tests).
public sealed class XlsxClosedXmlLoadPackageSanitizerDisposalTests
{
    [Fact]
    public void Create_DisposesTransientSanitizedStreamWhenGridNormalizationThrowsMidRewrite()
    {
        using var package = CreatePackageWithMalformedWorksheet();

        var hints = CreateHintsWithOnly(worksheetGridXmlSchemaIssues: true);

        MemoryStream? capturedTransientStream = null;
        XlsxClosedXmlLoadPackageSanitizer.TransientSanitizedStreamCreatedForTests.Value =
            stream => capturedTransientStream = stream;
        try
        {
            Action act = () => XlsxClosedXmlLoadPackageSanitizer.Create(
                package,
                removeUnsupportedConditionalFormatting: false,
                removeAllConditionalFormatting: false,
                hints);

            // The worksheet's XML is not well-formed, so XDocument.Load (inside
            // NormalizeWorksheetGridXml) throws an XmlException that must propagate unchanged.
            act.Should().Throw<XmlException>();
        }
        finally
        {
            XlsxClosedXmlLoadPackageSanitizer.TransientSanitizedStreamCreatedForTests.Value = null;
        }

        capturedTransientStream.Should().NotBeNull(
            "a non-mutating Create call always allocates its own transient copy of the source package");
        capturedTransientStream.Should().NotBeSameAs(
            package,
            "this call path never mutates the caller's own source package");

        // Before the fix, `sanitized` was never disposed on this throw path; a disposed
        // MemoryStream throws ObjectDisposedException from any member access.
        Action accessAfterThrow = () => _ = capturedTransientStream!.Position;
        accessAfterThrow.Should().Throw<ObjectDisposedException>(
            "the transient sanitized copy must be disposed when sanitization throws mid-rewrite, mirroring CreateFusedTransientPackage's try/finally");
    }

    // Sibling no-regression test: when the caller opts into in-place mutation
    // (mutateSourcePackage: true), `sanitized` IS the caller's own `sourcePackage` instance, which
    // this method does not own. The fix's dispose-on-throw guard must never reach that path -
    // only the sanitizer's own transient copy may be disposed on this throw.
    [Fact]
    public void Create_DoesNotDisposeCallerOwnedPackageWhenMutatingInPlaceAndGridNormalizationThrows()
    {
        using var package = CreatePackageWithMalformedWorksheet();

        var hints = CreateHintsWithOnly(worksheetGridXmlSchemaIssues: true);

        Action act = () => XlsxClosedXmlLoadPackageSanitizer.Create(
            package,
            removeUnsupportedConditionalFormatting: false,
            removeAllConditionalFormatting: false,
            hints,
            mutateSourcePackage: true);

        act.Should().Throw<XmlException>();

        // The caller still owns `package` after the throw; it must remain open and usable.
        Action accessAfterThrow = () => _ = package.Length;
        accessAfterThrow.Should().NotThrow<ObjectDisposedException>(
            "mutateSourcePackage:true means the sanitizer never owns sourcePackage and must not dispose it");
    }

    private static XlsxClosedXmlLoadSanitizationHints CreateHintsWithOnly(bool worksheetGridXmlSchemaIssues) =>
        new(
            HasPivotPackageMetadata: false,
            HasChartExChartParts: false,
            HasDrawingPackageParts: false,
            HasConditionalFormattingBlocks: false,
            HasUnsupportedConditionalFormattingBlocks: false,
            HasWorksheetDynamicFilters: false,
            HasWorksheetGridXmlSchemaIssues: worksheetGridXmlSchemaIssues,
            HasWorksheetPageLayoutSchemaIssues: false,
            HasWorksheetPageBreakSchemaIssues: false,
            HasWorksheetAutoFilterSchemaIssues: false,
            HasStructuredTableAutoFilterSchemaIssues: false,
            HasStructuredTableSortStateSchemaIssues: false,
            HasStructuredTableMetadataSchemaIssues: false,
            HasDocumentPropertiesPackageGraphIssues: false,
            HasCustomRibbonPackageGraphIssues: false,
            HasWorksheetSheetViewSchemaIssues: false,
            HasWorkbookViewSchemaIssues: false,
            HasWorkbookCalculationPropertySchemaIssues: false,
            HasWorkbookFileSharingSchemaIssues: false,
            HasWorkbookFileRecoveryPropertySchemaIssues: false,
            HasWorkbookProtectionSchemaIssues: false,
            HasWorkbookWebPublishingSchemaIssues: false,
            HasWorkbookSmartTagSchemaIssues: false,
            HasWorkbookNativeMetadataSchemaIssues: false,
            HasWorksheetRelationshipMarkerSchemaIssues: false,
            HasWorksheetNativeMetadataSchemaIssues: false,
            MergeCellWorksheetPathsToStrip: null,
            HasCalculationChainPackagePart: false);

    private static MemoryStream CreatePackageWithMalformedWorksheet()
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            // Deliberately missing the closing </worksheet> tag: not well-formed XML, so the
            // streaming canonical pre-scan gives up (catches its own exception, reports
            // "not canonical") and the subsequent authoritative XDocument.Load throws an
            // uncaught XmlException from inside the sanitizer's rewrite loop.
            writer.Write(
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1"><c r="A1"><v>1</v></c></row>
                  </sheetData>
                """);
        }

        package.Position = 0;
        return package;
    }
}
