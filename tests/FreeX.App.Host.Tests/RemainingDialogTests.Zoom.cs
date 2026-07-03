using FluentAssertions;
using System.Windows.Controls;
using System.Windows.Input;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void ZoomDialog_TryCreateResult_AcceptsPercentWithinExcelRange()
    {
        ZoomDialog.TryCreateResult("125", out var result, out _).Should().BeTrue();

        result.Should().Be(new ZoomDialogResult(125));
    }

    [Fact]
    public void ZoomDialog_TryCreateResult_RejectsFractionalCustomPercent()
    {
        ZoomDialog.TryCreateResult("125.5", out _, out var error).Should().BeFalse();

        error.Should().Be("Zoom must be a whole percent between 10% and 400%.");
    }

    [Fact]
    public void ZoomDialog_ExposesExcelPresetPercentsAndCustomPercent()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("Width = ZoomDialogPlanner.Width");
        source.Should().Contain("Height = ZoomDialogPlanner.Height");
        source.Should().Contain("ZoomDialogPlanner.Presets");
        source.Should().Contain("ZoomDialogPlanner.IsPreset(currentZoomPercent)");
        source.Should().Contain("_fitSelectionButton");
        source.Should().Contain("UiText.Get(\"Zoom_FitSelection\")");
        source.Should().Contain("_customZoomButton");
        source.Should().Contain("_zoomBox");
    }

    [Fact]
    public void ZoomDialog_CustomPercentBoxExposesAutomationName()
    {
        var source = ReadClassSource("ZoomDialog.cs", "public sealed class ZoomDialog", "public sealed record __NoNextZoomDialog");

        source.Should().Contain("AutomationProperties.SetName(_zoomBox, UiText.Get(\"Zoom_CustomZoomPercent\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_zoomBox, \"ZoomCustomPercentBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_zoomBox, UiText.Get(\"Zoom_EnterAWholeZoomPercentageFrom10To400\"));");
    }

    [Fact]
    public void ZoomDialogOpenedFromKeyboard_FocusesPresetOrCustomZoomChoice()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("RadioButton? checkedPreset = null;");
        source.Should().Contain("foreach (var button in _presetButtons)");
        source.Should().Contain("if (button.IsChecked != true)");
        source.Should().Contain("checkedPreset = button;");
        source.Should().Contain("if (checkedPreset is not null)");
        source.Should().Contain("checkedPreset.Focus();");
        source.Should().Contain("Keyboard.Focus(checkedPreset);");
        source.Should().Contain("else");
        source.Should().Contain("DialogFocus.FocusAndSelect(_zoomBox);");
    }

    [Fact]
    public void ZoomDialogOpenedWithCustomPercent_FocusesAndSelectsCustomPercent()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ZoomDialog(125);
            try
            {
                dialog.Show();
                PumpDispatcher();

                var customButton = GetField<RadioButton>(dialog, "_customZoomButton");
                var zoomBox = GetField<TextBox>(dialog, "_zoomBox");

                customButton.IsChecked.Should().BeTrue();
                Keyboard.FocusedElement.Should().BeSameAs(zoomBox);
                zoomBox.Text.Should().Be("125");
                zoomBox.SelectionStart.Should().Be(0);
                zoomBox.SelectionLength.Should().Be(zoomBox.Text.Length);
            }
            finally
            {
                dialog.Close();
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void ZoomDialogCustomPercentFocus_SelectsCustomChoiceOverPreset()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ZoomDialog(100);
            try
            {
                dialog.Show();
                PumpDispatcher();

                var customButton = GetField<RadioButton>(dialog, "_customZoomButton");
                var zoomBox = GetField<TextBox>(dialog, "_zoomBox");

                customButton.IsChecked.Should().BeFalse();
                zoomBox.Focus();
                Keyboard.Focus(zoomBox);
                PumpDispatcher();

                customButton.IsChecked.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
                PumpDispatcher();
            }
        });
    }

    [Fact]
    public void ZoomDialog_InvalidCustomInput_ShowsParserErrorAndRefocusesEntry()
    {
        var source = ReadRemainingDialogSources();

        source.Should().Contain("TryCreateResult(input, out var result, out var error)");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, error ?? UiText.Get(\"Zoom_EnterAValidZoomPercent\")");
        source.Should().Contain("_customZoomButton.IsChecked = true");
        source.Should().Contain("DialogFocus.FocusAndSelect(_zoomBox);");
    }

    [Fact]
    public void ZoomDialog_CreateFitSelectionResult_RequestsFitSelectionWithoutChangingPercent()
    {
        ZoomDialog.CreateFitSelectionResult(125)
            .Should()
            .Be(new ZoomDialogResult(125, FitSelection: true));
    }
}
