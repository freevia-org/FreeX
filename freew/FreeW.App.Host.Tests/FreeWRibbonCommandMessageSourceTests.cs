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
        source.Should().Contain("UiText.Get(\"ChangeCase_SelectText_Message\")");
        fragmentWorkflow.Should().Contain("$\"Could not insert the {subject}:\\n{reason}\"");
        pictureWorkflow.Should().Contain("$\"Could not insert the image:\\n{reason}\"");
        source.Should().Contain("UiText.Format(\"ScreenClip_Failed_Message_Format\"");
        source.Should().Contain("UiText.Format(\"Review_CompareFailed_Message_Format\"");
        source.Should().Contain("UiText.Format(\"Review_CombineFailed_Message_Format\"");
        source.Should().Contain("MailMergeDialogMetadata.MailMergeTitle");
        mailMergeMetadata.Should().Contain("public static string MailMergeTitle => Text(\"MailMerge_Dialog_Title\");");
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
