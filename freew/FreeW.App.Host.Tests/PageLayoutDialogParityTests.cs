using System.IO;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host.Tests;

public sealed class PageLayoutDialogParityTests
{
    [Theory]
    [InlineData("ColumnsDialog.cs", "ColumnsDialogPlanner")]
    [InlineData("CustomParagraphSpacingDialog.cs", "CustomParagraphSpacingDialogPlanner")]
    [InlineData("DropCapOptionsDialog.cs", "DropCapOptionsDialogPlanner")]
    [InlineData("HyphenationOptionsDialog.cs", "HyphenationOptionsDialogPlanner")]
    [InlineData("LineNumberOptionsDialog.cs", "LineNumberOptionsDialogPlanner")]
    public void Mandatory_dialogs_share_planners_and_modal_lifecycle(string fileName, string plannerName)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain(plannerName);
        source.Should().Contain("DialogButtonRowFactory.Create(");
        source.Should().Contain("DialogFocus.FocusAndSelect(");
        source.Should().Contain("ShowDialog()");
        source.Should().Contain("return dialog._result;");
    }

    [Fact]
    public void Drop_cap_policy_is_extracted_without_nested_shadow()
    {
        var commands = ReadHostSource(Path.Combine("Ribbon", "FreeWRibbonCommands.cs"));

        commands.Should().Contain("global::FreeW.App.Host.DropCapOptionsDialog.Prompt(");
        commands.Should().NotContain("private static class DropCapOptionsDialog");
        commands.Should().NotContain("DropCapOptionsResult?");
    }

    [StaFact]
    public void Columns_result_is_one_undoable_page_settings_change()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Columns"));
        var editor = new DocumentView();
        editor.LoadModel(document);

        editor.ApplyPageSettings(page => PageLayoutCommandPlanner.ApplyColumnsResult(
            page,
            new ColumnsDialogResult(3, 24, true, null)));

        editor.Model.Page.ColumnCount.Should().Be(3);
        editor.Model.Page.ColumnsLineBetween.Should().BeTrue();
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.Model.Page.ColumnCount.Should().Be(1);
        editor.CanUndo.Should().BeFalse();
    }

    [Theory]
    [InlineData("freew.columns-more", FreeWRibbonCommandAction.ColumnsMore)]
    [InlineData("freew.custom-paragraph-spacing", FreeWRibbonCommandAction.CustomParagraphSpacing)]
    [InlineData("freew.drop-cap-options", FreeWRibbonCommandAction.DropCapOptions)]
    [InlineData("freew.hyphenation-options", FreeWRibbonCommandAction.HyphenationOptions)]
    [InlineData("freew.line-numbers-options", FreeWRibbonCommandAction.LineNumbersOptions)]
    public void Mandatory_route_ids_are_registered(string expectedId, FreeWRibbonCommandAction action)
    {
        var commands = ReadHostSource(Path.Combine("Ribbon", "FreeWRibbonCommands.cs"));
        var registrationSource = action switch
        {
            FreeWRibbonCommandAction.CustomParagraphSpacing =>
                ReadPresentationSource("DesignRibbonWorkflow.cs"),
            FreeWRibbonCommandAction.DropCapOptions =>
                ReadPresentationSource("DropCapRibbonWorkflow.cs"),
            _ => commands,
        };

        FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action).Value.Should().Be(expectedId);
        registrationSource.Should().Contain($"Bind(FreeWRibbonCommandAction.{action}");
    }

    [Fact]
    public void Manual_hyphenation_reviews_shared_candidates_without_enabling_automatic_mode()
    {
        var commands = ReadHostSource(Path.Combine("Ribbon", "FreeWRibbonCommands.cs"));
        var start = commands.IndexOf("private sealed class HyphenationManualCommand", StringComparison.Ordinal);
        var end = commands.IndexOf("// Hyphenation dropdown", start + 40, StringComparison.Ordinal);
        var command = commands[start..end];
        var dialog = ReadHostSource("ManualHyphenationDialog.cs");

        command.Should().Contain("ManualHyphenationPlanner.CreateSession(editor.Model)");
        command.Should().Contain("ManualHyphenationDialog.Prompt(");
        command.Should().Contain("editor.ApplyManualHyphenation(session.Edits)");
        command.Should().NotContain("AutoHyphenation");
        command.Should().NotContain("ApplyPageSettings");
        dialog.Should().Contain("ManualHyphenationDialogSession");
        dialog.Should().Contain("_session.PlanAcceptance(");
        dialog.Should().Contain("_session.PlanSkip()");
        dialog.Should().Contain("_session.PlanCancel()");
        dialog.Should().NotContain("new ManualHyphenationDialogResult(");
    }

    private static string ReadHostSource(string relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", relativePath));
    }

    private static string ReadPresentationSource(string fileName)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            fileName));
    }
}
