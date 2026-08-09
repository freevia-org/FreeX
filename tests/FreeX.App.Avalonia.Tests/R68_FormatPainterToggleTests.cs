using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Interactivity;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R68-commands-format-painter-6-2: <c>MainWindow.FormatPainterButton_Click</c> always re-captured a
/// new Format Painter source from the current selection, even when the painter was already armed --
/// so a single click after locking it (double-click) silently re-armed with a different source
/// instead of canceling, unlike Excel/the WPF host's <c>FormatPainterBtn_Click</c> (clicking the
/// already-pressed button cancels it). The fix checks <c>_session.IsFormatPainterActive</c> first
/// and cancels instead of re-capturing.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R68_FormatPainterToggleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Click_WhileAlreadyLocked_CancelsInsteadOfReArmingWithANewSource()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("FormatPainterToggleFixture");
                window.Session.SelectSheet(sheet.Id);

                var sourceCell = new CellAddress(sheet.Id, 1, 1);
                var otherCell = new CellAddress(sheet.Id, 2, 2);

                // Lock the painter (double-click equivalent: persistent = true) from A1.
                window.Session.SelectCell(sourceCell);
                var captureMethod = typeof(MainWindow).GetMethod(
                    "CaptureFormatPainterSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
                captureMethod.Invoke(window, [true]);
                window.Session.IsFormatPainterActive.Should().BeTrue("the painter must be locked on after the double-click");

                // Move the selection elsewhere, then single-click the Format Painter button again.
                window.Session.SelectCell(otherCell);
                var clickMethod = typeof(MainWindow).GetMethod(
                    "FormatPainterButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                clickMethod.Invoke(window, [null, new RoutedEventArgs()]);

                window.Session.IsFormatPainterActive.Should().BeFalse(
                    "clicking the already-armed Format Painter button must CANCEL it, not silently re-capture " +
                    "a new source from the cell the user has since moved to");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Click_WithNoActivePainter_StillCapturesTheSource()
    {
        // Sibling no-regression check: the very first click (no painter active yet) must still
        // capture the current selection as the single-shot source, exactly as before the fix.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("FormatPainterFirstClickFixture");
                window.Session.SelectSheet(sheet.Id);
                window.Session.SelectCell(new CellAddress(sheet.Id, 3, 3));

                window.Session.IsFormatPainterActive.Should().BeFalse();

                var clickMethod = typeof(MainWindow).GetMethod(
                    "FormatPainterButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
                clickMethod.Invoke(window, [null, new RoutedEventArgs()]);

                window.Session.IsFormatPainterActive.Should().BeTrue(
                    "a first click with no active painter must still capture the current selection as the source");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
