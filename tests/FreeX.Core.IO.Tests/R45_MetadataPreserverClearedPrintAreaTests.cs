using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R45-io-defined-name-print-area-3-1: <c>XlsxWorkbookMetadataPreserver.MergeDefinedNames</c>'s
/// liveness gate must not unconditionally resurrect a stale <c>_xlnm.Print_Area</c>/
/// <c>_xlnm.Print_Titles</c> defined name from the pristine source workbook.xml snapshot after
/// the user has cleared the corresponding sheet setting (<c>Sheet.SetPrintAreas([])</c> /
/// <c>Sheet.PrintTitleRows</c>/<c>PrintTitleColumns</c> set to <c>null</c>).
///
/// Before the fix, the gate at MergeDefinedNames exempted EVERY Excel-reserved name
/// (<c>XlsxNamedRangeMapper.IsExcelReservedDefinedName</c>) from the liveness check
/// unconditionally, on the theory that reserved names are never loaded into the model at all (true
/// for _FilterDatabase/Criteria/Database/Extract/Consolidate_Area, which FreeX genuinely never
/// models) - but Print_Area and Print_Titles ARE modeled, as Sheet.PrintAreas and
/// Sheet.PrintTitleRows/PrintTitleColumns respectively, so exempting them the same way silently
/// reverted a user's "Clear Print Area"/"clear print titles" action on the very next full save
/// whenever the metadata preserver had to merge defined names against the ORIGINAL (pre-edit)
/// source bytes.
///
/// These tests call <c>XlsxWorkbookMetadataPreserver.Preserve</c> directly against hand-built
/// source/target xl/workbook.xml package fragments - the exact same shape XlsxFileAdapter.Save.cs
/// hands it during a real full-rebuild save - to isolate this specific merge step's behavior from
/// the rest of the save pipeline.
/// </summary>
public sealed class R45_MetadataPreserverClearedPrintAreaTests
{
    private const string WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void ClearedPrintArea_IsNotResurrectedFromPristineSourceSnapshot()
    {
        // Source snapshot: Sheet1 already has an on-disk _xlnm.Print_Area.
        var sourceWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
               <definedNames>
                 <definedName name="_xlnm.Print_Area" localSheetId="0">Sheet1!$A$1:$C$5</definedName>
               </definedNames>
             </workbook>
             """;

        // Target: the freshly-generated full-rebuild workbook.xml. ClosedXML never emits an
        // _xlnm.Print_Area name here because Sheet.PrintAreas.Count == 0 for the cleared sheet
        // (XlsxFileAdapter.Save.cs's `if (sheet.PrintAreas.Count > 0)` has no else branch).
        var targetWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
             </workbook>
             """;

        var workbook = new Workbook("Print Area Clear");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetPrintAreas([]); // The user just cleared the print area.
        sheet.PrintAreas.Should().BeEmpty();

        var resultWorkbookXml = RunPreserve(sourceWorkbookXml, targetWorkbookXml, workbook, [sheet.Id]);

        var definedNames = resultWorkbookXml.Root!.Element((XNamespace)WorkbookNs + "definedNames");
        var printAreaNames = definedNames?
            .Elements((XNamespace)WorkbookNs + "definedName")
            .Where(element => IsPrintAreaOrTitlesName(element.Attribute("name")?.Value, "Print_Area"))
            .ToList() ?? [];

        printAreaNames.Should().BeEmpty(
            "the cleared print area must not be resurrected from the pristine source workbook.xml " +
            "snapshot during the metadata preserver's defined-name merge");
    }

    [Fact]
    public void ClearedPrintTitles_AreNotResurrectedFromPristineSourceSnapshot()
    {
        var sourceWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
               <definedNames>
                 <definedName name="_xlnm.Print_Titles" localSheetId="0">Sheet1!$1:$2,Sheet1!$A:$A</definedName>
               </definedNames>
             </workbook>
             """;

        var targetWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
             </workbook>
             """;

        var workbook = new Workbook("Print Titles Clear");
        var sheet = workbook.AddSheet("Sheet1");
        // The user just cleared both repeat rows and repeat columns.
        sheet.PrintTitleRows = null;
        sheet.PrintTitleColumns = null;

        var resultWorkbookXml = RunPreserve(sourceWorkbookXml, targetWorkbookXml, workbook, [sheet.Id]);

        var definedNames = resultWorkbookXml.Root!.Element((XNamespace)WorkbookNs + "definedNames");
        var printTitlesNames = definedNames?
            .Elements((XNamespace)WorkbookNs + "definedName")
            .Where(element => IsPrintAreaOrTitlesName(element.Attribute("name")?.Value, "Print_Titles"))
            .ToList() ?? [];

        printTitlesNames.Should().BeEmpty(
            "cleared print titles must not be resurrected from the pristine source workbook.xml " +
            "snapshot during the metadata preserver's defined-name merge");
    }

