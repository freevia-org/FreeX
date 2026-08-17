using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeW.App.Host;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Round-138 finding (B): the WPF Cell Borders color swatches (<see cref="CellBordersDialog"/>)
/// were plain <see cref="Border"/>s wired only to <c>MouseLeftButtonUp</c> -- a keyboard-only user
/// could never reach or pick a color. The fix ports the Avalonia twin's approach (already shipped)
/// of using real, focusable <see cref="Button"/> swatches. This test drives a genuine Space
/// press-then-release through the swatch's actual <see cref="ButtonBase"/> key handling (the same
/// routed events a real keyboard produces) rather than calling the private Click handler directly,
/// so it fails again if a future edit swaps the Button back for a mouse-only element.
/// </summary>
public sealed class CellBordersDialogSwatchKeyboardTests
{
    [StaFact]
    public void SpaceKeyOnAFocusableSwatchButtonSelectsItsColor_NoMouseInputAnywhere()
    {
        var dialog = CreateDialog();
        try
        {
            var swatches = Swatches(dialog);
            swatches.Count.Should().BeGreaterThan(2, "the palette needs at least 3 colors to pick a non-default one");
            foreach (var swatch in swatches)
                swatch.Focusable.Should().BeTrue("every swatch must be a keyboard-reachable tab stop");

            var target = swatches[2];
            PressAndReleaseSpace(target);

            ColorIndex(dialog).Should().Be(2);
            // The selected swatch grows a thicker outline; the previously-selected (index 0) one
            // shrinks back to the unselected thickness.
            target.BorderThickness.Left.Should().Be(2);
            swatches[0].BorderThickness.Left.Should().Be(1);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void PresetButtonsStillWorkByClick_NoRegression()
    {
        var dialog = CreateDialog();
        try
        {
            var outer = (StackPanel)dialog.Content;
            var presetButton = outer.Children.OfType<WrapPanel>().First().Children.OfType<Button>().First();
            presetButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            PresetIndex(dialog).Should().Be(0);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static CellBordersDialog CreateDialog()
    {
        var dialog = (CellBordersDialog)typeof(CellBordersDialog)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Window)], null)!
            .Invoke([null]);
        // A real PresentationSource is required to raise routed KeyEventArgs (its constructor
        // rejects a null inputSource) -- show the dialog off-screen, exactly as
        // SharedBackstageFrameTests.ArrowDown_OnRail_MovesFocusToNextNavButton does for the same
        // reason, rather than modally (Show, not ShowDialog, so the test thread is not blocked).
        dialog.WindowStyle = WindowStyle.None;
        dialog.Left = -10000;
        dialog.Top = -10000;
        dialog.Show();
        dialog.UpdateLayout();
        return dialog;
    }

    private static List<Button> Swatches(CellBordersDialog dialog)
    {
        var outer = (StackPanel)dialog.Content;
        // Declaration order in the constructor: presets WrapPanel, then the color-swatch WrapPanel.
        var colorPanel = outer.Children.OfType<WrapPanel>().ElementAt(1);
        return [.. colorPanel.Children.OfType<Button>()];
    }

    private static int ColorIndex(CellBordersDialog dialog) =>
        (int)typeof(CellBordersDialog).GetField("_colorIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static int PresetIndex(CellBordersDialog dialog) =>
        (int)typeof(CellBordersDialog).GetField("_presetIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dialog)!;

    private static void PressAndReleaseSpace(Button button)
    {
        var source = PresentationSource.FromVisual(button)
            ?? throw new InvalidOperationException("Dialog must be Shown before raising routed key events.");
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent };
        button.RaiseEvent(down);
        var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent };
        button.RaiseEvent(up);
    }
}
