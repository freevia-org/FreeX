using System.Reflection;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowSettingsDialogTests
{
    [StaFact]
    public void SetupDialog_ReflectsPresentationDefaultsAndAppliesUndoably()
    {
        var editor = MakeSession();
        var dialog = new SlideShowSettingsDialog(editor);

        GetField<CheckBox>(dialog, "_useTimingsCheck").IsChecked.Should().BeTrue();
        GetField<CheckBox>(dialog, "_showAnimationCheck").IsChecked.Should().BeFalse();
        GetField<CheckBox>(dialog, "_loopCheck").IsChecked.Should().BeFalse();
        GetField<CheckBox>(dialog, "_showNarrationCheck").IsChecked.Should().BeTrue();
        GetField<CheckBox>(dialog, "_showMediaControlsCheck").IsChecked.Should().BeTrue();
        GetField<CheckBox>(dialog, "_showMasterShapesCheck").IsChecked.Should().BeTrue();
        GetField<ComboBox>(dialog, "_showTypeCombo").Items
            .Cast<SlideShowSettingsShowTypeOption>()
            .Select(option => option.Label)
            .Should().Equal(
                "Presented by a speaker",
                "Browsed by an individual",
                "Browsed at a kiosk");

        dialog.ApplyForTests(
            useSlideTimings: false,
            showWithAnimation: false,
            loopUntilStopped: true,
            showType: PresentationShowType.BrowsedByIndividual,
            showBrowseScrollbar: false,
            kioskRestartAfterMilliseconds: 12_000,
            showWithNarration: false,
            showMediaControls: false,
            showMasterShapes: false).Should().BeTrue();
        editor.Presentation.UseSlideTimings.Should().BeFalse();
        editor.Presentation.ShowWithAnimation.Should().BeFalse();
        editor.Presentation.LoopUntilStopped.Should().BeTrue();
        editor.Presentation.ShowType.Should().Be(PresentationShowType.BrowsedByIndividual);
        editor.Presentation.ShowBrowseScrollbar.Should().BeFalse();
        editor.Presentation.KioskRestartAfterMilliseconds.Should().Be(12_000);
        editor.Presentation.ShowWithNarration.Should().BeFalse();
        editor.Presentation.ShowMediaControls.Should().BeFalse();
        editor.Presentation.ShowMasterShapes.Should().BeFalse();
        dialog.LastCommitPlan!.Settings.Should().Be(new SlideShowSettingsState(
            UseSlideTimings: false,
            ShowWithAnimation: false,
            LoopUntilStopped: true,
            ShowType: PresentationShowType.BrowsedByIndividual,
            ShowBrowseScrollbar: false,
            KioskRestartAfterMilliseconds: 12_000,
            ShowWithNarration: false,
            ShowMediaControls: false,
            ShowMasterShapes: false));

        editor.Undo();
        editor.Presentation.UseSlideTimings.Should().BeTrue();
        editor.Presentation.ShowWithAnimation.Should().BeTrue();
        editor.Presentation.LoopUntilStopped.Should().BeFalse();
        editor.Presentation.ShowType.Should().Be(PresentationShowType.PresentedBySpeaker);
        editor.Presentation.ShowBrowseScrollbar.Should().BeTrue();
        editor.Presentation.KioskRestartAfterMilliseconds.Should().BeNull();
        editor.Presentation.ShowWithNarration.Should().BeTrue();
        editor.Presentation.ShowMediaControls.Should().BeTrue();
        editor.Presentation.ShowMasterShapes.Should().BeTrue();
    }

    [StaFact]
    public void SetupDialog_ThrowsOnNullEditor()
    {
        var act = () => new SlideShowSettingsDialog(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // r143 F2 (freep-slideshow-presenter): "Browsed at a kiosk" must force
    // loop-until-stopped the way PowerPoint does, so an unattended kiosk show never
    // ends and exposes the editor -- even if the user leaves the independent "Loop
    // until stopped" checkbox unchecked.
    [StaFact]
    public void SetupDialog_KioskShowType_ForcesLoopUntilStoppedEvenWhenCheckboxUnchecked()
    {
        var editor = MakeSession();
        var dialog = new SlideShowSettingsDialog(editor);

        dialog.ApplyForTests(
            useSlideTimings: true,
            showWithAnimation: true,
            loopUntilStopped: false,
            showType: PresentationShowType.BrowsedAtKiosk).Should().BeTrue();

        editor.Presentation.ShowType.Should().Be(PresentationShowType.BrowsedAtKiosk);
        editor.Presentation.LoopUntilStopped.Should().BeTrue(
            "PowerPoint always loops a kiosk show until 'Esc', regardless of the loop checkbox");
        dialog.LastCommitPlan!.Settings.LoopUntilStopped.Should().BeTrue();

        // The live loop checkbox itself must reflect and lock the forced state: it
        // shows checked, and the user cannot uncheck it while kiosk is selected.
        var loopCheck = GetField<CheckBox>(dialog, "_loopCheck");
        loopCheck.IsChecked.Should().BeTrue();
        loopCheck.IsEnabled.Should().BeFalse("the user cannot turn looping off in kiosk mode");
    }

    // Sibling test: non-kiosk show types must NOT be force-looped -- the checkbox
    // keeps working normally for "Presented by a speaker" / "Browsed by an individual".
    [StaFact]
    public void SetupDialog_NonKioskShowType_LeavesLoopUntilStoppedUntouched()
    {
        var editor = MakeSession();
        var dialog = new SlideShowSettingsDialog(editor);

        dialog.ApplyForTests(
            useSlideTimings: true,
            showWithAnimation: true,
            loopUntilStopped: false,
            showType: PresentationShowType.BrowsedByIndividual).Should().BeTrue();

        editor.Presentation.ShowType.Should().Be(PresentationShowType.BrowsedByIndividual);
        editor.Presentation.LoopUntilStopped.Should().BeFalse();
        dialog.LastCommitPlan!.Settings.LoopUntilStopped.Should().BeFalse();

        var loopCheck = GetField<CheckBox>(dialog, "_loopCheck");
        loopCheck.IsEnabled.Should().BeTrue("the loop checkbox stays user-editable outside kiosk mode");
    }

    private static EditingSession MakeSession()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static T GetField<T>(SlideShowSettingsDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(SlideShowSettingsDialog).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(dialog)!;
    }
}
