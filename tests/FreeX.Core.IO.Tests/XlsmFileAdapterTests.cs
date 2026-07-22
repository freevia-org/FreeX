using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests for the XLSM / XLTM macro-enabled save adapters. They write through the standard XLSX
/// writer and flip only the workbook content-type to the macro-enabled (or macro-enabled template)
/// type, so the tests assert (a) the content-type flip, (b) that the package is still a readable
/// workbook whose values round-trip, and (c) that an xl/vbaProject.bin carried by the source
/// package survives a round-trip save.
///
/// R70-io-vba-6-1: also covers the counterpart behavior -- Saving a macro-enabled source AS a
/// plain, non-macro .xlsx via <see cref="XlsxFileAdapter"/> directly (i.e. NOT through
/// <see cref="XlsmFileAdapter"/>/<see cref="XltmFileAdapter"/>) must DROP the VBA project and its
/// content-type entirely, matching Excel's own Save-As-plain-format behavior.
/// </summary>
public sealed class XlsmFileAdapterTests
{
    private const string MacroEnabledMainContentType =
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string MacroEnabledTemplateContentType =
        "application/vnd.ms-excel.template.macroEnabled.main+xml";
    private const string WorksheetMainContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string VbaProjectPath = "xl/vbaProject.bin";
    private const string VbaProjectContentType = "application/vnd.ms-office.vbaProject";

    private static readonly XNamespace ContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string VbaProjectRelationshipType =
        "http://schemas.microsoft.com/office/2006/relationships/vbaProject";

    private static Workbook BuildSample()
    {
        var wb = new Workbook("Untitled");
        var sheet = wb.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Header"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(123.5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("A2*2"));
        return wb;
    }

