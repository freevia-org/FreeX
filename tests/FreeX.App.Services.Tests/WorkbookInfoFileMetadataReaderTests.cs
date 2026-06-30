using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookInfoFileMetadataReaderTests
{
    [Fact]
    public void Read_ExistingFile_ReturnsSizeAndModifiedTimes()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "budget.xlsx");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);

        var modified = new DateTime(2026, 6, 30, 12, 15, 0, DateTimeKind.Local);
        File.SetLastWriteTime(path, modified);

        var metadata = WorkbookInfoFileMetadataReader.Read(path);

        metadata.FileSizeBytes.Should().Be(5);
        metadata.LastModifiedLocal.Should().Be(File.GetLastWriteTime(path));
        metadata.LastModifiedUtc.Should().Be(File.GetLastWriteTimeUtc(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path.xlsx")]
    public void Read_BlankOrInvalidPath_ReturnsMissing(string? path)
    {
        WorkbookInfoFileMetadataReader.Read(path)
            .Should()
            .Be(WorkbookInfoFileMetadata.Missing);
    }

    [Fact]
    public void BuildPlan_CarriesFileMetadataAndDirtyStateIntoWorkbookInfoPlan()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "saved.xlsx");
        File.WriteAllBytes(path, new byte[1536]);
        var workbook = new Workbook("Saved");
        workbook.AddSheet("Sheet1");

        var plan = WorkbookInfoFileMetadataReader.BuildPlan(
            workbook,
            path,
            activeSheetIndex: 0,
            hasUnsavedChanges: true);

        plan.IsSaved.Should().BeTrue();
        plan.FileExistsOnDisk.Should().BeTrue();
        plan.FileSizeBytes.Should().Be(1536);
        plan.LastModifiedLocal.Should().Be(File.GetLastWriteTime(path));
        plan.HasUnsavedChanges.Should().BeTrue();
    }
}
