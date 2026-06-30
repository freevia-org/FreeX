using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class FilePickerPlannerSourceTests
{
    [Fact]
    public void MainWindow_RoutesPresentationPickerPolicyThroughSharedPlanner()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var project = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Avalonia",
            "FreeP.App.Avalonia.csproj"));

        source.Should().Contain("PresentationFileDialogPlanner.BuildOpenPickerPlan()");
        source.Should().Contain("PresentationFileDialogPlanner.BuildSavePickerPlan(");
        source.Should().Contain("PresentationFileDialogPlanner.IsLegacyPresentationPath(path)");
        source.Should().Contain("AvaloniaFilePickerTypeAdapter.ToFileTypes(plan.FileTypes)");
        source.Should().Contain("SisterAppFileTextPlanner.Presentation");
        source.Should().Contain("PresentationFileTextResources.PictureFileTypeName");
        source.Should().Contain("FileText.OpenPickerTitle");
        source.Should().Contain("FileText.SavePickerTitle");
        source.Should().Contain("SisterAppFileTextPlanner.FormatSelectedFileNotLocalPath(");
        source.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        source.Should().NotContain("new FilePickerFileType(descriptor.DisplayName)");
        source.Should().NotContain("Patterns = descriptor.Patterns.ToArray()");
        source.Should().Contain("IsSupportedPresentationPath(a)");
        source.Should().NotContain("PptxFileType");
        source.Should().NotContain("new FilePickerFileType(\"Images\")");
        source.Should().NotContain("Title         = \"Open Presentation\"");
        source.Should().NotContain("Title             = \"Save Presentation\"");
        source.Should().NotContain("_statusText.Text = $\"Open failed:");
        source.Should().NotContain("_statusText.Text = $\"Save failed:");
        source.Should().NotContain("new FilePickerFileType(\"PowerPoint Presentation\")");
        source.Should().NotContain("DefaultExtension  = \"pptx\"");
        source.Should().NotContain("SuggestedFileName = suggested");
        project.Should().Contain(@"..\..\shared\Free.Shared.IO\Free.Shared.IO.csproj");
        project.Should().Contain(@"..\..\shared\Free.Shared.Shell.Avalonia\Free.Shared.Shell.Avalonia.csproj");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
