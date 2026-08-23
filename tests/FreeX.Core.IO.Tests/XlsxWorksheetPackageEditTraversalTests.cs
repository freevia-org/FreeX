using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPackageEditTraversalTests
{
    private static readonly XNamespace WorksheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Edit_UsesWorkbookOrderRelationshipPathsAndWorksheetNamespace()
    {
        var workbook = new Workbook("Traversal");
        workbook.AddSheet("Second");
        workbook.AddSheet("First");
        using var package = Save(workbook);
        var visited = new List<string>();

        XlsxWorksheetPackageEditTraversal.Edit(package, workbook, (session, sheet, edit) =>
        {
            visited.Add(sheet.Name);
            edit.Root.Name.Should().Be(WorksheetNs + "worksheet");
            edit.Root.SetAttributeValue("dedupOrder", visited.Count);
            session.MarkDirty(edit);
        });

        visited.Should().Equal("Second", "First");
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var pathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive)!;
        ReadRoot(archive, pathMap.SheetPathsByName["Second"])
            .Attribute("dedupOrder")!.Value.Should().Be("1");
        ReadRoot(archive, pathMap.SheetPathsByName["First"])
            .Attribute("dedupOrder")!.Value.Should().Be("2");
    }

    [Fact]
    public void Edit_MissingWorkbookRelationships_IsAnExactNoOp()
    {
        var workbook = new Workbook("MissingRelationships");
        workbook.AddSheet("Data");
        using var package = Save(workbook);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
            archive.GetEntry("xl/_rels/workbook.xml.rels")!.Delete();
        var before = package.ToArray();
        var callbackInvoked = false;

        XlsxWorksheetPackageEditTraversal.Edit(package, workbook, (_, _, _) => callbackInvoked = true);

        callbackInvoked.Should().BeFalse();
        package.ToArray().Should().Equal(before);
    }

    [Fact]
    public void Edit_CorruptWorkbookRelationships_PropagatesXmlFailureBeforeEditing()
    {
        var workbook = new Workbook("CorruptRelationships");
        workbook.AddSheet("Data");
        using var package = Save(workbook);
        ReplaceEntry(package, "xl/_rels/workbook.xml.rels", "<Relationships>");
        var callbackInvoked = false;

        var action = () => XlsxWorksheetPackageEditTraversal.Edit(
            package,
            workbook,
            (_, _, _) => callbackInvoked = true);

        action.Should().Throw<XmlException>();
        callbackInvoked.Should().BeFalse();
    }

    [Fact]
    public void Edit_UnmarkedWorksheets_LeavePackageBytesUnchanged()
    {
        var workbook = new Workbook("NoOp");
        workbook.AddSheet("Data");
        using var package = Save(workbook);
        var before = package.ToArray();

        XlsxWorksheetPackageEditTraversal.Edit(package, workbook, (_, _, edit) =>
            edit.Root.Name.Should().Be(WorksheetNs + "worksheet"));

        package.ToArray().Should().Equal(before);
    }

    [Fact]
    public void WorksheetMappers_AdoptSharedPackageTraversal()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var mapperFiles = new[]
        {
            "XlsxWorksheetCalculationPropertyMapper.cs",
            "XlsxWorksheetPhoneticPropertyMapper.cs",
            "XlsxWorksheetCustomPropertyMapper.cs",
            "XlsxWorksheetScenarioMapper.cs",
            "XlsxWorksheetAdditionalViewMapper.cs"
        };

        foreach (var mapperFile in mapperFiles)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", mapperFile));
            source.Should().Contain("XlsxWorksheetPackageEditTraversal.Edit")
                .And.NotContain("GetWorkbookSheetPaths(")
                .And.NotContain("new ZipArchive(packageStream, ZipArchiveMode.Update");
        }
    }

    [Fact]
    public void XlsxRoundTrip_PreservesAllMigratedWorksheetMetadata()
    {
        var workbook = new Workbook("Metadata");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.FullCalculationOnLoad = true;
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");
        sheet.CustomProperties.Add(new WorksheetCustomProperty("Mode", 7));
        sheet.AdditionalViews = new WorksheetAdditionalViewsModel
        {
            Views =
            [
                new WorksheetAdditionalViewModel
                {
                    WorkbookViewId = "1",
                    NativeAttributes = new Dictionary<string, string> { ["view"] = "pageLayout" }
                }
            ]
        };
        workbook.Scenarios.Add(new WorkbookScenario(
            "Expected",
            [new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(42))]));

        using var package = Save(workbook);
        package.Position = 0;
        var loaded = new XlsxFileAdapter().Load(package);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.FullCalculationOnLoad.Should().BeTrue();
        loadedSheet.PhoneticProperties.Should().BeEquivalentTo(sheet.PhoneticProperties);
        loadedSheet.CustomProperties.Should().ContainSingle(property => property.Name == "Mode" && property.Id == 7);
        loadedSheet.AdditionalViews!.Views.Should().ContainSingle(view => view.WorkbookViewId == "1");
        loaded.Scenarios.Should().ContainSingle(scenario => scenario.Name == "Expected");
    }

    private static MemoryStream Save(Workbook workbook)
    {
        var package = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, package);
        package.Position = 0;
        return package;
    }

    private static XElement ReadRoot(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return XDocument.Load(stream).Root!;
    }

    private static void ReplaceEntry(MemoryStream package, string path, string content)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
