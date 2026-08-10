using System.IO;
using System.Windows.Input;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.Options;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class FreeWKeyboardShortcutIntegrationTests
{
    [StaFact]
    public void WpfWindowInstallsEverySharedCatalogGesture()
    {
        var window = new MainWindow(new FreeWOptions());
        try
        {
            var actual = window.InputBindings
                .OfType<KeyBinding>()
                .Where(binding => binding.Command is RoutedUICommand command &&
                                  command.Name.StartsWith("FreeW", StringComparison.Ordinal))
                .Select(binding => (KeyGesture)binding.Gesture)
                .Select(gesture => (gesture.Key, gesture.Modifiers))
                .ToArray();

            var expected = FreeWKeyboardShortcutCatalog.All
                .Select(shortcut => (ToWpfKey(shortcut.Key), ToWpfModifiers(shortcut.Modifiers)))
                .ToArray();

            actual.Should().HaveCount(expected.Length);
            actual.Should().BeEquivalentTo(expected);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void CurrentFieldCommandsResolveTheFocusedStoryEditor()
    {
        var bodyField = new ComplexField(" TITLE ");
        var storyField = new ComplexField(" SUBJECT ");
        var bodyEditor = BuildFieldEditor(bodyField, "body result");
        var storyEditor = BuildFieldEditor(storyField, "story result");

        var resolved = MainWindow.ResolveFieldCommandEditor(storyEditor, bodyEditor);

        MainWindow.ExecuteCurrentFieldCommand(FreeWKeyboardCommand.ToggleCurrentFieldCode, resolved);
        storyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single()
            .ComplexField!.ShowCode.Should().BeTrue();
        bodyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single()
            .ComplexField!.ShowCode.Should().BeFalse();

        MainWindow.ExecuteCurrentFieldCommand(FreeWKeyboardCommand.LockCurrentField, resolved);
        storyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single()
            .ComplexField!.IsLocked.Should().BeTrue();
        bodyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single()
            .ComplexField!.IsLocked.Should().BeFalse();

        MainWindow.ExecuteCurrentFieldCommand(FreeWKeyboardCommand.UnlockCurrentField, resolved);
        storyEditor.Model.Properties.Subject = "Current subject";
        bodyEditor.Model.Properties.Title = "Current title";
        MainWindow.ExecuteCurrentFieldCommand(FreeWKeyboardCommand.UpdateCurrentField, resolved);
        storyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single().Text
            .Should().Be("Current subject");
        bodyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single().Text
            .Should().Be("body result");

        MainWindow.ExecuteCurrentFieldCommand(FreeWKeyboardCommand.UnlinkCurrentField, resolved);
        storyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single()
            .ComplexField.Should().BeNull();
        bodyEditor.Model.Blocks.OfType<Paragraph>().Single().Runs.Single()
            .ComplexField.Should().NotBeNull();

        MainWindow.ResolveFieldCommandEditor(focusedElement: null, bodyEditor)
            .Should().BeSameAs(bodyEditor);
    }

    [StaFact]
    public void FindReplaceDialog_reuse_updates_open_mode_for_both_shortcuts()
    {
        var dialog = new FindReplaceDialog(null!, new FreeW.App.Host.Editing.DocumentView(), FindReplaceDialogOpenMode.Find);
        try
        {
            dialog.Show();
            dialog.Activate();
            dialog.ActivateFor(FindReplaceDialogOpenMode.Find);
            dialog.FocusedFieldForTest.Should().Be(FindReplaceDialogOpenMode.Find);

            dialog.Show();
            dialog.Activate();
            dialog.ActivateFor(FindReplaceDialogOpenMode.Replace);
            dialog.FocusedFieldForTest.Should().Be(FindReplaceDialogOpenMode.Replace);

            dialog.Show();
            dialog.Activate();
            dialog.ActivateFor(FindReplaceDialogOpenMode.Find);
            dialog.FocusedFieldForTest.Should().Be(FindReplaceDialogOpenMode.Find);
            dialog.OpenModeForTest.Should().Be(FindReplaceDialogOpenMode.Find);
        }
        finally
        {
            dialog.Close();
        }
    }

    [Fact]
    public void WpfPrintDocument_uses_the_shared_application_command_router()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew", "FreeW.App.Host", "MainWindow.cs"));

        source.Should().Contain("PrintDocument: Print");
        source.Should().Contain("_applicationCommands.Shortcuts");
        source.Should().Contain("_applicationCommands.Execute(command)");
        source.Should().NotContain("FreeWKeyboardShortcutCatalog.All");
    }

    [Fact]
    public void WpfPrintDocument_enables_and_applies_native_user_page_ranges()
    {
        var source = File.ReadAllText(
            Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew", "FreeW.App.Host", "MainWindow.cs"));

        source.Should().Contain("var plan = FreeWPrintRequestPlanner.Create(");
        source.Should().Contain("dialog.UserPageRangeEnabled = plan.TotalPages > 1;");
        source.Should().Contain("dialog.PageRangeSelection == PageRangeSelection.UserPages");
        source.Should().Contain("PageRangeDocumentPaginator.Create(");
    }

    private static Key ToWpfKey(FreeWKeyboardKey key) =>
        Enum.Parse<Key>(key.ToString());

    private static DocumentView BuildFieldEditor(ComplexField field, string cachedResult)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph
        {
            Runs =
            {
                new Run(cachedResult) { ComplexField = field }
            }
        });

        var editor = new DocumentView();
        editor.LoadModel(document);
        editor.CaretPosition = editor.Document.ContentStart.GetPositionAtOffset(2)
            ?? editor.Document.ContentStart;
        return editor;
    }

    private static ModifierKeys ToWpfModifiers(FreeWKeyboardModifiers modifiers)
    {
        var result = ModifierKeys.None;
        if ((modifiers & FreeWKeyboardModifiers.Control) != 0)
            result |= ModifierKeys.Control;
        if ((modifiers & FreeWKeyboardModifiers.Shift) != 0)
            result |= ModifierKeys.Shift;
        if ((modifiers & FreeWKeyboardModifiers.Alt) != 0)
            result |= ModifierKeys.Alt;
        return result;
    }
}