    [Fact]
    public void LivePrintArea_IsStillResurrectedWhenMissingFromTarget()
    {
        // Sibling no-regression case: the fix must not turn Print_Area into something that is
        // NEVER preserved -- when the sheet's print area is still genuinely live (not cleared) but
        // is, for whatever reason, absent from the freshly-generated target workbook.xml, it must
        // still be carried over from the source snapshot exactly as before the fix.
        var sourceWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
               <definedNames>
                 <definedName name="_xlnm.Print_Area" localSheetId="0">Sheet1!$A$1:$C$5</definedName>
               </definedNames>
             </workbook>
             """;

        var targetWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
             </workbook>
             """;

        var workbook = new Workbook("Print Area Kept");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetPrintAreas([new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 3))]);

        var resultWorkbookXml = RunPreserve(sourceWorkbookXml, targetWorkbookXml, workbook, [sheet.Id]);

        var definedNames = resultWorkbookXml.Root!.Element((XNamespace)WorkbookNs + "definedNames");
        var printAreaNames = definedNames?
            .Elements((XNamespace)WorkbookNs + "definedName")
            .Where(element => IsPrintAreaOrTitlesName(element.Attribute("name")?.Value, "Print_Area"))
            .ToList() ?? [];

        printAreaNames.Should().ContainSingle(
            "a still-live print area missing from the target must still be resurrected from the " +
            "pristine source snapshot");
        printAreaNames[0].Value.Should().Be("Sheet1!$A$1:$C$5");
    }

    [Fact]
    public void LivePrintTitles_AreStillResurrectedWhenMissingFromTarget()
    {
        var sourceWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
               <definedNames>
                 <definedName name="_xlnm.Print_Titles" localSheetId="0">Sheet1!$1:$2</definedName>
               </definedNames>
             </workbook>
             """;

        var targetWorkbookXml =
            $"""
             <workbook xmlns="{WorkbookNs}">
               <sheets>
                 <sheet name="Sheet1" sheetId="1" r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>
               </sheets>
             </workbook>
             """;

        var workbook = new Workbook("Print Titles Kept");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);

        var resultWorkbookXml = RunPreserve(sourceWorkbookXml, targetWorkbookXml, workbook, [sheet.Id]);

        var definedNames = resultWorkbookXml.Root!.Element((XNamespace)WorkbookNs + "definedNames");
        var printTitlesNames = definedNames?
            .Elements((XNamespace)WorkbookNs + "definedName")
            .Where(element => IsPrintAreaOrTitlesName(element.Attribute("name")?.Value, "Print_Titles"))
            .ToList() ?? [];

        printTitlesNames.Should().ContainSingle(
            "still-live print titles missing from the target must still be resurrected from the " +
            "pristine source snapshot");
    }

    private static bool IsPrintAreaOrTitlesName(string? name, string suffix)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmed = name.Trim();
        var unprefixed = trimmed.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase)
            ? trimmed["_xlnm.".Length..]
            : trimmed;
        return string.Equals(unprefixed, suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument RunPreserve(
        string sourceWorkbookXml,
        string targetWorkbookXml,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        using var sourceStream = new MemoryStream();
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(sourceArchive, "xl/workbook.xml", sourceWorkbookXml);

        using var targetStream = new MemoryStream();
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(targetArchive, "xl/workbook.xml", targetWorkbookXml);

        sourceStream.Position = 0;
        targetStream.Position = 0;
        using (var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: true))
        using (var targetArchive = new ZipArchive(targetStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook, sourceSheetIdsByLocalId);
        }

        targetStream.Position = 0;
        using var resultArchive = new ZipArchive(targetStream, ZipArchiveMode.Read, leaveOpen: true);
        return XDocument.Load(resultArchive.GetEntry("xl/workbook.xml")!.Open());
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
