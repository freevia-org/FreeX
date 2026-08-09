using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for F10 (HIGH, data loss): the Avalonia Format Cells dialog used to silently
/// overwrite any number-format code that wasn't one of the fixed presets (e.g. a custom code like
/// "0.0000") with the first preset in the "Custom" category ("General") whenever the dialog was
/// opened and OK was clicked, even with zero user edits.
///
/// The fix seeds the dialog's number-format list with the cell's ACTUAL current format code when it
/// isn't one of the presets, and selects it, so <c>ResolveSelectedNumberFormatCode()</c> round-trips
/// the original code unchanged on a no-edit OK.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FormatCellsDialogNumberFormatSeedTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCells_NoEditOk_PreservesCustomNumberFormatCode_NotCoercedToPreset()
    {
        const string customFormat = "0.0000";
        MainWindow.FormatCellsDialogResult? result = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                // Arrange: give the active cell (A1) a number-format code that is NOT one of the
                // fixed presets in FormatCellsNumberFormatPlanner.Options, so the dialog must fall
                // into the "Custom" category with no matching preset option.
                window.Session.SetSelectedRangeNumberFormat(customFormat);
                window.Session.SelectedRangeStartNumberFormat.Should().Be(customFormat);

                // Act: open the Format Cells dialog and immediately click OK with no edits, via the
                // smoke-probe hook (clicks OK, then the dialog auto-closes).
                var dialogTask = window.ShowFormatCellsInputDialogAsync(probe =>
                {
                    probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                result = await dialogTask;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);

        // Assert: the resolved request must either omit NumberFormat (no change requested) or
        // carry the identical original code - never a coerced preset like "General".
        result.Should().NotBeNull();
        var appliedFormat = result!.Request.NumberFormat;
        if (appliedFormat is not null)
            appliedFormat.Should().Be(customFormat);
    }
}
