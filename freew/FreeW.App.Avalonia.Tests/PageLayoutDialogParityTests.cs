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
        var start = source.IndexOf($"public sealed class {dialogName}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var next = source.IndexOf("public sealed class ", start + 20, StringComparison.Ordinal);
        var dialog = next < 0 ? source[start..] : source[start..next];

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
    public void Mandatory_route_ids_are_declared_and_registered()
    {
        var definition = ReadDefinitionSource();
        var registry = ReadSource(Path.Combine("Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        foreach (var id in new[]
        {
            "freew.columns-more",
            "freew.custom-paragraph-spacing",
            "freew.drop-cap-options",
            "freew.hyphenation-options",
            "freew.line-numbers-options",
        })
        {
            definition.Should().Contain(id);
            registry.Should().Contain($"Register(\"{id}\"");
        }
    }

    private static string ReadDefinitionSource()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.Ribbon.Definitions", "FreeWAvaloniaRibbonDefinition.cs"));
    }

    private static string ReadSource(string relativePath)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", relativePath));
    }
}
