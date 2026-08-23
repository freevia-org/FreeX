using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDerivedFormatSaveWorkflowTests
{
    private static readonly XNamespace ContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    [Theory]
    [InlineData("xlsm", "application/vnd.ms-excel.sheet.macroEnabled.main+xml")]
    [InlineData("xltm", "application/vnd.ms-excel.template.macroEnabled.main+xml")]
    [InlineData("xltx", "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml")]
    public void DerivedFormatSaveWithWarnings_ReplacesDestinationAndRoundTripsPackage(
        string format,
        string expectedContentType)
    {
        var adapter = CreateAdapter(format);
        var warningAdapter = (IWarningCollectingFileAdapter)adapter;
        var workbook = new Workbook("DerivedFormat");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue(format));
        sheet.Comments[new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1)] = "cannot serialize";
        using var destination = new MemoryStream(Enumerable.Repeat((byte)0xCC, 1_000_000).ToArray());
        destination.Position = 0;

        var result = warningAdapter.SaveWithWarnings(workbook, destination);

        result.Warnings.Should().ContainSingle(warning =>
            warning.Contains("[comment]", StringComparison.OrdinalIgnoreCase));
        destination.Length.Should().BeLessThan(1_000_000);
        ReadWorkbookContentType(destination).Should().Be(expectedContentType);
        destination.Position = 0;
        var loadedSheet = adapter.Load(destination).GetSheetAt(0);
        loadedSheet.GetValue(1, 1).Should().Be(new TextValue(format));
    }

    [Fact]
    public void DerivedFormatAdapters_AdoptSharedSaveWorkflowWithRetainedVbaPolicies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        AssertAdapter(root, "XlsmFileAdapter.cs", "MacroEnabledMainContentType", preserveVbaProject: true);
        AssertAdapter(root, "XltmFileAdapter.cs", "MacroEnabledTemplateContentType", preserveVbaProject: true);
        AssertAdapter(root, "XltxFileAdapter.cs", "TemplateMainContentType", preserveVbaProject: false);
    }

    private static IFileAdapter CreateAdapter(string format) => format switch
    {
        "xlsm" => new XlsmFileAdapter(),
        "xltm" => new XltmFileAdapter(),
        "xltx" => new XltxFileAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    private static string? ReadWorkbookContentType(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var stream = archive.GetEntry("[Content_Types].xml")!.Open();
        var xml = XDocument.Load(stream);
        return xml.Root!
            .Elements(ContentTypeNs + "Override")
            .Single(element => string.Equals(
                element.Attribute("PartName")?.Value,
                "/xl/workbook.xml",
                StringComparison.OrdinalIgnoreCase))
            .Attribute("ContentType")?.Value;
    }

    private static void AssertAdapter(
        string root,
        string fileName,
        string contentTypeConstant,
        bool preserveVbaProject)
    {
        var source = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", fileName));
        source.Should().Contain("XlsxDerivedFormatSaveWorkflow.Save")
            .And.Contain(contentTypeConstant)
            .And.Contain($"preserveVbaProject: {preserveVbaProject.ToString().ToLowerInvariant()}")
            .And.Contain("collectWarnings: false")
            .And.Contain("collectWarnings: true")
            .And.NotContain("new MemoryStream()")
            .And.NotContain("new ZipArchive(")
            .And.NotContain("SaveStreamPreparer.TruncateFromCurrentPosition");
    }
}
