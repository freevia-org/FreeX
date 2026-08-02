using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

// R114: XlsxWorksheetColumnWidthWriter.ApplyExactColumnWidths expanded every existing <col> run into one
// clone per column number (to stamp exact widths/attributes per column) but never re-coalesced adjacent
// columns with identical resulting attributes back into a single min..max run before writing. Excel (and
// ClosedXML's own writer) always emits <cols> as compact, non-overlapping runs; a uniform-width
// multi-column "Column Width" ribbon action (SetColumnWidthCommand.Apply, which populates one
// Sheet.ColumnWidths entry per selected column) turned into one singleton <col min="c" max="c" .../>
// PER COLUMN instead of a single compact run, bloating worksheet XML for a common real-world action.
public sealed class R114_ColumnWidthWriterRunCompactionTests
{
    [Fact]
    public void SetColumnWidthCommand_UniformWidthAcrossManyColumns_WritesSingleCompactColRun()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        // Mirrors the ribbon's "Column Width" action over a multi-column header selection: one command
        // populates Sheet.ColumnWidths[col] for every column in the selected range with the same width.
        var cmd = new SetColumnWidthCommand(sheet.Id, 1, 50, 18.0);
        cmd.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var colElements = worksheetXml.Root!.Element(ns + "cols")!.Elements(ns + "col").ToList();

        // A uniform width over columns 1-50 must collapse into a single compact run, not 50 singletons.
        colElements.Should().ContainSingle("a uniform-width multi-column selection must be written as one compact min..max run, not one <col> per column");
        var run = colElements[0];
        run.Attribute("min")!.Value.Should().Be("1");
        run.Attribute("max")!.Value.Should().Be("50");
        run.Attribute("width")!.Value.Should().Be("18");
        run.Attribute("customWidth")!.Value.Should().Be("1");

        // The model-level round-trip must still be exact for every column in the run.
        var reloaded = new XlsxFileAdapter().Load(RewindCopy(saved));
        var reloadedSheet = reloaded.Sheets[0];
        for (uint c = 1; c <= 50; c++)
        {
            reloadedSheet.ColumnWidths.TryGetValue(c, out var width).Should().BeTrue();
            width.Should().BeApproximately(18.0, 1e-6);
        }
    }

    // Sibling no-regression case: adjacent columns whose widths genuinely DIFFER must NOT be merged into
    // one run (that would corrupt the narrower/wider columns' widths), while a run of columns that DO
    // share the same width elsewhere on the same sheet still compacts.
    [Fact]
    public void MixedColumnWidths_OnlyTrulyIdenticalAdjacentColumnsMerge()
    {
        var workbook = new Workbook("T");
        var sheet = workbook.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        // Columns 1-3 share one width (should merge into a single run); column 4 has a different width
        // (must stay its own entry); columns 5-6 share another width (should merge into their own run).
        sheet.ColumnWidths[1] = 12.0;
        sheet.ColumnWidths[2] = 12.0;
        sheet.ColumnWidths[3] = 12.0;
        sheet.ColumnWidths[4] = 30.0;
        sheet.ColumnWidths[5] = 9.0;
        sheet.ColumnWidths[6] = 9.0;

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var colElements = worksheetXml.Root!.Element(ns + "cols")!.Elements(ns + "col")
            .OrderBy(e => uint.Parse(e.Attribute("min")!.Value))
            .ToList();

        colElements.Should().HaveCount(3, "columns 1-3 merge, column 4 stands alone, and columns 5-6 merge -- three distinct runs total");

        colElements[0].Attribute("min")!.Value.Should().Be("1");
        colElements[0].Attribute("max")!.Value.Should().Be("3");
        colElements[0].Attribute("width")!.Value.Should().Be("12");

        colElements[1].Attribute("min")!.Value.Should().Be("4");
        colElements[1].Attribute("max")!.Value.Should().Be("4");
        colElements[1].Attribute("width")!.Value.Should().Be("30");

        colElements[2].Attribute("min")!.Value.Should().Be("5");
        colElements[2].Attribute("max")!.Value.Should().Be("6");
        colElements[2].Attribute("width")!.Value.Should().Be("9");

        // Model-level widths must still be exact and distinct per column.
        var reloaded = new XlsxFileAdapter().Load(RewindCopy(saved));
        var widths = reloaded.Sheets[0].ColumnWidths;
        widths[1].Should().BeApproximately(12.0, 1e-6);
        widths[2].Should().BeApproximately(12.0, 1e-6);
        widths[3].Should().BeApproximately(12.0, 1e-6);
        widths[4].Should().BeApproximately(30.0, 1e-6);
        widths[5].Should().BeApproximately(9.0, 1e-6);
        widths[6].Should().BeApproximately(9.0, 1e-6);
    }

    private static MemoryStream RewindCopy(MemoryStream source)
    {
        var copy = new MemoryStream(source.ToArray());
        copy.Position = 0;
        return copy;
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
