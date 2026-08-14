using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class MailMergeDialogMetadataTests
{
    [Fact]
    public void CatalogOwnsCommonMailMergeDialogSemantics()
    {
        MailMergeDialogMetadata.MatchFieldsTitle.Should().Be("Match Fields");
        MailMergeDialogMetadata.FilterInstruction.Should().Contain("sort order");
        MailMergeDialogMetadata.FormatFinishIssue(MailMergeFinishIssue.InvalidRange)
            .Should().Be("Finish and merge: InvalidRange.");
        MailMergeDialogMetadata.FormatFieldsHint(["FirstName", "Email"])
            .Should().Be("Fields in this document: FirstName, Email");
    }

    [Fact]
    public void NativeMailMergeDialogsConsumeSharedMetadata()
    {
        var root = FindRepositoryRoot();
        var wpf = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "MailMergeDialogs.cs"));

        wpf.Should().Contain("MailMergeDialogMetadata.MatchFieldsTitle");
        wpf.Should().Contain("MailMergeDialogMetadata.FilterSortRecipientsTitle");
        wpf.Should().Contain("MailingsEnvelopeLabelPlanner.CreateEnvelopeDialogPlan()");
        wpf.Should().Contain("MailingsEnvelopeLabelPlanner.CreateLabelDialogPlan()");
        wpf.Should().NotContain("MailingsEnvelopeLabelPlanner.GetEnvelopeSizes()");
        wpf.Should().NotContain("MailingsEnvelopeLabelPlanner.GetLabelPresets()");
        avalonia.Should().Contain("MailMergeDialogMetadata.MatchFieldsTitle");
        avalonia.Should().Contain("MailMergeDialogMetadata.FilterSortRecipientsTitle");
        avalonia.Should().Contain("MailMergeDialogMetadata.FinishAndMergeTitle");
    }

    [Fact]
    public void NativeMailMergeDialogsUseSharedRendererChromeAcrossHosts()
    {
        var root = FindRepositoryRoot();
        var wpf = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avalonia = File.ReadAllText(Path.Combine(
            root, "freew", "FreeW.App.Avalonia", "MailMergeDialogs.cs"));

        wpf.Should().Contain(
            "class MailMergeDialogWindow : Free.Shared.Ribbon.Wpf.DialogWindow");
        Count(wpf, "new MailMergeDialogWindow").Should().Be(13,
            "every code-built WPF mail-merge form must receive shared dialog resources");

        avalonia.Should().Contain("class MailMergeDialogWindow : FreeWDialogWindow");
        avalonia.Should().Contain("new MailMergeDialogWindow()");
        avalonia.Should().NotContain("new Window");
        Count(avalonia, "CreateDialog(").Should().Be(15,
            "fourteen shared-form routes plus the factory declaration must remain centralized");
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
