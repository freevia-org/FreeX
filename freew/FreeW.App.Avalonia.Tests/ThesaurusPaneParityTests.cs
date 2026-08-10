using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;
using System.Threading;

namespace FreeW.App.Avalonia.Tests;

public sealed class ThesaurusPaneParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Insert_replaces_selected_word_as_one_undoable_edit_and_refreshes_pane()
    {
        string? before = null;
        string? after = null;
        var undoAvailable = false;
        var heading = string.Empty;

        await Session.Dispatch(() =>
        {
            var editor = NewEditor("happy");
            editor.MoveCaretToBlock(0, 2);
            editor.SetSelectionRangePublic(0, 0, 0, 5);
            var pane = new ThesaurusPane(editor, _ => Task.FromResult(true));
            pane.Toggle();
            before = editor.SelectedText;

            pane.ReplaceForTest("pleased").Should().BeTrue();

            after = editor.PlainText;
            undoAvailable = editor.CanUndo;
            heading = pane.HeadingForTest;
            editor.Undo();
            editor.PlainText.Should().Be("happy");
        }, CancellationToken.None);

        before.Should().Be("happy");
        after.Should().Be("pleased");
        undoAvailable.Should().BeTrue();
        heading.Should().Be("pleased");
    }

    [Fact]
    public async Task Copy_uses_injected_clipboard_and_unavailable_platform_clipboard_is_disabled()
    {
        string? copied = null;
        bool copyResult = false;
        IReadOnlyList<(bool InsertEnabled, bool CopyEnabled)> disabledStates = [];

        await Session.Dispatch(() =>
        {
            var editor = NewEditor("happy");
            editor.MoveCaretToBlock(0, 2);
            var unavailablePane = new ThesaurusPane(editor);
            unavailablePane.Toggle();
            disabledStates = unavailablePane.ActionStatesForTest;

            var pane = new ThesaurusPane(editor, text =>
            {
                copied = text;
                return Task.FromResult(true);
            });
            pane.Toggle();
            copyResult = pane.CopyForTestAsync("pleased").GetAwaiter().GetResult();
        }, CancellationToken.None);

        disabledStates.Should().NotBeEmpty();
        disabledStates.Should().OnlyContain(state => state.InsertEnabled && !state.CopyEnabled);
        copyResult.Should().BeTrue();
        copied.Should().Be("pleased");
    }

    [Fact]
    public void Source_keeps_avalonia_actions_shared_and_platform_honest()
    {
        var pane = File.ReadAllText(RepoFile("freew", "FreeW.App.Avalonia", "ThesaurusPane.cs"));
        var wpf = File.ReadAllText(RepoFile("freew", "FreeW.App.Host", "ThesaurusPane.cs"));

        pane.Should().Contain("Content = \"↵\"");
        pane.Should().Contain("ThesaurusPaneSession _session");
        pane.Should().Contain("_session.CompleteReplacement");
        pane.Should().Contain("action.InsertToolTip");
        pane.Should().Contain("private readonly Func<string, Task<bool>>? _copyText;");
        pane.Should().NotContain("TopLevel.GetTopLevel(this)?.Clipboard");
        pane.Should().Contain("return false;");
        pane.Should().NotContain("Content = \"Replace\"");
        pane.Should().NotContain("ThesaurusPresentationPlanner.Lookup");
        wpf.Should().Contain("Content = \"↵\"");
        wpf.Should().Contain("ThesaurusPaneSession _session");
        wpf.Should().Contain("_session.CompleteReplacement");
        wpf.Should().Contain("action.InsertToolTip");
        wpf.Should().NotContain("action.ReplaceToolTip");
        wpf.Should().NotContain("ThesaurusPresentationPlanner.Lookup");
    }

    private static DocumentView NewEditor(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text));
        document.Blocks.Add(paragraph);
        var editor = new DocumentView();
        editor.LoadDocument(document);
        return editor;
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
