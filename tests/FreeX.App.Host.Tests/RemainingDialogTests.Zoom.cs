using FluentAssertions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var captureSource = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("Width = ZoomDialogPlanner.Width");
        source.Should().Contain("Height = ZoomDialogPlanner.Height");
        source.Should().Contain("ZoomDialogPlanner.Presets");
        source.Should().Contain("ZoomDialogPlanner.IsPreset(currentZoomPercent)");
        source.Should().Contain("_fitSelectionButton");
        source.Should().Contain("UiText.Get(\"Zoom_FitSelection\")");
        source.Should().Contain("_customZoomButton");
        source.Should().Contain("_zoomBox");
        captureSource.Should().Contain("\"dialog.Zoom\" => (ZoomDialogPlanner.Width, ZoomDialogPlanner.Height)");
    }

    [Fact]
    public void ZoomDialog_ParityCapture_RendersCompleteClientFrame()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new ZoomDialog(100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -10000,
                Top = -10000,
            };
            dialog.Show();
            try
            {
                var bitmap = ParityCapture.RenderDialogClientFrameForTest(
                    dialog,
                    ZoomDialogPlanner.Width,
                    ZoomDialogPlanner.Height);
                var content = dialog.Content.Should().BeAssignableTo<FrameworkElement>().Subject;
                var group = WpfTestTree.FindVisualDescendants<GroupBox>(content).Single();
                var buttons = WpfTestTree.FindVisualDescendants<Button>(content).ToArray();
                var groupBounds = group.TransformToAncestor(content).TransformBounds(new Rect(group.RenderSize));
                var buttonBounds = buttons
                    .Select(button =>
                    {
                        var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                        bounds.Offset(content.Margin.Left, content.Margin.Top);
                        return bounds;
                    })
                    .ToArray();
                groupBounds.Offset(content.Margin.Left, content.Margin.Top);

                (content.ActualWidth + content.Margin.Left + content.Margin.Right)
                    .Should().BeApproximately(ZoomDialogPlanner.Width, 0.25);
                (content.ActualHeight + content.Margin.Top + content.Margin.Bottom)
                    .Should().BeApproximately(ZoomDialogPlanner.Height, 0.25);
                groupBounds.Right.Should().BeLessThanOrEqualTo(ZoomDialogPlanner.Width);
                buttonBounds.Should().HaveCount(2);
                buttonBounds.Should().OnlyContain(bounds => bounds.Right <= ZoomDialogPlanner.Width);
                bitmap.PixelWidth.Should().Be((int)ZoomDialogPlanner.Width);
                bitmap.PixelHeight.Should().Be((int)ZoomDialogPlanner.Height);
                CountNonWhitePixels(bitmap, new Rect(groupBounds.Right - 2, groupBounds.Top, 3, groupBounds.Height))
                    .Should().BeGreaterThan(
                        80,
                        "the complete Magnification group border must be captured; content {0}, group {1}, buttons {2}",
                        content.RenderSize,
                        groupBounds,
                        string.Join("; ", buttonBounds.Select(bounds => bounds.ToString())));
                foreach (var bounds in buttonBounds)
                {
                    CountNonWhitePixels(bitmap, bounds).Should().BeGreaterThan(
                        100,
                        "both action buttons must be fully painted inside the client frame");
                }
            }
            finally
            {
                dialog.Close();
                PumpDispatcher();
            }
        });
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

    private static int CountNonWhitePixels(BitmapSource bitmap, Rect bounds)
    {
        var x = Math.Max(0, (int)Math.Floor(bounds.X));
        var y = Math.Max(0, (int)Math.Floor(bounds.Y));
        var right = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(bounds.Right));
        var bottom = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(bounds.Bottom));
        var width = Math.Max(0, right - x);
        var height = Math.Max(0, bottom - y);
        if (width == 0 || height == 0)
            return 0;

        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new Int32Rect(x, y, width, height), pixels, width * 4, 0);
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] < 250 || pixels[offset + 1] < 250 || pixels[offset + 2] < 250)
                count++;
        }

        return count;
    }
}
