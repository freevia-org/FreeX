using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R66-io-row-col-props-6-1: <c>XlsxWorksheetMetadataPreserver</c>'s worksheet-level preflight
/// (<see cref="XlsxWorksheetMetadataPreserver.PlainPreflight"/>'s <c>ModeledColumnAttributes</c>)
/// used to list a column's <c>bestFit</c> and <c>style</c> attributes as "already modeled", even
/// though FreeX has no model field for either (<c>Sheet</c> only tracks
/// ColumnWidths/HiddenCols/ColOutlineLevels). A column whose ONLY native attribute was
/// <c>bestFit</c> or <c>style</c> was therefore misclassified as fully modeled, excluded from
/// <c>WorksheetsWithPreservableSourceMetadata</c>, and the attribute was silently dropped on the
/// next full-rebuild save.
///
/// These tests exercise the exact same <c>Preserve(sourceArchive, targetArchive, workbook)</c>
/// entry point and plain-worksheet-detection pattern as
/// <see cref="XlsxWorksheetMetadataPreserverTests"/>: the target worksheet XML is intentionally
/// invalid, so if the preflight (correctly) decides the source column metadata is preservable it
/// must attempt to load and merge the target worksheet XML and throw an <see cref="XmlException"/>;
/// if it (incorrectly) decides there is nothing to preserve, it skips loading the target entirely
/// and the call does not throw.
/// </summary>
public sealed class R66_ColumnBestFitStylePreservationTests
{
    [Fact]
    public void ColumnWithOnlyBestFitAttribute_IsTreatedAsPreservable()
    {
        // Column 2 carries only modeled width attributes plus bestFit - bestFit alone must make
        // this worksheet's column metadata preservable.
        using var sourcePackage = CreateWorkbookPackage(CreateWorksheetWithColumnXml(
            """<col min="2" max="2" width="12" customWidth="1" bestFit="1" />"""));
        using var targetPackage = CreateWorkbookPackage("<not-valid-xml");
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var workbook = new Workbook("BestFit column");
        workbook.AddSheet("Sheet1");

        var act = () => XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook);

        act.Should().Throw<XmlException>(
            "a column whose only native attribute is bestFit must be recognized as preservable, " +
            "requiring the (invalid) target worksheet XML to be loaded and merged");
    }

    [Fact]
    public void ColumnWithOnlyStyleAttribute_IsTreatedAsPreservable()
    {
        // Column 2 carries only min/max plus a style index - FreeX has no per-column style model
        // field, so this must also be recognized as preservable native-only metadata.
        using var sourcePackage = CreateWorkbookPackage(CreateWorksheetWithColumnXml(
            """<col min="2" max="2" style="1" />"""));
        using var targetPackage = CreateWorkbookPackage("<not-valid-xml");
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var workbook = new Workbook("Styled column");
        workbook.AddSheet("Sheet1");

        var act = () => XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook);

        act.Should().Throw<XmlException>(
            "a column whose only native attribute is a style index must be recognized as " +
            "preservable, requiring the (invalid) target worksheet XML to be loaded and merged");
    }

    [Fact]
    public void PlainModeledWidthColumn_RemainsNonPreservable()
    {
        // Sibling no-regression case: a column carrying ONLY genuinely-modeled attributes (min,
        // max, width, customWidth) must still be classified as fully modeled and skip loading the
        // target worksheet XML entirely, exactly as before the fix.
        using var sourcePackage = CreateWorkbookPackage(CreateWorksheetWithColumnXml(
            """<col min="2" max="2" width="12" customWidth="1" />"""));
        using var targetPackage = CreateWorkbookPackage("<not-valid-xml");
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var workbook = new Workbook("Plain width column");
        workbook.AddSheet("Sheet1");

        var act = () => XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook);

        act.Should().NotThrow(
            "a column with only genuinely-modelled width attributes carries no preservable native " +
            "metadata, so the target worksheet XML must not even be loaded");
    }

    [Fact]
    public void MergeWorksheetColumnAttributes_CopiesBestFitAndStyleOntoMatchingRebuiltColumn()
    {
        // Unit-level check on the merge step itself (mirrors XlsxWorksheetMetadataPreserverRowStyleTests):
        // once the preflight correctly flags the sheet as preservable, the actual attribute merge
        // must carry bestFit/style across for a column range that still exists in the rebuilt target.
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sourceColumns = new XElement(
            ns + "cols",
            new XElement(
                ns + "col",
                new XAttribute("min", "2"),
                new XAttribute("max", "2"),
                new XAttribute("width", "12"),
                new XAttribute("customWidth", "1"),
                new XAttribute("bestFit", "1"),
                new XAttribute("style", "1")));

        var targetRoot = new XElement(
            ns + "worksheet",
            new XElement(
                ns + "cols",
                new XElement(
                    ns + "col",
                    new XAttribute("min", "2"),
                    new XAttribute("max", "2"),
                    new XAttribute("width", "12"),
                    new XAttribute("customWidth", "1"))));

        var changed = XlsxWorksheetMetadataPreserver.MergeWorksheetColumnAttributes(sourceColumns, targetRoot, ns);

        changed.Should().BeTrue();
        var targetColumn = targetRoot.Element(ns + "cols")!.Element(ns + "col")!;
        targetColumn.Attribute("bestFit")?.Value.Should().Be("1");
        targetColumn.Attribute("style")?.Value.Should().Be("1");
    }

    private static string CreateWorksheetWithColumnXml(string colXml) => $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <cols>
            {colXml}
          </cols>
          <sheetData>
            <row r="1">
              <c r="A1"><v>1</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private static MemoryStream CreateWorkbookPackage(string worksheetXml)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                                Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        package.Position = 0;
        return package;
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
