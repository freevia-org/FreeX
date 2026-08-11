using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookOpenServiceTests
{
    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public async Task LoadAsync_RenamesSingleSheetTextWorkbooksToExcelCompatibleFileName(string extension)
    {
        using var temp = new TestTemporaryDirectory();
        var fileNameWithoutExtension = "Very Long Sales [Draft] Import Name 2026";
        var tempPath = Path.Combine(temp.Path, $"{fileNameWithoutExtension}{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");

        var adapter = new TestFileAdapter(_ =>
        {
            var workbook = new Workbook("Loaded");
            workbook.AddSheet("Sheet1");
            return workbook;
        });
        var loader = new WorkbookOpenService(_ => { });

        var result = await loader.LoadAsync(
            tempPath,
            adapter,
            extension,
            new FileFormatDescriptor(extension, "Text", CanOpen: true, CanSave: false),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        result.Workbook.Sheets.Single().Name.Should().Be("Very Long Sales _Draft_ Import");
    }
}
