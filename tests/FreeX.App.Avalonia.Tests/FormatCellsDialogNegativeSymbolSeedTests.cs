using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for G19 (HIGH, data loss): the Avalonia Format Cells dialog's Number tab
/// always seeded the "Negative numbers" list to index 0 (plain, no red/parentheses) and the
/// currency "Symbol" combo to "$", regardless of the cell's actual current format. Opening
/// Format Cells (Ctrl+1) on a cell already formatted with a real Currency preset that uses a
/// red/parenthesized negative style and clicking OK with zero edits would silently strip the
/// negative styling, because the resolved format (rebuilt from the wrongly-seeded index 0)
/// would then differ from the cell's original format and the OK handler's "did the user change
/// anything" guard would treat it as a real edit.
///
/// The fix derives the seeded negative-style index (and currency symbol) from the cell's actual
/// format code (mirroring the existing Custom-code seeding fix beside it - see F10), so a
/// no-edit open+OK round-trips the original format unchanged, including after the user browses
/// away to another Category and back.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FormatCellsDialogNegativeSymbolSeedTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCells_NoEditOk_PreservesCurrencyRedParenthesesNegativeStyle()
    {
        const string currencyRedParens = "$#,##0.00;[Red]($#,##0.00)";
        await AssertNoEditOkRoundTripsFormat(currencyRedParens);
    }

    [Fact]
    public async Task FormatCells_NoEditOk_PreservesCurrencyRedParenthesesNegativeStyle_ZeroDecimals()
    {
        const string currencyRedParensNoDecimals = "$#,##0;[Red]($#,##0)";
        await AssertNoEditOkRoundTripsFormat(currencyRedParensNoDecimals);
    }

    [Fact]
    public async Task FormatCells_NoEditOk_AfterBrowsingToAnotherCategoryAndBack_StillPreservesNegativeStyle()
    {
        const string currencyRedParens = "$#,##0.00;[Red]($#,##0.00)";
        FormatCellsCompactDialogPlan? result = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                window.Session.SetSelectedRangeNumberFormat(currencyRedParens);
                window.Session.SelectedRangeStartNumberFormat.Should().Be(currencyRedParens);

                var dialogTask = window.ShowFormatCellsInputDialogAsync(probe =>
                {
                    // Browse to "Number" and back to "Currency" (the cell's original category)
                    // before accepting - this exercises SyncSymbolAndNegativeFromCategory's
                    // re-seeding, which must restore the cell's actual negative style rather than
                    // leaving behind the "Number" category's default (index 0).
                    probe.NumberCategoryList.SelectedItem = "Number";
                    probe.NumberCategoryList.SelectedItem = "Currency";
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

        result.Should().NotBeNull();
        var appliedFormat = result!.Request.NumberFormat;
        if (appliedFormat is not null)
            appliedFormat.Should().Be(currencyRedParens);
    }

    private static async Task AssertNoEditOkRoundTripsFormat(string originalFormat)
    {
        FormatCellsCompactDialogPlan? result = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                // Arrange: give the active cell (A1) a real Currency preset format (see
                // FormatCellsNumberFormatPlanner.Options) with a red/parenthesized negative style.
                window.Session.SetSelectedRangeNumberFormat(originalFormat);
                window.Session.SelectedRangeStartNumberFormat.Should().Be(originalFormat);

                // Act: open the Format Cells dialog and immediately click OK with no edits.
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
        // carry the identical original code - never a coerced format that drops the negative
        // styling.
        result.Should().NotBeNull();
        var appliedFormat = result!.Request.NumberFormat;
        if (appliedFormat is not null)
            appliedFormat.Should().Be(originalFormat);
    }
}
