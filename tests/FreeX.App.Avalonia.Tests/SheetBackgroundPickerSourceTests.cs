using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class SheetBackgroundPickerSourceTests
{
    [Fact]
    public void BackgroundPicker_DelegatesFormatPolicyToSharedPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs"));

        source.Should().Contain("SheetBackgroundPickerPlanner.BuildOpenPickerPlan()");
        source.Should().Contain("AvaloniaFilePickerService.PickSingleOpenFileAsync(");
        source.Should().Contain("AvaloniaFilePickerOpenRequest.FromDescriptors(");
        source.Should().Contain("SheetBackgroundPickerPlanner.IsSupportedImagePath(file.Name)");
        source.Should().Contain("SheetBackgroundPickerPlanner.TryBuildBackgroundImage(imageBytes, file.Name, out var background)");
        source.Should().NotContain("OpenFilePickerAsync(");
        source.Should().NotContain("FileTypeFilter = [PictureFileType]");
        source.Should().NotContain("InsertPictureCommandFactory.ContentTypeForPath(file.Name)");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