    private static string? ReadWorkbookContentType(byte[] package)
    {
        using var ms = new MemoryStream(package);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("[Content_Types].xml")!;
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        return xml.Root!
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(e => string.Equals(
                e.Attribute("PartName")?.Value,
                "/xl/workbook.xml",
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")?.Value;
    }

    // ── XLSM ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Xlsm_Save_FlipsWorkbookContentTypeToMacroEnabled()
    {
        using var stream = new MemoryStream();
        new XlsmFileAdapter().Save(BuildSample(), stream);

        var contentType = ReadWorkbookContentType(stream.ToArray());
        contentType.Should().Be(MacroEnabledMainContentType);
        contentType.Should().NotBe(WorksheetMainContentType);
    }

    [Fact]
    public void Xlsm_Save_ThenLoad_RoundTripsValuesAndFormula()
    {
        var adapter = new XlsmFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(BuildSample(), stream);
        stream.Position = 0;

        var sheet = adapter.Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Header"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(123.5));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("A2*2");
    }

    [Fact]
    public void Xlsm_Save_ProducesPackageReadableByXlsxAdapter()
    {
        using var stream = new MemoryStream();
        new XlsmFileAdapter().Save(BuildSample(), stream);
        stream.Position = 0;

        // The xlsm package is structurally a workbook; the xlsx loader must open it without error.
        var sheet = new XlsxFileAdapter().Load(stream).Sheets.Single();
        sheet.Name.Should().Be("Data");
    }

    [Fact]
    public void Xlsm_Formats_AreCanOpenAndCanSave()
    {
        var adapter = new XlsmFileAdapter();

        adapter.Formats.Should().ContainSingle(f =>
            f.Extension == ".xlsm" &&
            f.CanOpen &&
            f.CanSave &&
            !f.OpensAsTemplate);
    }

    [Fact]
    public void Xlsm_Save_NonMacroWorkbook_WritesExpectedContentType()
    {
        // Saving a plain (non-macro) workbook as .xlsm is valid: the package gets the
        // macroEnabled content-type even though there is no vbaProject.bin present.
        var wb = new Workbook("Plain");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        using var stream = new MemoryStream();
        new XlsmFileAdapter().Save(wb, stream);

        ReadWorkbookContentType(stream.ToArray())
            .Should().Be(MacroEnabledMainContentType);

        // No vbaProject.bin expected — the package must still load cleanly.
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        archive.GetEntry(VbaProjectPath).Should().BeNull();

        stream.Position = 0;
        var loaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0);
        loaded.GetValue(1, 1).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Xlsm_RoundTrip_VbaProjectBinSurvivesSave()
    {
        // Build a package that has xl/vbaProject.bin (simulating a real .xlsm open).
        using var source = BuildMacroEnabledPackage(macroEnabled: true);

        // Load via XlsxFileAdapter (the common open path for .xlsm).
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.HasVbaProjectPackage.Should().BeTrue();

        // Make a minor edit so a full save is triggered.
        var editSheet = workbook.GetSheetAt(0);
        editSheet.SetCell(new CellAddress(editSheet.Id, 3, 1), new NumberValue(99));

        // Save as .xlsm via the new adapter.
        using var saved = new MemoryStream();
        new XlsmFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        // vbaProject.bin must be present.
        savedArchive.GetEntry(VbaProjectPath).Should().NotBeNull(
            "xl/vbaProject.bin must survive a round-trip save via XlsmFileAdapter");

        // The workbook content-type must be macroEnabled.
        var contentTypesXml = LoadZipEntryXml(savedArchive, "[Content_Types].xml");
        GetContentTypeOverride(contentTypesXml, "xl/workbook.xml")
            .Should().Be(MacroEnabledMainContentType);

        // The vbaProject part must have its content-type declared.
        ContentTypeOverrideExists(contentTypesXml, VbaProjectPath, VbaProjectContentType)
            .Should().BeTrue("the vbaProject.bin content-type entry must survive the save");

        // The workbook's relationship to vbaProject.bin must still exist.
        var workbookRelsXml = LoadZipEntryXml(savedArchive, "xl/_rels/workbook.xml.rels");
        var hasVbaRelationship = workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Any(e =>
                string.Equals(e.Attribute("Type")?.Value, VbaProjectRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Attribute("Target")?.Value, "vbaProject.bin", StringComparison.OrdinalIgnoreCase));
        hasVbaRelationship.Should().BeTrue("the workbook->vbaProject.bin relationship must survive the save");
    }

    [Fact]
    public void Xlsm_SaveAs_UnchangedModel_StillPreservesVbaProjectViaFastPath()
    {
        // No-regression guard: an UNEDITED macro-enabled workbook Saved-As .xlsm takes the "model
        // unchanged" fast source-copy path (see Xlsx_SaveAs_FromMacroEnabledSource_
        // DropsVbaProjectAndContentType below, which exercises the same fast path for the DROP
        // case) rather than a full rebuild. That fast path must still preserve xl/vbaProject.bin
        // when the target format actually is macro-enabled -- only a plain (non-macro) target may
        // ever cause it to be bypassed/dropped.
        using var source = BuildMacroEnabledPackage(macroEnabled: true);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        using var saved = new MemoryStream();
        new XlsmFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        savedArchive.GetEntry(VbaProjectPath).Should().NotBeNull(
            "an unedited macro-enabled workbook Saved-As .xlsm must still keep xl/vbaProject.bin");

        var contentTypesXml = LoadZipEntryXml(savedArchive, "[Content_Types].xml");
        GetContentTypeOverride(contentTypesXml, "xl/workbook.xml")
            .Should().Be(MacroEnabledMainContentType);
    }

    // ── XLSX (plain target — VBA must be DROPPED) ──────────────────────────────────────────────────
    // R70-io-vba-6-1: Excel drops a workbook's VBA project (with a warning) when a macro-enabled
    // workbook is Saved As a plain, non-macro format. Saving via XlsxFileAdapter directly (i.e. NOT
    // through XlsmFileAdapter/XltmFileAdapter) is exactly that Save-As-plain-.xlsx case.

    [Fact]
    public void Xlsx_SaveAs_FromMacroEnabledSource_DropsVbaProjectAndContentType()
    {
        // Build a package that has xl/vbaProject.bin (simulating a real .xlsm open), then
        // Save-As a PLAIN .xlsx with NO edit first -- this exercises the "model unchanged" fast
        // source-copy path, which (before the fix) replayed the source bytes, vbaProject.bin
        // included, verbatim regardless of the plain-.xlsx target.
        using var source = BuildMacroEnabledPackage(macroEnabled: true);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        workbook.HasVbaProjectPackage.Should().BeTrue();

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        savedArchive.GetEntry(VbaProjectPath).Should().BeNull(
            "Save-As a plain .xlsx must drop the source's xl/vbaProject.bin, matching Excel");

        var contentTypesXml = LoadZipEntryXml(savedArchive, "[Content_Types].xml");
        GetEffectiveWorkbookContentType(contentTypesXml, "xl/workbook.xml")
            .Should().Be(WorksheetMainContentType);

        ContentTypeOverrideExists(contentTypesXml, VbaProjectPath, VbaProjectContentType)
            .Should().BeFalse("the vbaProject.bin content-type override must not survive a plain .xlsx save");

        var workbookRelsXml = LoadZipEntryXml(savedArchive, "xl/_rels/workbook.xml.rels");
        workbookRelsXml.Root!
            .Elements(PackageRelNs + "Relationship")
            .Any(e => string.Equals(e.Attribute("Type")?.Value, VbaProjectRelationshipType, StringComparison.OrdinalIgnoreCase))
            .Should().BeFalse("the workbook->vbaProject.bin relationship must not survive a plain .xlsx save");

        // The package must still be a perfectly ordinary, readable workbook.
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved).GetSheetAt(0);
        reloaded.GetValue(1, 1).Should().Be(new TextValue("value"));
    }

    [Fact]
    public void Xlsx_SaveAs_FromMacroEnabledSource_AfterEdit_StillDropsVbaProject()
    {
        // Same as above but with a cell edit first, forcing the full ClosedXML-rebuild +
        // source-package-preservation path (rather than the "model unchanged" fast path) -- both
        // paths must drop the VBA project on a plain .xlsx target.
        using var source = BuildMacroEnabledPackage(macroEnabled: true);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var editSheet = workbook.GetSheetAt(0);
        editSheet.SetCell(new CellAddress(editSheet.Id, 3, 1), new NumberValue(99));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        savedArchive.GetEntry(VbaProjectPath).Should().BeNull(
            "an edited workbook Saved-As a plain .xlsx must still drop xl/vbaProject.bin");

        var contentTypesXml = LoadZipEntryXml(savedArchive, "[Content_Types].xml");
        GetEffectiveWorkbookContentType(contentTypesXml, "xl/workbook.xml")
            .Should().Be(WorksheetMainContentType);
    }

    // ── XLTM ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Xltm_Save_FlipsWorkbookContentTypeToMacroEnabledTemplate()
    {
        using var stream = new MemoryStream();
        new XltmFileAdapter().Save(BuildSample(), stream);

        var contentType = ReadWorkbookContentType(stream.ToArray());
        contentType.Should().Be(MacroEnabledTemplateContentType);
        contentType.Should().NotBe(WorksheetMainContentType);
    }

    [Fact]
    public void Xltm_Save_ThenLoad_RoundTripsValuesAndFormula()
    {
        var adapter = new XltmFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(BuildSample(), stream);
        stream.Position = 0;

        var sheet = adapter.Load(stream).Sheets.Single();
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Header"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(123.5));
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be("A2*2");
    }

    [Fact]
    public void Xltm_Save_ProducesPackageReadableByXlsxAdapter()
    {
        using var stream = new MemoryStream();
        new XltmFileAdapter().Save(BuildSample(), stream);
        stream.Position = 0;

        var sheet = new XlsxFileAdapter().Load(stream).Sheets.Single();
        sheet.Name.Should().Be("Data");
    }

    [Fact]
    public void Xltm_Formats_AreCanOpenAndCanSaveAndOpensAsTemplate()
    {
        var adapter = new XltmFileAdapter();

        adapter.Formats.Should().ContainSingle(f =>
            f.Extension == ".xltm" &&
            f.CanOpen &&
            f.CanSave &&
            f.OpensAsTemplate);
    }

    [Fact]
    public void Xltm_RoundTrip_VbaProjectBinSurvivesSave()
    {
        using var source = BuildMacroEnabledPackage(macroEnabled: true);

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);

        var editSheet = workbook.GetSheetAt(0);
        editSheet.SetCell(new CellAddress(editSheet.Id, 3, 1), new NumberValue(77));

        using var saved = new MemoryStream();
        new XltmFileAdapter().Save(workbook, saved);

        saved.Position = 0;
        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        savedArchive.GetEntry(VbaProjectPath).Should().NotBeNull(
            "xl/vbaProject.bin must survive a round-trip save via XltmFileAdapter");

        var contentTypesXml = LoadZipEntryXml(savedArchive, "[Content_Types].xml");
        GetContentTypeOverride(contentTypesXml, "xl/workbook.xml")
            .Should().Be(MacroEnabledTemplateContentType);

        ContentTypeOverrideExists(contentTypesXml, VbaProjectPath, VbaProjectContentType)
            .Should().BeTrue("the vbaProject.bin content-type entry must survive the save");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal in-memory XLSX-based package that optionally includes a stub
    /// <c>xl/vbaProject.bin</c> and the appropriate content-type / relationship entries that
    /// a real .xlsm file would carry.
    /// </summary>
    private static MemoryStream BuildMacroEnabledPackage(bool macroEnabled)
    {
        var workbook = new Workbook("MacroWorkbook");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));

