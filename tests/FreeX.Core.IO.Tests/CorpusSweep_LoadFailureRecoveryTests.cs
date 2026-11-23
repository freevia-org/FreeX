using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Whole-corpus recalc-parity sweep findings (2,149 real workbooks): three load-failure clusters in which
/// workbooks Excel opens fine were rejected outright by FreeX.
///  1. A single 130-200 MB worksheet part tripped the 64M-character XML cap in the STREAMING style-only-cell
///     stripper and surfaced as "not a valid .xlsx package".
///  2. Excel form-button captions in legacy VML contain an unclosed &lt;br&gt;; ClosedXML's strict comment loader
///     threw "Could not load comments file".
///  3. A defined name Excel wrote with an UNQUOTED sheet prefix embedding an external ordinal
///     ("VIPP [16]NA!$G$15:$DG$1042") crashed ClosedXML's formula parser.
/// </summary>
public sealed class CorpusSweep_LoadFailureRecoveryTests
{
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void StyleOnlyCellStripper_StreamsWorksheetLargerThanDomXmlCap()
    {
        // 64M chars is the DOM cap; stream ~72M chars of style-only cells through the strip pass.
        const string header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>";
        const string footer = "</sheetData></worksheet>";
        using var input = new MemoryStream();
        using (var writer = new StreamWriter(input, new UTF8Encoding(false), 1 << 16, leaveOpen: true))
        {
            writer.Write(header);
            const string row = "<row r=\"1\"><c r=\"A1\" s=\"1\"/><c r=\"B1\" s=\"1\"/><c r=\"C1\" s=\"1\"/><c r=\"D1\" s=\"1\"/><c r=\"E1\" s=\"1\"/><c r=\"F1\" s=\"1\"/><c r=\"G1\" s=\"1\"/><c r=\"H1\" s=\"1\"/></row>";
            for (long written = 0; written < 72L * 1024 * 1024; written += row.Length)
                writer.Write(row);
            writer.Write(footer);
        }

        input.Position = 0;
        using var output = new MemoryStream();

        var act = () => XlsxClosedXmlStyleOnlyCellStripper.StripRedundantStyleOnlyCells(input, output);

        act.Should().NotThrow("a legitimately large worksheet part must stream, not trip the DOM-load character cap");
        output.Length.Should().BeLessThan(input.Length, "the redundant duplicate style-only cells are stripped");
    }

    [Fact]
    public void Load_WorkbookWithVmlButtonCaptionContainingUnclosedBr_DoesNotThrowAndKeepsNote()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.Comments[new CellAddress(sheet.Id, 2, 2)] = "Keep this note";

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AppendToVml(source, "<br>");

        var adapter = new XlsxFileAdapter();
        source.Position = 0;

        Workbook? loaded = null;
        var act = () => loaded = adapter.Load(source);
        act.Should().NotThrow("Excel opens VML with an unclosed <br>; FreeX must not reject the whole workbook");

        loaded!.Sheets.Single().Comments.Values.Should().Contain("Keep this note");
    }

    [Theory]
    [InlineData("VIPP [16]NA!$G$15:$DG$1042")]
    [InlineData("Some Book [3]Data!$A$1")]
    public void Load_WorkbookWithUnquotedExternalOrdinalSheetPrefixDefinedName_DoesNotThrow(string refersTo)
    {
        var workbook = new Workbook("Test");
        workbook.AddSheet("Sheet1");

        using var source = XlsxPackageTestHelper.SaveWorkbook(workbook);
        AddDefinedName(source, "DF_GRID_1", refersTo);

        var adapter = new XlsxFileAdapter();
        source.Position = 0;

        Workbook? loaded = null;
        var act = () => loaded = adapter.Load(source);
        act.Should().NotThrow("a defined name FreeX cannot model must be skipped, not abort the load");

        loaded!.Sheets.Select(s => s.Name).Should().BeEquivalentTo(["Sheet1"]);
        loaded.NamedRanges.Should().NotContainKey("DF_GRID_1");
    }

    private static void AppendToVml(MemoryStream package, string fragment)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.Entries.First(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase));
            var name = entry.FullName;
            string text;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                text = reader.ReadToEnd();

            var close = text.LastIndexOf("</xml>", StringComparison.Ordinal);
            close.Should().BeGreaterThan(0);
            text = text.Insert(close, fragment);

            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }

        package.Position = 0;
    }

    private static void AddDefinedName(MemoryStream package, string name, string refersTo)
    {
        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
                workbookXml = XDocument.Load(entryStream);

            var root = workbookXml.Root!;
            var definedNames = root.Element(WorkbookNs + "definedNames");
            if (definedNames is null)
            {
                definedNames = new XElement(WorkbookNs + "definedNames");
                root.Element(WorkbookNs + "sheets")!.AddAfterSelf(definedNames);
            }

            definedNames.Add(new XElement(WorkbookNs + "definedName", new XAttribute("name", name), refersTo));

            entry.Delete();
            using var replacementStream = archive.CreateEntry("xl/workbook.xml").Open();
            workbookXml.Save(replacementStream, SaveOptions.DisableFormatting);
        }

        package.Position = 0;
    }
}
