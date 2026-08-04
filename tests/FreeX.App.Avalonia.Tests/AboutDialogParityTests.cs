using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Free.Shared.Shell;
using FreeX.App.Avalonia;

namespace FreeX.App.Avalonia.Tests;

public sealed class AboutDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task About_uses_shared_Wpf_authority_geometry_content_and_modal_buttons()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new AboutDialog();

            dialog.Title.Should().Be("About FreeX");
            dialog.Width.Should().Be(AboutDialogMetrics.Width);
            dialog.Height.Should().Be(AboutDialogMetrics.Height);
            dialog.MinWidth.Should().Be(AboutDialogMetrics.MinWidth);
            dialog.MinHeight.Should().Be(AboutDialogMetrics.MinHeight);
            AutomationProperties.GetAutomationId(dialog).Should().Be("AboutFreeXDialog");

            var text = dialog.GetLogicalDescendants().OfType<TextBox>()
                .Single(textBox => AutomationProperties.GetAutomationId(textBox) == "AboutFreeXText");
            text.IsReadOnly.Should().BeTrue();
            text.FontSize.Should().Be(AboutDialogMetrics.AvaloniaTextFontSize);
            AboutDialogMetrics.AvaloniaTextLineHeight.Should().Be(16.75);
            text.Padding.Should().Be(new Thickness(
                AboutDialogMetrics.AvaloniaTextPaddingLeft,
                AboutDialogMetrics.AvaloniaTextPaddingTop,
                AboutDialogMetrics.AvaloniaTextPaddingRight,
                AboutDialogMetrics.TextPadding));
            text.LineHeight.Should().Be(AboutDialogMetrics.AvaloniaTextLineHeight);
            text.Text.Should().Contain("A free spreadsheet app for XLSX editing");
            text.Text.Should().Contain("Built with .NET 10, Avalonia, ClosedXML.");
            text.Text.Should().Contain("Help > Legal Notices");

            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            buttons.Should().ContainSingle(button => button.IsDefault);
            buttons.Should().ContainSingle(button => button.IsCancel);
            AutomationProperties.GetAutomationId(buttons.Single())
                .Should().Be("AboutFreeXOkButton");
        }, CancellationToken.None);
    }
}
