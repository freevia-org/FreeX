using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class DialogLifecycleParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    static DialogLifecycleParityTests()
    {
        if (AppProduct.Current is null)
            AppProduct.Current = new AppProductIdentity("FreeP", "FREEP_DIAGNOSTICS", "FreeP");
    }

    [Fact]
    public async Task SlideSize_modal_dialog_has_default_cancel_and_validation_lifecycle()
    {
        await Session.Dispatch(() =>
        {
            var editor = MakeSession();
            var dialog = new SlideSizeDialog(editor);
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();

            buttons.Single(button => Equals(button.Content, "OK")).IsDefault.Should().BeTrue();
            buttons.Single(button => Equals(button.Content, "Cancel")).IsCancel.Should().BeTrue();
            dialog.InitialState.Preset.Should().Be(SlideSizeDialogPreset.Widescreen169);

            dialog.Show();
            dialog.SetInputForTests("0.25", "7.5", SlideSizeDialogUnit.Inches);
            dialog.ApplyForTests().Should().BeFalse();
            dialog.IsVisible.Should().BeTrue();
            dialog.ValidationText.Should().Be(SlideSizeDialogPlanner.MinimumSizeMessage);
            editor.Presentation.SlideSizeCxEmu.Should().Be(12_192_000L);

            dialog.SetInputForTests("11", "6.25", SlideSizeDialogUnit.Inches);
            dialog.ApplyForTests().Should().BeTrue();
            dialog.IsVisible.Should().BeFalse();
            editor.Presentation.SlideSizeCxEmu.Should().Be(10_058_400L);
            editor.Presentation.SlideSizeCyEmu.Should().Be(5_715_000L);

            editor.Undo();
            editor.Presentation.SlideSizeCxEmu.Should().Be(12_192_000L);
            editor.Presentation.SlideSizeCyEmu.Should().Be(6_858_000L);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HeaderFooter_modal_dialog_has_apply_cancel_and_shared_result_lifecycle()
    {
        await Session.Dispatch(() =>
        {
            var editor = MakeSession();
            var dialog = new HeaderFooterDialog(editor, HeaderFooterCommandFocus.DateTime);
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();

            buttons.Single(button => Equals(button.Content, "Apply")).IsDefault.Should().BeTrue();
            buttons.Single(button => Equals(button.Content, "Cancel")).IsCancel.Should().BeTrue();
            dialog.RequestedFocus.Should().Be(HeaderFooterCommandFocus.DateTime);

            dialog.ApplyForTests(
                showDateTime: true,
                showFooter: true,
                showSlideNumber: true,
                footerText: "Deck footer",
                scope: HeaderFooterApplyScope.CurrentSlide).Should().BeTrue();

            dialog.LastApplyPlan.Should().NotBeNull();
            dialog.LastApplyPlan!.ShouldApply.Should().BeTrue();
            editor.Presentation.Slides[0].HfVisibility!.ShowFooter.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SlideShowSettings_modal_dialog_applies_shared_playback_options()
    {
        await Session.Dispatch(() =>
        {
            var editor = MakeSession();
            var dialog = new SlideShowSettingsDialog(editor);
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();

            buttons.Single(button => Equals(button.Content, "OK")).IsDefault.Should().BeTrue();
            buttons.Single(button => Equals(button.Content, "Cancel")).IsCancel.Should().BeTrue();
            dialog.InitialState.Should().Be(new SlideShowSettingsState(true, true, false));

            dialog.ApplyForTests(
                useSlideTimings: false,
                showWithAnimation: false,
                loopUntilStopped: true,
                showType: PresentationShowType.BrowsedAtKiosk,
                showBrowseScrollbar: false,
                kioskRestartAfterMilliseconds: 18_000).Should().BeTrue();

            editor.Presentation.UseSlideTimings.Should().BeFalse();
            editor.Presentation.ShowWithAnimation.Should().BeFalse();
            editor.Presentation.LoopUntilStopped.Should().BeTrue();
            editor.Presentation.ShowType.Should().Be(PresentationShowType.BrowsedAtKiosk);
            editor.Presentation.ShowBrowseScrollbar.Should().BeFalse();
            editor.Presentation.KioskRestartAfterMilliseconds.Should().Be(18_000);
            editor.Undo();
            editor.Presentation.UseSlideTimings.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FindReplace_modeless_dialog_reuses_instance_switches_mode_and_escape_closes()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                window.OpenFindDialog();
                var dialog = window.ActiveFindReplaceDialog!;
                dialog.IsVisible.Should().BeTrue();
                dialog.Title.Should().Be(FindReplaceDialogPlanner.FindTitle);
                dialog.ShowReplace.Should().BeFalse();

                window.OpenFindReplaceDialog();
                window.ActiveFindReplaceDialog.Should().BeSameAs(dialog);
                dialog.Title.Should().Be(FindReplaceDialogPlanner.FindAndReplaceTitle);
                dialog.ShowReplace.Should().BeTrue();

                var escape = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Escape,
                    Source = dialog,
                };
                dialog.RaiseEvent(escape);

                escape.Handled.Should().BeTrue();
                dialog.IsVisible.Should().BeFalse();
                window.ActiveFindReplaceDialog.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FindReplace_modeless_dialog_applies_shared_navigation_and_replace_results()
    {
        await Session.Dispatch(() =>
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
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Comments_command_reveals_empty_pane_for_first_comment_creation()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
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
        }, CancellationToken.None);
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
}
