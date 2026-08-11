using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class PivotValueFieldSettingsNumberFormatInteractionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PivotNumberFormatDialog_AcceptedFormatReturnsNumberCodeWithoutApplyingWorksheetFormat()
    {
        FormatCellsCompactDialogPlan? result = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var dialogTask = window.ShowPivotNumberFormatInputDialogAsync("General", probe =>
                {
                    probe.NumberCategoryList.SelectedItem = "Currency";
                    probe.NumberFormatBox.SelectedItem = "$#,##0.00";
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
        result!.Request.NumberFormat.Should().Be("$#,##0.00");
    }

    [Fact]
    public async Task PivotNumberFormatDialog_CancelReturnsNoFormatChange()
    {
        FormatCellsCompactDialogPlan? result = null;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                var dialogTask = window.ShowPivotNumberFormatInputDialogAsync("$#,##0.00", probe =>
                {
                    probe.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                result = await dialogTask;
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);

        result.Should().BeNull();
    }
}
