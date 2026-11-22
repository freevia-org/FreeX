using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF FormatCellsDialog parity: the Number "Type" drop-down is editable (a custom code is typed straight
/// into it) and the four Border preview buttons carry "Apply &lt;side&gt; border" tooltips.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FormatCellsTypedCodeAndBorderTooltipTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task FormatCellsNumberType_IsEditableAndAppliesTypedCustomCode()
    {
        const string typedCode = "0.000\" units\"";
        FormatCellsCompactDialogPlan? result = null;
        var editable = false;

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                result = await window.ShowFormatCellsInputDialogAsync(probe =>
                {
                    editable = probe.NumberFormatBox.IsEditable;
                    probe.NumberCategoryList.SelectedItem = "Custom";
                    probe.NumberFormatBox.Text = typedCode;
                    probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);

        editable.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Request.NumberFormat.Should().Be(typedCode);
    }

    [Fact]
    public async Task FormatCellsBorderTab_PreviewButtonsCarryApplySideBorderTooltips()
    {
        var tips = new System.Collections.Generic.Dictionary<string, object?>();

        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                window.UpdateLayout();

                await window.ShowFormatCellsInputDialogAsync(probe =>
                {
                    probe.TabStrip.SelectedIndex = 3;
                    probe.Dialog.UpdateLayout();
                    foreach (var side in new[] { "Top", "Bottom", "Left", "Right" })
                    {
                        var toggle = probe.Dialog.GetVisualDescendants()
                            .OfType<ToggleButton>()
                            .First(control => AutomationProperties.GetAutomationId(control) == $"FormatCellsBorder{side}Toggle");
                        tips[side] = ToolTip.GetTip(toggle);
                    }

                    probe.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);

        tips["Top"].Should().Be("Apply top border");
        tips["Bottom"].Should().Be("Apply bottom border");
        tips["Left"].Should().Be("Apply left border");
        tips["Right"].Should().Be("Apply right border");
    }
}
