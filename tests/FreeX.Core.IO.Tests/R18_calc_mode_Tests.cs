using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R18-calc-chain-fullcalc-1: calcPr/@calcMode="autoNoTable" must round-trip to
/// <see cref="WorkbookCalculationMode.AutomaticExceptDataTables"/>, not be downgraded to
/// <see cref="WorkbookCalculationMode.Automatic"/>, in both <see cref="XlsxWorkbookMetadataReader"/>
/// entry points (the public stream overload and the internal XDocument overload used by the full
/// workbook metadata loader).
/// </summary>
public sealed class R18_calc_mode_Tests
{
    [Fact]
    public void LoadCalculationProperties_MapsAutoNoTable_ToAutomaticExceptDataTables()
    {
        using var package = CreateWorkbookPackageWithCalcMode("autoNoTable");

        var properties = XlsxWorkbookMetadataReader.LoadCalculationProperties(package);

        properties.Mode.Should().Be(WorkbookCalculationMode.AutomaticExceptDataTables);
    }

    [Fact]
    public void LoadWorkbookMetadata_MapsAutoNoTable_ToAutomaticExceptDataTables()
    {
        using var package = CreateWorkbookPackageWithCalcMode("autoNoTable");

        var snapshot = XlsxWorkbookMetadataReader.LoadWorkbookMetadata(package);

        snapshot.CalculationProperties.Mode.Should().Be(WorkbookCalculationMode.AutomaticExceptDataTables);
    }

    [Fact]
    public void AutomaticExceptDataTables_RoundTripsThroughAdapterSaveAndLoad_AsAutoNoTable()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Sheet1");
        workbook.CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables;

        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml");
            entry.Should().NotBeNull();
            using var entryStream = entry!.Open();
            var workbookXml = XDocument.Load(entryStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var calcPr = workbookXml.Root!.Element(ns + "calcPr");
            calcPr.Should().NotBeNull();
            calcPr!.Attribute("calcMode")!.Value.Should().Be("autoNoTable");
        }

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.CalculationMode.Should().Be(WorkbookCalculationMode.AutomaticExceptDataTables);
    }

    private static MemoryStream CreateWorkbookPackageWithCalcMode(string calcMode)
    {
        var workbook = new Workbook();
        workbook.AddSheet("Sheet1");
        workbook.CalculationMode = WorkbookCalculationMode.Automatic;

        var adapter = new XlsxFileAdapter();
        var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbookXml;
            using (var entryStream = entry.Open())
            {
                workbookXml = XDocument.Load(entryStream);
            }

            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var calcPr = workbookXml.Root!.Element(ns + "calcPr");
            calcPr.Should().NotBeNull();
            calcPr!.SetAttributeValue("calcMode", calcMode);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/workbook.xml");
            using var writeStream = newEntry.Open();
            workbookXml.Save(writeStream);
        }

        saved.Position = 0;
        return saved;
    }
}
