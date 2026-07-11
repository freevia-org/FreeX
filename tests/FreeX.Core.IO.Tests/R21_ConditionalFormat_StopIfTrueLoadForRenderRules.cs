using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R21-conditional-format-render-1: StopIfTrue was silently lost on load for ColorScale/DataBar/IconSet
/// conditional-format rules — only the long-tail (aboveAverage/top10/etc.) branch read the
/// <c>stopIfTrue</c> attribute. A file written by FreeX with StopIfTrue set on a data bar (or color
/// scale / icon set) rule lost that flag the moment it was reopened, corrupting CF layering
/// (a "stop if true" data bar would incorrectly let a lower-priority rule also paint the cell).
/// </summary>
public sealed class R21_ConditionalFormat_StopIfTrueLoadForRenderRules
{
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// Builds a minimal XLSX package with a single conditionalFormatting block whose cfRule carries
    /// <c>stopIfTrue="1"</c> and the given rule-type payload, injected directly into the worksheet XML
    /// (mirroring how a real Excel-written file, or FreeX's own writer, would look).
    /// </summary>
    private static MemoryStream BuildPackageWithStopIfTrueRule(string ruleType, XElement payload)
    {
        var wb = new Workbook("StopIfTrueBook");
        var sheet = wb.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry(WorksheetPath)!;
            XDocument doc;
            using (var xmlStream = entry.Open())
                doc = XDocument.Load(xmlStream);

            doc.Root!.Add(new XElement(
                Ns + "conditionalFormatting",
                new XAttribute("sqref", "A1:A5"),
                new XElement(
                    Ns + "cfRule",
                    new XAttribute("type", ruleType),
                    new XAttribute("priority", "1"),
                    new XAttribute("stopIfTrue", "1"),
                    payload)));

            entry.Delete();
            var replacement = archive.CreateEntry(WorksheetPath);
            using var writer = new System.IO.StreamWriter(replacement.Open());
            doc.Save(writer);
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void XlsxLoad_ColorScaleWithStopIfTrue_PreservesStopIfTrueFlag()
    {
        var payload = new XElement(
            Ns + "colorScale",
            new XElement(Ns + "cfvo", new XAttribute("type", "min")),
            new XElement(Ns + "cfvo", new XAttribute("type", "max")),
            new XElement(Ns + "color", new XAttribute("rgb", "FF63BE7B")),
            new XElement(Ns + "color", new XAttribute("rgb", "FFF8696B")));
        var stream = BuildPackageWithStopIfTrueRule("colorScale", payload);

        var loaded = new XlsxFileAdapter().Load(stream);
        var cf = loaded.GetSheetAt(0).ConditionalFormats.Single(r => r.RuleType == CfRuleType.ColorScale);

        cf.StopIfTrue.Should().BeTrue("the colorScale cfRule had stopIfTrue=\"1\" in the file");
    }

    [Fact]
    public void XlsxLoad_DataBarWithStopIfTrue_PreservesStopIfTrueFlag()
    {
        var payload = new XElement(
            Ns + "dataBar",
            new XElement(Ns + "cfvo", new XAttribute("type", "min")),
            new XElement(Ns + "cfvo", new XAttribute("type", "max")),
            new XElement(Ns + "color", new XAttribute("rgb", "FF638EC6")));
        var stream = BuildPackageWithStopIfTrueRule("dataBar", payload);

        var loaded = new XlsxFileAdapter().Load(stream);
        var cf = loaded.GetSheetAt(0).ConditionalFormats.Single(r => r.RuleType == CfRuleType.DataBar);

        cf.StopIfTrue.Should().BeTrue("the dataBar cfRule had stopIfTrue=\"1\" in the file");
    }

    [Fact]
    public void XlsxLoad_IconSetWithStopIfTrue_PreservesStopIfTrueFlag()
    {
        var payload = new XElement(
            Ns + "iconSet",
            new XAttribute("iconSet", "3TrafficLights1"),
            new XElement(Ns + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", "0")),
            new XElement(Ns + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", "33")),
            new XElement(Ns + "cfvo", new XAttribute("type", "percent"), new XAttribute("val", "67")));
        var stream = BuildPackageWithStopIfTrueRule("iconSet", payload);

        var loaded = new XlsxFileAdapter().Load(stream);
        var cf = loaded.GetSheetAt(0).ConditionalFormats.Single(r => r.RuleType == CfRuleType.IconSet);

        cf.StopIfTrue.Should().BeTrue("the iconSet cfRule had stopIfTrue=\"1\" in the file");
    }

    [Fact]
    public void XlsxRoundTrip_DataBarWithStopIfTrue_SurvivesLoadSaveReload()
    {
        // Arrange – load a file with a data bar rule that has stopIfTrue set.
        var payload = new XElement(
            Ns + "dataBar",
            new XElement(Ns + "cfvo", new XAttribute("type", "min")),
            new XElement(Ns + "cfvo", new XAttribute("type", "max")),
            new XElement(Ns + "color", new XAttribute("rgb", "FF638EC6")));
        var stream = BuildPackageWithStopIfTrueRule("dataBar", payload);
        var firstLoad = new XlsxFileAdapter().Load(stream);

        // Act – save it back out with FreeX's own writer, then reload.
        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(firstLoad, saved);
        saved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(saved);

        // Assert – a rule saved by FreeX with StopIfTrue must still have it set after reopening,
        // otherwise a "stop if true" data bar would silently start letting lower-priority rules
        // also paint the cell after every save/reopen cycle.
        var cf = reloaded.GetSheetAt(0).ConditionalFormats.Single(r => r.RuleType == CfRuleType.DataBar);
        cf.StopIfTrue.Should().BeTrue("StopIfTrue must survive a full FreeX load -> save -> reload round trip");
    }
}
