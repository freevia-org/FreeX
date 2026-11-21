using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

using Free.Shared.Shell;
using FreeX.App.Presentation;
using FreeX.App.Avalonia.Ribbon;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// The status bar tooltips, automation names/help text/ids and the click-to-open Zoom dialog that the WPF
/// host's MainWindow.xaml / MainWindow.ViewCommands.cs define for the view buttons, zoom buttons, zoom
/// slider and zoom percentage.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaStatusBarWpfInteractionParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task StatusBarControlsCarryWpfNamesHelpTextAndTooltips()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                var zoomIn = FindById<Button>(window, "StatusZoomInButton");
                var zoomOut = FindById<Button>(window, "StatusZoomOutButton");
                AutomationProperties.GetName(zoomIn).Should().Be(UiText.Get("MainWindow_AutomationName_ZoomIn"));
                AutomationProperties.GetName(zoomOut).Should().Be(UiText.Get("MainWindow_AutomationName_ZoomOut"));
                AutomationProperties.GetHelpText(zoomIn)
                    .Should().Be(UiText.Get("MainWindow_AutomationHelpText_IncreaseWorksheetZoom"));
                AutomationProperties.GetHelpText(zoomOut)
                    .Should().Be(UiText.Get("MainWindow_AutomationHelpText_DecreaseWorksheetZoom"));
                ToolTip.GetTip(zoomIn).Should().NotBeNull();
                ToolTip.GetTip(zoomOut).Should().NotBeNull();

                var normal = FindById<ToggleButton>(window, "StatusNormalViewButton");
                AutomationProperties.GetName(normal).Should().Be(UiText.Get("MainWindow_Content_Normal"));
                AutomationProperties.GetHelpText(normal)
                    .Should().Be(UiText.Get("MainWindow_TooltipDescription_ShowTheWorksheetInTheStandardEditingView"));
                ToolTip.GetTip(normal).Should().Be(UiText.Get("MainWindow_TooltipTitle_Normal"));
                ToolTip.GetTip(FindById<ToggleButton>(window, "StatusPageLayoutViewButton"))
                    .Should().Be(UiText.Get("MainWindow_TooltipTitle_PageLayout"));
                ToolTip.GetTip(FindById<ToggleButton>(window, "StatusPageBreakPreviewButton"))
                    .Should().Be(UiText.Get("MainWindow_TooltipTitle_PageBreakPreview"));

                var zoomText = FindById<TextBlock>(window, "StatusZoomText");
                ToolTip.GetTip(zoomText).Should().Be(UiText.Get("MainWindow_ToolTip_Zoom"));
                AutomationProperties.GetName(zoomText).Should().Be(UiText.Get("MainWindow_AutomationName_ZoomPercentage"));
                AutomationProperties.GetHelpText(zoomText).Should().Be(UiText.Get("MainWindow_AutomationHelpText_OpenTheZoomDialog"));
                ToolTip.GetTip(FindById<Slider>(window, "ZoomSlider")).Should().Be(UiText.Get("MainWindow_ToolTip_Zoom"));
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
    public async Task ClickingTheZoomPercentageOpensTheZoomDialog()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                var zoomText = FindById<TextBlock>(window, "StatusZoomText");
                var point = zoomText.TranslatePoint(new Point(zoomText.Bounds.Width / 2, zoomText.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point, RawInputModifiers.None);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Window? dialog = null;
                for (var i = 0; i < 40 && dialog is null; i++)
                {
                    await Task.Delay(50);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    dialog = window.OwnedWindows.SingleOrDefault(
                        owned => AutomationProperties.GetAutomationId(owned) == ZoomDialogVisualMetrics.DialogAutomationId);
                }

                dialog.Should().NotBeNull("clicking the zoom percentage opens the Zoom dialog like WPF");
                dialog!.Close();
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
    public async Task FormulaBarButtonsCarryRibbonTooltipsAndTheSheetTabMenuHasNoAvaloniaOnlyRows()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                foreach (var id in new[] { "FormulaBarCancelButton", "FormulaBarEnterButton", "FormulaBarFxButton", "FormulaBarExpandBtn" })
                {
                    var button = FindById<Button>(window, id);
                    ToolTip.GetTip(button).Should().BeAssignableTo<Control>($"{id} shows the WPF ribbon-style tooltip");
                    button.Cursor.Should().NotBeNull($"{id} has the WPF hand cursor");
                }

                var headers = window.GetVisualDescendants().OfType<Control>()
                    .Select(control => control.ContextMenu)
                    .Where(menu => menu is not null)
                    .SelectMany(menu => menu!.Items.OfType<MenuItem>())
                    .Select(item => item.Header as string)
                    .ToList();
                headers.Should().Contain(UiText.Get("OutlineSettings_MenuItem"));
                headers.Should().NotContain(UiText.Get("MainWindow_Header_Duplicate"));
                headers.Should().NotContain(UiText.Get("MainWindow_Header_MoveLeft"));
                headers.Should().NotContain(UiText.Get("MainWindow_Header_MoveRight"));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static T FindById<T>(MainWindow window, string automationId) where T : Control =>
        window.GetVisualDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);
}
