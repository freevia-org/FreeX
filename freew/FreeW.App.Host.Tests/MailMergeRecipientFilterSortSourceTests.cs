using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class MailMergeRecipientFilterSortSourceTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesRecipientFilterSortPolicyToPresentationPlanner()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("MailMergeRecipientFilterSortPlanner.GetPreviewColumns(");
        source.Should().Contain("MailMergeRecipientFilterSortPlanner.FormatPreviewHeader(");
        source.Should().Contain("MailMergeRecipientFilterSortPlanner.FormatPreviewRow(");
        source.Should().Contain("MailMergeRecipientFilterSortPlanner.Apply(");
        source.Should().NotContain("const int MaxPreviewCols");
        source.Should().NotContain("chosen.OrderBy(");
        source.Should().NotContain("chosen.OrderByDescending(");
        source.Should().NotContain("new MergeData(data.Header, result.Select");
    }

    [Fact]
    public void RecipientChanges_RestoreActivePreviewTemplateBeforeInvalidation()
    {
        var source = File.ReadAllText(
                Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("var fields = MailMerge.FieldNames(template);");
        source.Should().Contain("RestoreEditableTemplate(editor, session);");
        source.Should().Contain("session.EndPreview()");
        source.Should().Contain("new SetMergeModeCommand(editor, mergeSession");
        source.Should().Contain("new ClearMergeSessionCommand(editor, mergeSession)");
    }

}
