using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class DialogLifecycleParityTests
{
    [StaFact]
    public void SlideSize_modal_dialog_has_default_cancel_and_validation_lifecycle()
    {
        var editor = MakeSession();
        var dialog = new SlideSizeDialog(editor);
        var buttons = FindButtons((DependencyObject)dialog.Content);

        buttons.Should().ContainSingle(button => button.IsDefault);
        buttons.Should().ContainSingle(button => button.IsCancel);
        dialog.InitialState.Preset.Should().Be(SlideSizeDialogPreset.Widescreen169);

        dialog.SetInputForTests("0.25", "7.5", SlideSizeDialogUnit.Inches);
        dialog.ApplyForTests().Should().BeFalse();
        dialog.ValidationText.Should().Be(SlideSizeDialogPlanner.MinimumSizeMessage);
        editor.Presentation.SlideSizeCxEmu.Should().Be(12_192_000L);

        dialog.SetInputForTests("11", "6.25", SlideSizeDialogUnit.Inches);
        dialog.ApplyForTests().Should().BeTrue();
        editor.Presentation.SlideSizeCxEmu.Should().Be(10_058_400L);
        editor.Presentation.SlideSizeCyEmu.Should().Be(5_715_000L);

        editor.Undo();
        editor.Presentation.SlideSizeCxEmu.Should().Be(12_192_000L);
        editor.Presentation.SlideSizeCyEmu.Should().Be(6_858_000L);
    }

    [StaFact]
    public void HeaderFooter_modal_dialog_has_apply_cancel_and_shared_result_lifecycle()
    {
        var editor = MakeSession();
        var dialog = new HeaderFooterDialog(editor, HeaderFooterCommandFocus.DateTime);
        var buttons = FindButtons((DependencyObject)dialog.Content);

        buttons.Single(button => Equals(button.Content, "Apply")).IsDefault.Should().BeTrue();
        buttons.Single(button => Equals(button.Content, "Cancel")).IsCancel.Should().BeTrue();
        dialog.RequestedFocus.Should().Be(HeaderFooterCommandFocus.DateTime);
        dialog.InitialState.Should().Be(HeaderFooterCommandPlanner.BuildState(editor));

        dialog.ApplyForTests(
            showDateTime: true,
            showFooter: true,
            showSlideNumber: true,
            footerText: "Deck footer",
            scope: HeaderFooterApplyScope.CurrentSlide).Should().BeTrue();

        dialog.LastApplyPlan.Should().NotBeNull();
        editor.Presentation.Slides[0].HfVisibility!.ShowFooter.Should().BeTrue();
    }

    [StaFact]
    public void FindReplace_modeless_dialog_reuses_instance_switches_mode_and_exposes_close_action()
    {
        var window = new MainWindow();
        window.OpenFindDialog();
        var dialog = window.ActiveFindReplaceDialog!;
        try
        {
            dialog.Title.Should().Be(FindReplaceDialogPlanner.FindTitle);
            dialog.ShowReplace.Should().BeFalse();
            GetField<RowDefinition>(dialog, "_replaceRow").Height.Should().Be(new GridLength(0));

            window.OpenFindReplaceDialog();

            window.ActiveFindReplaceDialog.Should().BeSameAs(dialog);
            dialog.Title.Should().Be(FindReplaceDialogPlanner.FindAndReplaceTitle);
            dialog.ShowReplace.Should().BeTrue();
            GetField<RowDefinition>(dialog, "_replaceRow").Height.Should().Be(GridLength.Auto);
            FindButtons((DependencyObject)dialog.Content)
                .Should().ContainSingle(button => Equals(button.Content, "Close") && button.IsCancel);
        }
        finally
        {
            dialog.Close();
            window.Close();
        }
        window.ActiveFindReplaceDialog.Should().BeNull();
    }

    [StaFact]
    public void FindReplace_modeless_dialog_applies_shared_navigation_and_replace_results()
    {
        var editor = MakeSession();
        var shape = editor.InsertTextBox("cat cat");
        var refreshCount = 0;
        var dialog = new FindReplaceDialog(editor, showReplace: true, () => refreshCount++);

        dialog.SetInputForTests("cat", "dog");
        var navigation = dialog.NavigateForTests(+1);
        navigation.StatusText.Should().Be("Match 1 of 2");
        editor.SelectedShapeIds.Should().ContainSingle().Which.Should().Be(shape.Id);

        var replacement = dialog.ReplaceAllForTests();
        replacement.StatusText.Should().Be("2 replacement(s) made.");
        shape.TextBody!.Paragraphs[0].Runs[0].Text.Should().Be("dog dog");
        refreshCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [StaFact]
    public void Comments_command_reveals_empty_pane_for_first_comment_creation()
    {
        var window = new MainWindow();
        try
        {
            var plan = window.ShowReviewCommentsPane();

            plan.Comments.Should().BeEmpty();
            window.IsReviewCommentsPaneVisible.Should().BeTrue();

            window.HideReviewCommentsPane();
            window.IsReviewCommentsPaneVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void FindReplace_modeless_dialog_source_guards_escape_and_editor_rebind_cleanup()
    {
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var dialogSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "freep", "FreeP.App.Host", "FindReplaceDialog.cs"));
        var mainWindowSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var workareaEndpointSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "freep", "FreeP.App.Host", "MainWindow.WorkareaEndpoint.cs"));

        dialogSource.Should().Contain("e.Key != Key.Escape");
        dialogSource.Should().Contain("Close();");
        mainWindowSource.Should().Contain("_findReplaceDialog.Closed += (_, _) => _findReplaceDialog = null;");
        workareaEndpointSource.Should().Contain("BeforePresentationReplaced = () => _findReplaceDialog?.Close()");
    }

    private static EditingSession MakeSession()
    {
        var presentation = new Presentation
        {
            SlideSizeCxEmu = 12_192_000L,
            SlideSizeCyEmu = 6_858_000L,
        };
        presentation.Slides.Add(new Slide());
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static T GetField<T>(object instance, string fieldName)
        where T : class
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(instance)!;
    }

    private static IReadOnlyList<Button> FindButtons(DependencyObject root)
    {
        var buttons = new List<Button>();
        Visit(root);
        return buttons;

        void Visit(DependencyObject current)
        {
            if (current is Button button)
                buttons.Add(button);

            foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
                Visit(child);
        }
    }
}
