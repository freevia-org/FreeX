using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class WorkbookOpenServiceTests
{
    [Fact]
    public async Task LoadAsync_ThrowsWhenFileExceedsConfiguredSizeLimit()
    {
        using var temp = new TestTemporaryDirectory();
        var tempPath = Path.Combine(temp.Path, "oversized.fxjson");
        await File.WriteAllTextAsync(tempPath, "payload-that-is-too-large");
        var adapterInvoked = false;
        var adapter = new TestFileAdapter(_ =>
        {
            adapterInvoked = true;
            return new Workbook("Loaded");
        });
        var loader = new WorkbookOpenService(_ => { }, maxFileBytes: 4);

        var act = async () => await loader.LoadAsync(
            tempPath,
            adapter,
            ".fxjson",
            new FileFormatDescriptor(".fxjson", "Fake"),
            new TestProgress<WorkbookOpenProgressUpdate>(_ => { }));

        await act.Should().ThrowAsync<WorkbookTooLargeException>();
        adapterInvoked.Should().BeFalse("the loader must reject oversized files before reading them");
    }
}