        var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;

        if (!macroEnabled)
            return package;

        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            // Write a stub vbaProject.bin (minimal OLE compound-document header magic bytes).
            archive.GetEntry(VbaProjectPath)?.Delete();
            var vbaEntry = archive.CreateEntry(VbaProjectPath, CompressionLevel.Optimal);
            using (var s = vbaEntry.Open())
            {
                // Minimal stub — not a valid VBA project but sufficient for structural testing.
                s.Write([0xD0, 0xCF, 0x11, 0xE0, 0x56, 0x42, 0x41]);
            }

            // Flip workbook content-type to macroEnabled.
            var contentTypesEntry = archive.GetEntry("[Content_Types].xml")!;
            var ctXml = LoadZipEntryXml(contentTypesEntry);
            var root = ctXml.Root!;

            // Replace workbook override with macroEnabled type. A plain ClosedXML-authored .xlsx
            // has NO explicit Override for xl/workbook.xml -- it relies on the package-wide
            // Default Extension="xml" entry (which already equals the plain worksheet type) --
            // so there is usually nothing to flip in place; ADD the override when it is missing
            // rather than silently no-op, matching what a genuine .xlsm always carries (Excel must
            // emit an explicit Override there since the macroEnabled type differs from the Default).
            var existing = root.Elements(ContentTypeNs + "Override")
                .FirstOrDefault(e => string.Equals(
                    e.Attribute("PartName")?.Value?.TrimStart('/'),
                    "xl/workbook.xml",
                    StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.SetAttributeValue("ContentType", MacroEnabledMainContentType);
            }
            else
            {
                root.Add(new XElement(
                    ContentTypeNs + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", MacroEnabledMainContentType)));
            }

            // Add vbaProject content-type override if missing.
            if (!ContentTypeOverrideExists(ctXml, VbaProjectPath, VbaProjectContentType))
            {
                root.Add(new XElement(
                    ContentTypeNs + "Override",
                    new XAttribute("PartName", "/xl/vbaProject.bin"),
                    new XAttribute("ContentType", VbaProjectContentType)));
            }

            ReplaceZipEntry(archive, "[Content_Types].xml", ctXml);

            // Add vbaProject relationship in workbook.xml.rels.
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")!;
            var relsXml = LoadZipEntryXml(relsEntry);
            var hasVbaRel = relsXml.Root!
                .Elements(PackageRelNs + "Relationship")
                .Any(e => string.Equals(
                    e.Attribute("Type")?.Value,
                    VbaProjectRelationshipType,
                    StringComparison.OrdinalIgnoreCase));
            if (!hasVbaRel)
            {
                relsXml.Root.Add(new XElement(
                    PackageRelNs + "Relationship",
                    new XAttribute("Id", "rIdVbaProject"),
                    new XAttribute("Type", VbaProjectRelationshipType),
                    new XAttribute("Target", "vbaProject.bin")));
                ReplaceZipEntry(archive, "xl/_rels/workbook.xml.rels", relsXml);
            }
        }

        package.Position = 0;
        return package;
    }

    private static XDocument LoadZipEntryXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)!;
        return LoadZipEntryXml(entry);
    }

    private static XDocument LoadZipEntryXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplaceZipEntry(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        xml.Save(writer, SaveOptions.DisableFormatting);
    }

    private static string? GetContentTypeOverride(XDocument contentTypesXml, string partName)
    {
        var normalizedPartName = "/" + partName.TrimStart('/');
        return contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(e => string.Equals(
                e.Attribute("PartName")?.Value,
                normalizedPartName,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")?.Value;
    }

    private static bool ContentTypeOverrideExists(XDocument contentTypesXml, string partName, string contentType)
    {
        var normalizedPartName = "/" + partName.TrimStart('/');
        return contentTypesXml.Root!
            .Elements(ContentTypeNs + "Override")
            .Any(e =>
                string.Equals(e.Attribute("PartName")?.Value, normalizedPartName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the content-type that actually governs <paramref name="partName"/>: an explicit
    /// Override for that part if one exists, otherwise the package-wide Default for the part's
    /// file extension. A plain ClosedXML-authored .xlsx normally has NO explicit Override for
    /// xl/workbook.xml -- it relies on the Default Extension="xml" entry, which already carries
    /// the plain worksheet content-type -- so <see cref="GetContentTypeOverride"/> alone (which
    /// only looks at Override entries) cannot tell "correctly plain via Default" apart from
    /// "no content-type at all"; this resolves the same effective value Excel/OPC readers do.
    /// </summary>
    private static string? GetEffectiveWorkbookContentType(XDocument contentTypesXml, string partName)
    {
        var overrideValue = GetContentTypeOverride(contentTypesXml, partName);
        if (overrideValue is not null)
            return overrideValue;

        var extension = partName.TrimStart('/').Split('.').LastOrDefault();
        if (extension is null)
            return null;

        return contentTypesXml.Root!
            .Elements(ContentTypeNs + "Default")
            .FirstOrDefault(e => string.Equals(e.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")?.Value;
    }
}
