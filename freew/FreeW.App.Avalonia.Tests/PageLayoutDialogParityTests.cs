using System.IO;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class PageLayoutDialogParityTests
{
    [Theory]
    [InlineData("ColumnsDialog", "ColumnsDialogPlanner")]
    [InlineData("CustomParagraphSpacingDialog", "CustomParagraphSpacingDialogPlanner")]
    [InlineData("DropCapOptionsDialog", "DropCapOptionsDialogPlanner")]
    [InlineData("HyphenationOptionsDialog", "HyphenationOptionsDialogPlanner")]
    [InlineData("LineNumberOptionsDialog", "LineNumberOptionsDialogPlanner")]
    public void Mandatory_dialogs_share_planners_and_modal_lifecycle(string dialogName, string plannerName)
    {
        var source = ReadSource("PageLayoutDialogs.cs");
        var start = source.IndexOf($"class {dialogName}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var declarationStart = source.LastIndexOf("public sealed ", start, StringComparison.Ordinal);
        var next = source.IndexOf("public sealed ", start + dialogName.Length, StringComparison.Ordinal);
        var dialog = next < 0 ? source[declarationStart..] : source[declarationStart..next];

        dialog.Should().Contain(plannerName);
        dialog.Should().Contain("PageLayoutDialogChrome.Actions(");
        dialog.Should().Contain("PageLayoutDialogChrome.WireEscape<");
        dialog.Should().Contain("Opened +=");
        dialog.Should().Contain("ShowDialog<");
        dialog.Should().Contain("editor.Focus();");
    }

    [Fact]
    public void Shared_chrome_owns_default_cancel_escape_and_null_close()
    {
        var source = ReadSource("PageLayoutDialogs.cs");
        var sharedFactory = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaDialogButtonRowFactory.cs"));

        source.Should().Contain("AvaloniaDialogButtonRowFactory.CreateOkCancel(");
        sharedFactory.Should().Contain("IsDefault = isDefault");
        sharedFactory.Should().Contain("IsCancel = isCancel");
        source.Should().Contain("e.Key != Key.Escape");
        source.Should().Contain("window.Close(default(TResult))");
        source.Should().Contain("Close(null)");
    }

    [Fact]
    public void Columns_result_is_one_undoable_page_settings_change()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Columns"));
        var editor = new DocumentView();
        editor.LoadDocument(document);

        ColumnsDialog.ApplyResult(editor, new ColumnsDialogResult(3, 24, true, null));

        editor.Document.Page.ColumnCount.Should().Be(3);
        editor.Document.Page.ColumnsLineBetween.Should().BeTrue();
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        editor.Document.Page.ColumnCount.Should().Be(1);
        editor.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Mandatory_route_ids_are_declared_and_mapped_by_the_shared_profile()
    {
        var definition = ReadDefinitionSource();
        var workflow = ReadPresentationSource(Path.Combine("Ribbon", "FreeWRibbonCommandWorkflow.cs"));
        var profile = ReadPresentationSource(Path.Combine("Ribbon", "FreeWRibbonHostExecutionProfile.cs"));
        var renderer = ReadSource(Path.Combine("Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        foreach (var (id, action) in new[]
        {
            ("freew.columns-more", "ColumnsMore"),
            ("freew.custom-paragraph-spacing", "CustomParagraphSpacing"),
            ("freew.drop-cap-options", "DropCapOptions"),
            ("freew.hyphenation-options", "HyphenationOptions"),
            ("freew.line-numbers-options", "LineNumbersOptions"),
        })
        {
            definition.Should().Contain(id);
            workflow.Should().Contain($"new(\"{id}\", FreeWRibbonCommandAction.{action})");
            switch (action)
            {
                case "CustomParagraphSpacing":
                    ReadPresentationSource(Path.Combine("Ribbon", "DesignRibbonWorkflow.cs"))
                        .Should().Contain($"FreeWRibbonCommandAction.{action}");
                    renderer.Should().Contain($"{action}: OptionalHostCommand(callbacks.Open{action}Dialog)");
                    break;
                case "DropCapOptions":
                    ReadPresentationSource(Path.Combine("Ribbon", "DropCapRibbonWorkflow.cs"))
                        .Should().Contain($"FreeWRibbonCommandAction.{action}");
                    renderer.Should().Contain("Options: OptionalHostCommand(callbacks.OpenDropCapOptionsDialog)");
                    break;
                default:
                    (profile + renderer).Should().Contain($"FreeWRibbonCommandAction.{action}");
                    break;
            }
        }
    }

    [Fact]
    public void Manual_hyphenation_uses_owner_modal_shared_session_without_enabling_automatic_mode()
    {
        var dialogs = ReadSource("PageLayoutDialogs.cs");
        var profile = ReadPresentationSource(Path.Combine("Ribbon", "FreeWRibbonHostExecutionProfile.cs"));
        var mainWindow = ReadSource("MainWindow.cs");
        var start = dialogs.IndexOf("public sealed class ManualHyphenationDialog", StringComparison.Ordinal);
        var end = dialogs.IndexOf("public sealed class LineNumberOptionsDialog", start, StringComparison.Ordinal);
        var dialog = dialogs[start..end];

        profile.Should().Contain(
            "BindOrUnavailable(bindings, FreeWRibbonCommandAction.HyphenationManual, ports.OpenManualHyphenationDialog)");
        mainWindow.Should().Contain("OpenManualHyphenationDialog: () => _ = OpenManualHyphenationDialogAsync()");
        mainWindow.Should().Contain("ManualHyphenationDialog.ShowAndApplyAsync(this, _editor");
        dialog.Should().Contain("ManualHyphenationPlanner.CreateSession(editor.Document)");
        dialog.Should().Contain("editor.ApplyManualHyphenation(session.Edits)");
        dialog.Should().Contain("ShowDialog<ManualHyphenationDialogResult?>");
        dialog.Should().NotContain("AutoHyphenation");
        dialog.Should().NotContain("ApplyPageSettings");
    }

    private static string ReadDefinitionSource()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var definitionRoot = Path.Combine(root, "freew", "FreeW.Ribbon.Definitions");
        return string.Join(
            Environment.NewLine,
            Directory.GetFiles(definitionRoot, "FreeWCanonicalRibbonTabs*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadPresentationSource(string relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Presentation", relativePath));
    }

    private static string ReadSource(string relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", relativePath));
    }
}
