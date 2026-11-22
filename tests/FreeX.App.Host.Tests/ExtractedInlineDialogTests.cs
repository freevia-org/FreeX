using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Services;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Pins the extraction of the two formerly inline MainWindow dialogs (merge-cells content warning and
/// Outline Settings) into standalone window classes: production call sites must construct the classes,
/// MainWindow must not rebuild the windows inline, and the classes must keep the exact window chrome,
/// automation ids and button defaults the inline versions had.
/// </summary>
public sealed class ExtractedInlineDialogTests
{
    [Fact]
    public void MainWindow_ConstructsExtractedDialogs_InsteadOfBuildingInlineWindows()
    {
        var formatting = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var sheetTabs = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");

        formatting.Should().Contain("new MergeCellsContentWarningDialog(");
        formatting.Should().Contain("dialog.Choice");
        formatting.Should().NotContain("MergeCellsContentWarningPlanner.Create(");
        formatting.Should().NotContain("presentation.DialogAutomationId");

        sheetTabs.Should().Contain("new OutlineSettingsDialog(");
        sheetTabs.Should().Contain("dialog.AcceptedState");
        sheetTabs.Should().NotContain("OutlineSettingsDialog\");");
        sheetTabs.Should().NotContain("\"OutlineSettings_Title\"");
        sheetTabs.Should().NotContain("\"OutlineSettingsOkButton\"");
    }

    [Fact]
    public void ExtractedDialogs_KeepOwnerAndStartupLocationContract()
    {
        var formatting = DialogSourceTestSupport.ReadHostSources("MainWindow.HomeFormatting.cs");
        var sheetTabs = DialogSourceTestSupport.ReadHostSources("MainWindow.SheetTabs.cs");
        var mergeDialog = DialogSourceTestSupport.ReadHostSources("MergeCellsContentWarningDialog.cs");
        var outlineDialog = DialogSourceTestSupport.ReadHostSources("OutlineSettingsDialog.cs");

        formatting.Should().Contain("Owner = this");
        sheetTabs.Should().Contain("new OutlineSettingsDialog(current) { Owner = this }");
        mergeDialog.Should().Contain("WindowStartupLocation = WindowStartupLocation.CenterOwner");
        mergeDialog.Should().Contain("ResizeMode = ResizeMode.NoResize");
        outlineDialog.Should().Contain("WindowStartupLocation = WindowStartupLocation.CenterOwner");
        outlineDialog.Should().Contain("ResizeMode = ResizeMode.NoResize");
        outlineDialog.Should().Contain("ShowInTaskbar = false");
        outlineDialog.Should().Contain("SizeToContent = SizeToContent.Height");
    }

    [Fact]
    public void MergeCellsContentWarningDialog_KeepsWindowChromeIdsAndButtonDefaults()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new MergeCellsContentWarningDialog(["North", "Widget"]);
            var presentation = MergeCellsContentWarningPlanner.Create(["North", "Widget"]);

            dialog.Title.Should().Be(presentation.Title);
            dialog.Width.Should().Be(460);
            dialog.Height.Should().Be(240);
            dialog.ResizeMode.Should().Be(ResizeMode.NoResize);
            dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);
            AutomationProperties.GetAutomationId(dialog).Should().Be(presentation.DialogAutomationId);
            dialog.Choice.Should().Be(MergeCellContentChoice.Cancel);

            var keepFirst = presentation.Action(MergeCellsContentWarningAction.KeepFirstCell);
            var concatenate = presentation.Action(MergeCellsContentWarningAction.ConcatenateAllCells);
            var cancel = presentation.Action(MergeCellsContentWarningAction.Cancel);
            var buttons = ((StackPanel)((StackPanel)dialog.Content).Children[^1]).Children.OfType<Button>().ToArray();

            buttons.Select(b => AutomationProperties.GetAutomationId(b))
                .Should().Equal(keepFirst.AutomationId, concatenate.AutomationId, cancel.AutomationId);
            buttons.Select(b => (string?)b.Content)
                .Should().Equal(keepFirst.Label, concatenate.Label, cancel.Label);
            buttons.Select(b => b.IsDefault).Should().Equal(keepFirst.IsDefault, false, false);
            buttons.Select(b => b.IsCancel).Should().Equal(false, false, cancel.IsCancel);
            buttons.Select(b => b.MinWidth).Should().Equal(136d, 136d, 82d);
        });
    }

    [Fact]
    public void OutlineSettingsDialog_KeepsWindowChromeIdsAndRoundTripsState()
    {
        StaTestRunner.Run(() =>
        {
            var initial = new OutlineSettingsState(SummaryBelow: true, SummaryRight: false, ApplyStyles: true);
            var dialog = new OutlineSettingsDialog(initial);

            dialog.Title.Should().Be(UiText.Get("OutlineSettings_Title"));
            dialog.Width.Should().Be(320);
            dialog.SizeToContent.Should().Be(SizeToContent.Height);
            dialog.ResizeMode.Should().Be(ResizeMode.NoResize);
            dialog.ShowInTaskbar.Should().BeFalse();
            dialog.WindowStartupLocation.Should().Be(WindowStartupLocation.CenterOwner);
            AutomationProperties.GetAutomationId(dialog).Should().Be("OutlineSettingsDialog");
            dialog.AcceptedState.Should().Be(initial);

            var root = (StackPanel)dialog.Content;
            var boxes = root.Children.OfType<CheckBox>().ToArray();
            boxes.Select(b => AutomationProperties.GetAutomationId(b)).Should().Equal(
                "OutlineSettingsSummaryBelowCheckBox",
                "OutlineSettingsSummaryRightCheckBox",
                "OutlineSettingsAutomaticStylesCheckBox");

            boxes[1].IsChecked = true;
            dialog.AcceptedState.Should().Be(new OutlineSettingsState(true, true, true));

            var buttons = ((StackPanel)root.Children[^1]).Children.OfType<Button>().ToArray();
            buttons.Select(b => AutomationProperties.GetAutomationId(b))
                .Should().Equal("OutlineSettingsOkButton", "OutlineSettingsCancelButton");
            buttons[0].IsDefault.Should().BeTrue();
            buttons[1].IsCancel.Should().BeTrue();
        });
    }
}
