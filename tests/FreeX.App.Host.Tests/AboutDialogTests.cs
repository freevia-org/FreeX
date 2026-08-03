using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class AboutDialogTests
{
    [Fact]
    public void Dialog_ExposesCopyableAboutTextWithStableAutomationAndDefaultClose()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new AboutDialog();

            dialog.Title.Should().Be("About FreeX");
            dialog.ShowInTaskbar.Should().BeFalse();
            AutomationProperties.GetAutomationId(dialog).Should().Be("AboutFreeXDialog");
            AutomationProperties.GetName(dialog).Should().Be("About FreeX");

            var textBox = WpfTestTree.FindLogicalDescendants<TextBox>(dialog)
                .Single(box => AutomationProperties.GetAutomationId(box) == "AboutFreeXText");
            textBox.IsReadOnly.Should().BeTrue();
            textBox.Text.Should().Be(AppInfo.AboutText);
            AutomationProperties.GetName(textBox).Should().Be("About FreeX");

            var okButton = WpfTestTree.FindLogicalDescendants<Button>(dialog)
                .Single(button => AutomationProperties.GetAutomationId(button) == "AboutFreeXOkButton");
            okButton.IsDefault.Should().BeTrue();
            okButton.IsCancel.Should().BeTrue();
            AutomationProperties.GetName(okButton).Should().Be("OK");
            AutomationProperties.GetAcceleratorKey(okButton).Should().Be("Alt+O");
        });
    }

    [Fact]
    public void ParityCapture_UsesSharedAboutClientGeometry()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ParityCapture.cs");

        source.Should().Contain("\"dialog.About\" => (AboutDialogMetrics.Width, AboutDialogMetrics.Height)");
    }
}
