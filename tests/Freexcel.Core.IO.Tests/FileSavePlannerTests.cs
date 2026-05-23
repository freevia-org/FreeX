using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class FileSavePlannerTests
{
    [Fact]
    public void TryResolveExistingPath_UsesOnlySaveCapableFormats()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".xlsx", "Excel Workbook", CanOpen: true, CanSave: true),
            new FileFormatDescriptor(".xlsm", "Excel Macro-Enabled Workbook", CanOpen: true, CanSave: false)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("Book.xlsm", [adapter], out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
    }

    [Fact]
    public void TryResolveExistingPath_ResolvesSaveCapableAlias()
    {
        var adapter = new FakeAdapter([
            new FileFormatDescriptor(".fxjson", "Freexcel Workbook", CanOpen: true, CanSave: true)
        ]);

        var resolved = FileSavePlanner.TryResolveExistingPath("Book.FXJSON", [adapter], out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Adapter.Should().BeSameAs(adapter);
        target.Path.Should().Be("Book.FXJSON");
    }

    private sealed class FakeAdapter(IReadOnlyList<FileFormatDescriptor> formats) : IFileAdapter
    {
        public string Extension => formats[0].Extension;
        public string FormatName => formats[0].FormatName;
        public IReadOnlyList<FileFormatDescriptor> Formats => formats;
        public Workbook Load(Stream stream) => throw new NotSupportedException();
        public void Save(Workbook workbook, Stream stream) => throw new NotSupportedException();
    }
}
