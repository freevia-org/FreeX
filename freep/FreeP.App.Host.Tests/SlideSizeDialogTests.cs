using System.Reflection;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
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
}
