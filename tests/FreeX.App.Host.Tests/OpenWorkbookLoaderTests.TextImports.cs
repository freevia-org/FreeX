using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class OpenWorkbookLoaderTests
{
    [Theory]
    [InlineData(".csv")]
    [InlineData(".txt")]
    [InlineData(".tsv")]
    [InlineData(".tab")]
    public async Task LoadAsync_RenamesSingleSheetTextWorkbooksToExcelCompatibleFileName(string extension)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var fileNameWithoutExtension = "Very Long Sales [Draft] Import Name 2026";
        var tempPath = Path.Combine(tempDirectory, $"{fileNameWithoutExtension}{extension}");
        await File.WriteAllTextAsync(tempPath, "payload");
        try
        {
            var adapter = new FakeAdapter(_ =>
            {
                var workbook = new Workbook("Loaded");
                workbook.AddSheet("Sheet1");
                return workbook;
            });
            var loader = new OpenWorkbookLoader(_ => { });

            var result = await loader.LoadAsync(
                tempPath,
                adapter,
                extension,
                new FileFormatDescriptor(extension, "Text", CanOpen: true, CanSave: false),
                new ImmediateProgress<OpenProgressUpdate>(_ => { }));

            result.Workbook.Sheets.Single().Name.Should().Be("Very Long Sales _Draft_ Import");
        }
        finally
        {
            File.Delete(tempPath);
            Directory.Delete(tempDirectory);
        }
    }
}
