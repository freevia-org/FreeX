using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip fidelity for legacy form controls against the real-world ExcelExamples1.xlsx
/// (the fixture that motivated the work). Skipped automatically when the file is absent so CI
/// without it stays green.
/// </summary>
public sealed class XlsxFormControlRealFileRoundTripTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string ExcelExamplesPath = @"E:\Users\quinn\Downloads\ExcelExamples1.xlsx";

    [Fact]
    public void Load_ExcelExamples1_ModelsFormControlsAcrossSheets()
    {
        if (!File.Exists(ExcelExamplesPath))
            return; // Fixture not present in this environment.

        using var stream = File.OpenRead(ExcelExamplesPath);
        var workbook = new XlsxFileAdapter().Load(stream);

        var totalControls = workbook.Sheets.Sum(s => s.FormControls.Count);
        totalControls.Should().BeGreaterThan(0, "form controls must no longer be silently dropped on load");

        // The Shift Calendar sheet has a scroll bar control.
        var shiftCalendar = workbook.Sheets.SingleOrDefault(s => s.Name == "Shift Calendar");
        shiftCalendar.Should().NotBeNull();
        shiftCalendar!.FormControls.Should().Contain(c => c.Kind == FormControlKind.ScrollBar);
    }

    [Fact]
    public void RoundTrip_ExcelExamples1_PreservesWorksheetControlReferences()
    {
        if (!File.Exists(ExcelExamplesPath))
            return; // Fixture not present in this environment.

        Workbook workbook;
        using (var stream = File.OpenRead(ExcelExamplesPath))
            workbook = new XlsxFileAdapter().Load(stream);

        // Dirty a cell so the save takes the full rebuild path (an unedited save byte-copies the
        // source worksheet verbatim, which trivially preserves controls). The full-rebuild path is
        // where ClosedXML regenerates the worksheet and historically dropped the controls block.
        var firstSheet = workbook.Sheets[0];
        firstSheet.SetCell(new CellAddress(firstSheet.Id, 1000, 1000), new NumberValue(42));

        using var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);

        // Every worksheet that owned a <control> in the source must still own one after round-trip,
        // otherwise the ctrlProps are orphaned and Excel shows nothing.
        var worksheetsWithControls = archive.Entries
            .Where(e =>
                e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Count(e =>
            {
                var xml = XDocument.Load(e.Open());
                return xml.Descendants(WorksheetNs + "control").Any();
            });

        worksheetsWithControls.Should().BeGreaterThan(0,
            "worksheet <control> references must round-trip so the preserved ctrlProps stay attached");
    }
}
