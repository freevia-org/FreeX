using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class RecoverySnapshotLoaderTests
{
    [Fact]
    public void Load_UsesOriginalFileNameAndPathWhenAvailable()
    {
        using var directory = new TestTemporaryDirectory();
        var snapshotPath = WriteSnapshot(directory.Path, "Snapshot name");
        var originalPath = Path.Combine(directory.Path, "Quarterly report.xlsx");

        var result = RecoverySnapshotLoader.Load(snapshotPath, originalPath);

        result.DisplayName.Should().Be("Quarterly report.xlsx");
        result.Workbook.Name.Should().Be("Quarterly report.xlsx");
        result.SourcePath.Should().Be(originalPath);
        result.Status.Should().Be("Recovered from a previous session.");
        result.IsFallback.Should().BeFalse();
    }

    [Fact]
    public void Load_PreservesSnapshotWorkbookNameWithoutOriginalPath()
    {
        using var directory = new TestTemporaryDirectory();
        var snapshotPath = WriteSnapshot(directory.Path, "Unsaved workbook");

        var result = RecoverySnapshotLoader.Load(snapshotPath, originalFilePath: null);

        result.DisplayName.Should().Be("Unsaved workbook");
        result.Workbook.Name.Should().Be("Unsaved workbook");
        result.SourcePath.Should().BeNull();
    }

    private static string WriteSnapshot(string directory, string workbookName)
    {
        var path = Path.Combine(directory, "recovery.fxl");
        using var stream = File.Create(path);
        new NativeJsonAdapter().Save(new Workbook(workbookName), stream);
        return path;
    }
}
