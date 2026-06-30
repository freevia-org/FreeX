using System.IO;

namespace FreeW.App.Avalonia.Tests;

public sealed class SisterDialogTextResourceSourceTests
{
    [Fact]
    public void InsertDialogs_ResolveTextFromPresentationResources()
    {
        var source = ReadAvaloniaSource("InsertDialogs.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("InsertDialogTextResources.Hyperlink");
        source.Should().Contain("InsertDialogTextResources.Bookmark");
        source.Should().Contain("InsertDialogTextResources.QuickPart");
        source.Should().Contain("InsertDialogTextResources.OkButton");
        source.Should().NotContain("PlaceholderText = \"Text to display\"");
        source.Should().NotContain("Title = \"Insert Hyperlink\"");
        source.Should().NotContain("MakeButton(\"Add\"");
        source.Should().NotContain("MakeButton(\"OK\"");
    }

    [Fact]
    public void PageSetupDialog_ResolvesChromeTextFromPlanner()
    {
        var source = ReadAvaloniaSource("PageSetupDialog.cs");

        source.Should().Contain("Title = PageSetupDialogPlanner.Title;");
        source.Should().Contain("PageSetupDialogPlanner.MarginsSectionLabel");
        source.Should().Contain("PageSetupDialogPlanner.OrientationNames[0]");
        source.Should().Contain("PageSetupDialogPlanner.OkButton");
        source.Should().NotContain("Title = \"Page Setup\"");
        source.Should().NotContain("Content = \"OK\"");
        source.Should().NotContain("SectionLabel(\"Margins (points)\")");
    }

    [Fact]
    public void MainWindow_ResolvesFilePickerAndStatusTextFromSharedResources()
    {
        var source = ReadAvaloniaSource("MainWindow.cs");

        source.Should().Contain("SisterAppFileTextPlanner.Document");
        source.Should().Contain("FileText.OpenPickerTitle");
        source.Should().Contain("FileText.SavePickerTitle");
        source.Should().Contain("FreeWFileTextResources.ExportPdfPickerTitle");
        source.Should().Contain("InsertDialogTextResources.TextFromFilePickerTitle");
        source.Should().Contain("SisterAppFileTextPlanner.FormatUnsupportedFileType(");
        source.Should().Contain("SisterAppFileTextPlanner.FormatCommandFailed(");
        source.Should().NotContain("Title = \"Open document\"");
        source.Should().NotContain("Title = \"Save document\"");
        source.Should().NotContain("_status.Text = $\"Open failed:");
        source.Should().NotContain("_status.Text = $\"Save failed:");
        source.Should().NotContain("_status.Text = $\"Saved ");
    }

    [Fact]
    public void BackstageView_ResolvesRailAndPaneTextFromPresentationResources()
    {
        var source = ReadAvaloniaSource("Backstage", "BackstageView.cs");

        source.Should().Contain("BackstageViewTextResources.WindowTitle");
        source.Should().Contain("BackstageViewTextResources.RailEntries");
        source.Should().Contain("BackstageViewTextResources.Home.Title");
        source.Should().Contain("BackstageViewTextResources.DirectPrintDeferredNote");
        source.Should().Contain("BackstageViewTextResources.ProductName");
        source.Should().NotContain("Content = \"\u2190 Back\"");
        source.Should().NotContain("AddNavEntry(panel, BackstagePane.Home, \"Home\")");
        source.Should().NotContain("BuildPaneHeader(\"Export\"");
        source.Should().NotContain("Create PDF/XPS Document");
    }

    private static string ReadAvaloniaSource(params string[] pathParts)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot(), "freew", "FreeW.App.Avalonia" }.Concat(pathParts).ToArray());
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
