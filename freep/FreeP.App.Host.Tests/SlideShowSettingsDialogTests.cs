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
