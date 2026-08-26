using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;
using FluentAssertions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Functional safety net for the three editable Page Layout Scale-to-Fit combos (Scale Width,
/// Scale Height, Scale Percent) on the DECLARATIVE ribbon. These tests drive the actual rendered
/// combo control resolved via <c>FindName</c> (which the declarative swap re-points at the on-screen
/// control) — typing a value and raising the commit event must apply the corresponding scale-to-fit
/// setting to the active sheet. Before the host-side wiring existed the rendered combo had no
/// SelectionChanged/commit handlers, so these assertions failed even though the displayed text synced.
/// </summary>
public sealed class MainWindowRenderedPageLayoutComboTests
{
    private static void PressEnter(MainWindow window, ComboBox combo)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        combo.RaiseEvent(args);
    }

    [Fact]
    public void RenderedScaleWidthCombo_CommitText_AppliesFitToPagesWide()
    {
        ReusableFreeXMainWindowSession.Run((window, workbookRef) =>
        {
            var workbook = workbookRef.Current;
            var sheet = workbook.GetSheetAt(0);

            var widthBox = (ComboBox)window.FindName("PageLayoutScaleWidthBox");

            widthBox.Text = "1 page";
            PressEnter(window, widthBox);
            PumpDispatcher();

            workbook.GetSheet(sheet.Id)!.ScaleToFit.FitToPagesWide.Should().Be(1);
        });
    }
}
