using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class OpenWorkbookLoaderTests
{
    [Fact]
    public async Task LoadAsync_ThrowsWhenFileExceedsConfiguredSizeLimit()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload-that-is-too-large");
        try
        {
            var adapterInvoked = false;
            var adapter = new TestFileAdapter(_ =>
            {
                adapterInvoked = true;
                return new Workbook("Loaded");
            });
            var loader = new OpenWorkbookLoader(_ => { }, maxFileBytes: 4);

            var act = async () => await loader.LoadAsync(
                tempPath,
                adapter,
                ".fxjson",
                new FileFormatDescriptor(".fxjson", "Fake"),
                new TestProgress<OpenProgressUpdate>(_ => { }));

            await act.Should().ThrowAsync<WorkbookTooLargeException>();
            adapterInvoked.Should().BeFalse("the loader must reject oversized files before reading them");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
