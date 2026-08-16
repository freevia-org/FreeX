using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Localization;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// STA tests for the WPF slide-size dialog shell. Shared parsing and validation
/// policy is covered by SlideSizeDialogPlannerTests.
/// </summary>
public sealed class SlideSizeDialogTests
{
    private static EditingSession MakeSession(long cxEmu = 12_192_000L, long cyEmu = 6_858_000L)
    {
        var pres = new Presentation
        {
            SlideSizeCxEmu = cxEmu,
            SlideSizeCyEmu = cyEmu
        };
        pres.Slides.Add(new Slide());
        var bus = new PresentationCommandBus(pres);
        return new EditingSession(pres, bus);
    }

    [StaFact]
    public void SlideSizeDialog_Constructs_WithEditor()
    {
        var sess = MakeSession();
        var dlg = new SlideSizeDialog(sess);
        dlg.Should().NotBeNull();
    }

    [StaFact]
    public void SlideSizeDialog_ThrowsOnNullEditor()
    {
        var act = () => new SlideSizeDialog(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [StaFact]
    public void SlideSizeDialog_LoadsCurrentWidescreenState()
    {
        var dlg = new SlideSizeDialog(MakeSession());

        GetField<ComboBox>(dlg, "_presetCombo").SelectedIndex.Should().Be(1);
        GetField<TextBox>(dlg, "_widthBox").Text.Should().Be("13.333");
        GetField<TextBox>(dlg, "_heightBox").Text.Should().Be("7.500");
        GetField<Label>(dlg, "_widthUnitLabel").Content.Should().Be("in");
        GetField<Label>(dlg, "_heightUnitLabel").Content.Should().Be("in");
    }

    [StaFact]
    public void SlideSizeDialog_UsesSharedLocalizedButtonRow()
    {
        AppLocalization.Bootstrap.InstallSharedSeams();

        var dlg = new SlideSizeDialog(MakeSession());
        var buttons = FindButtons((DependencyObject)dlg.Content);

        var ok = buttons.Single(button => Equals(button.Content, LocalizedUiText.Ok));
        ok.MinWidth.Should().Be(80);
        ok.IsDefault.Should().BeTrue();
        // The session plan supplies a descriptive accessible name per action, which is what a
        // screen reader should announce -- "Apply slide size" tells the user what OK does. The
        // localized label stays on Content; only the announced name is the richer string.
        AutomationProperties.GetName(ok).Should().Be("Apply slide size");
        AutomationProperties.GetAcceleratorKey(ok).Should().Be("Alt+O");

        var cancel = buttons.Single(button => Equals(button.Content, LocalizedUiText.Cancel));
        cancel.MinWidth.Should().Be(80);
        cancel.IsCancel.Should().BeTrue();
        AutomationProperties.GetName(cancel).Should().Be("Cancel slide size changes");
        AutomationProperties.GetAcceleratorKey(cancel).Should().Be("Alt+C");
    }

    [StaFact]
    public void SlideSizeDialog_PresetSelection_RefreshesDisplayedSize()
    {
        var dlg = new SlideSizeDialog(MakeSession());
        var presetCombo = GetField<ComboBox>(dlg, "_presetCombo");

        presetCombo.SelectedIndex = 0;

        GetField<TextBox>(dlg, "_widthBox").Text.Should().Be("10.000");
        GetField<TextBox>(dlg, "_heightBox").Text.Should().Be("7.500");
    }

    [StaFact]
    public void SlideSizeDialog_UnitSelection_ConvertsCurrentFields()
    {
        var dlg = new SlideSizeDialog(MakeSession());

        GetField<RadioButton>(dlg, "_inchesRadio").IsChecked = false;
        GetField<RadioButton>(dlg, "_cmRadio").IsChecked = true;

        GetField<TextBox>(dlg, "_widthBox").Text.Should().Be("33.87");
        GetField<TextBox>(dlg, "_heightBox").Text.Should().Be("19.05");
        GetField<Label>(dlg, "_widthUnitLabel").Content.Should().Be("cm");
        GetField<Label>(dlg, "_heightUnitLabel").Content.Should().Be("cm");
    }

    [StaFact]
    public void SlideSizeDialog_TryParseEmu_UsesCurrentFields()
    {
        var dlg = new SlideSizeDialog(MakeSession());
        GetField<TextBox>(dlg, "_widthBox").Text = "12";
        GetField<TextBox>(dlg, "_heightBox").Text = "6.75";

        dlg.TryParseEmu(out long cxEmu, out long cyEmu).Should().BeTrue();
        cxEmu.Should().Be(10_972_800L);
        cyEmu.Should().Be(6_172_200L);
    }

    [StaFact]
    public void SlideSizeDialog_SetSizeIsUndoable()
    {
        var sess = MakeSession(9_144_000L, 6_858_000L);

        sess.SetSlideSize(10_000_000L, 5_000_000L);
        sess.Undo();

        sess.Presentation.SlideSizeCxEmu.Should().Be(9_144_000L);
        sess.Presentation.SlideSizeCyEmu.Should().Be(6_858_000L);
    }

    private static T GetField<T>(SlideSizeDialog dialog, string fieldName)
        where T : class
    {
        var field = typeof(SlideSizeDialog).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(dialog)!;
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
