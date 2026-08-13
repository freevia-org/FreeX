using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FreeWRibbonCommandMessageSourceTests
{
    [Fact]
    public void RibbonCommands_RouteMessagesThroughDialogMessageHelper()
    {
        var source = ReadRibbonCommandsSource();
        var pictureWorkflow = ReadPresentationSource(
            "DocumentFragments", "FreeWPictureImportWorkflow.cs");
        var fragmentWorkflow = ReadPresentationSource(
            "DocumentFragments", "FreeWDocumentFragmentImportWorkflow.cs");
        var mailMergeMetadata = ReadPresentationSource(
            "Ribbon", "MailMergeDialogMetadata.cs");

        source.Should().Contain("DialogMessageHelper.ShowInfo(");
        source.Should().Contain("DialogMessageHelper.ShowError(");
        source.Should().Contain("\"Select some text first, then choose Change Case.\"");
        fragmentWorkflow.Should().Contain("$\"Could not insert the {subject}:\\n{reason}\"");
        pictureWorkflow.Should().Contain("$\"Could not insert the image:\\n{reason}\"");
        source.Should().Contain("\"Could not capture the screen clip:");
        source.Should().Contain("\"Could not compare the documents:");
        source.Should().Contain("\"Could not combine the documents:");
        source.Should().Contain("MailMergeDialogMetadata.MailMergeTitle");
        mailMergeMetadata.Should().Contain("public const string MailMergeTitle = \"Mail Merge\";");
        source.Should().NotContain("MessageBox.Show(");
        source.Should().NotContain("MessageBoxButton.");
        source.Should().NotContain("MessageBoxImage.");
    }

    private static string ReadRibbonCommandsSource()
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
        return File.ReadAllText(path);
    }

    private static string ReadPresentationSource(params string[] relativeParts)
    {
        var path = Path.Combine(
            new[]
            {
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew",
                "FreeW.App.Presentation"
            }.Concat(relativeParts).ToArray());
        return File.ReadAllText(path);
    }

}
