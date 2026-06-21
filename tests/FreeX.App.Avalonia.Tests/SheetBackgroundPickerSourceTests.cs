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
        source.Should().Contain("CreateFilePickerFileTypes(pickerPlan.FileTypes)");
        source.Should().Contain("SheetBackgroundPickerPlanner.IsSupportedImagePath(file.Name)");
        source.Should().Contain("SheetBackgroundPickerPlanner.TryBuildBackgroundImage(imageBytes, file.Name, out var background)");
        source.Should().NotContain("FileTypeFilter = [PictureFileType]");
        source.Should().NotContain("InsertPictureCommandFactory.ContentTypeForPath(file.Name)");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
