using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R39-io-calcpr-workbook-2-1: calcPr/@fullPrecision must be governed by
/// <see cref="Workbook.FullPrecision"/> on every save, including a full-rebuild save that runs
/// <c>XlsxWorkbookMetadataPreserver.Preserve</c> (which merges every "unmodeled" source calcPr
/// attribute back onto the freshly-written target). Before the fix, fullPrecision was missing
/// from the preserver's modeled-attribute exclusion list, so a user's "Precision as displayed"
/// toggle was silently reverted to whatever the ORIGINAL source file had, on the very next
/// full-rebuild save (e.g. adding a sheet).
/// </summary>
public sealed class R39_CalcPrFullPrecisionTests
{
    [Fact]
    public void FullRebuildSave_TurningFullPrecisionBackOn_IsNotRevertedByStaleSourceCalcPr()
    {
        // Arrange: build a source file whose calcPr already has fullPrecision="0" (stale/original
        // state -- "Precision as displayed" was ON when the file was last saved).
        using var source = new MemoryStream();
        {
            var seedWorkbook = new Workbook();
            seedWorkbook.AddSheet("Sheet1");
            seedWorkbook.FullPrecision = false;

            var seedAdapter = new XlsxFileAdapter();
            seedAdapter.Save(seedWorkbook, source);
        }
        source.Position = 0;

        var loadAdapter = new XlsxFileAdapter();
        var workbook = loadAdapter.Load(source);
        workbook.FullPrecision.Should().BeFalse("the source file was saved with fullPrecision=\"0\"");

        // Act: the user turns "Precision as displayed" back OFF in Excel terms, i.e. FullPrecision
        // back to the (default) true, then makes a change that forces a full ClosedXML rebuild
        // save (adding a sheet is not eligible for the cell-only patch-save fast path).
        workbook.FullPrecision = true;
        workbook.AddSheet("Sheet2");

        using var resaved = new MemoryStream();
        loadAdapter.Save(workbook, resaved);
        resaved.Position = 0;

        // Assert: the saved calcPr must reflect the user's toggle (fullPrecision attribute absent,
        // since true is the default), not the stale source value of "0".
        var fullPrecisionAttribute = ReadCalcPrAttribute(resaved, "fullPrecision");
        fullPrecisionAttribute.Should().BeNull(
            "the user re-enabled full precision, and the stale source calcPr must not resurrect fullPrecision=\"0\"");

        resaved.Position = 0;
        var reloaded = loadAdapter.Load(resaved);
        reloaded.FullPrecision.Should().BeTrue();
    }

    [Fact]
    public void FullRebuildSave_TurningFullPrecisionOff_PersistsAndIsNotResurrectedToTrue()
    {
        // Sibling/no-regression case: the opposite toggle direction. Source has no fullPrecision
        // attribute at all (default true), user turns it OFF, and a full-rebuild save must persist
        // fullPrecision="0" rather than the merge silently dropping the model's explicit override.
        using var source = new MemoryStream();
        {
            var seedWorkbook = new Workbook();
            seedWorkbook.AddSheet("Sheet1");
            seedWorkbook.FullPrecision = true;

            var seedAdapter = new XlsxFileAdapter();
            seedAdapter.Save(seedWorkbook, source);
        }
        source.Position = 0;

        var loadAdapter = new XlsxFileAdapter();
        var workbook = loadAdapter.Load(source);
        workbook.FullPrecision.Should().BeTrue();

        workbook.FullPrecision = false;
        workbook.AddSheet("Sheet2");

        using var resaved = new MemoryStream();
        loadAdapter.Save(workbook, resaved);
        resaved.Position = 0;

        var fullPrecisionAttribute = ReadCalcPrAttribute(resaved, "fullPrecision");
        fullPrecisionAttribute.Should().Be("0");

        resaved.Position = 0;
        var reloaded = loadAdapter.Load(resaved);
        reloaded.FullPrecision.Should().BeFalse();
    }

    [Fact]
    public void FullRebuildSave_CalcModeStillHonorsModelOverStaleSourceCalcPr()
    {
        // No-regression check for the sibling modeled calcPr flags (calcMode/iterate/
        // iterateCount/iterateDelta): confirm adding fullPrecision to the exclusion list did not
        // disturb the already-correct handling of those attributes.
        using var source = new MemoryStream();
        {
            var seedWorkbook = new Workbook();
            seedWorkbook.AddSheet("Sheet1");
            seedWorkbook.CalculationMode = WorkbookCalculationMode.Manual;

            var seedAdapter = new XlsxFileAdapter();
            seedAdapter.Save(seedWorkbook, source);
        }
        source.Position = 0;

        var loadAdapter = new XlsxFileAdapter();
        var workbook = loadAdapter.Load(source);
        workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);

        workbook.CalculationMode = WorkbookCalculationMode.Automatic;
        workbook.AddSheet("Sheet2");

        using var resaved = new MemoryStream();
        loadAdapter.Save(workbook, resaved);
        resaved.Position = 0;

        var calcModeAttribute = ReadCalcPrAttribute(resaved, "calcMode");
        calcModeAttribute.Should().Be("auto");

        resaved.Position = 0;
        var reloaded = loadAdapter.Load(resaved);
        reloaded.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
    }

    private static string? ReadCalcPrAttribute(Stream xlsxStream, string attributeName)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/workbook.xml");
        entry.Should().NotBeNull();
        using var entryStream = entry!.Open();
        var workbookXml = XDocument.Load(entryStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var calcPr = workbookXml.Root!.Element(ns + "calcPr");
        return calcPr?.Attribute(attributeName)?.Value;
    }
}
