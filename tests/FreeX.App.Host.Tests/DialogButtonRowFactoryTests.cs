using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DialogButtonRowFactoryTests
{
    [Fact]
    public void Create_BuildsRightAlignedOkCancelRowWithProvidedMetrics()
    {
        StaTestRunner.Run(() =>
        {
            var accepted = 0;

            var row = DialogButtonRowFactory.Create(
                () => accepted++,
                buttonWidth: 72,
                rowMargin: new Thickness(0, 12, 0, 0));

            row.Orientation.Should().Be(Orientation.Horizontal);
            row.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
            row.Margin.Should().Be(new Thickness(0, 12, 0, 0));
            row.Children.Count.Should().Be(2);

            var ok = row.Children[0].Should().BeOfType<Button>().Subject;
            ok.Content.Should().Be("_OK");
            ok.Width.Should().Be(72);
            ok.Margin.Should().Be(new Thickness(0, 0, 8, 0));
            ok.IsDefault.Should().BeTrue();
            AutomationProperties.GetName(ok).Should().Be("OK");
            AutomationProperties.GetAcceleratorKey(ok).Should().Be("Alt+O");

            var cancel = row.Children[1].Should().BeOfType<Button>().Subject;
            cancel.Content.Should().Be("_Cancel");
            cancel.Width.Should().Be(72);
            cancel.IsCancel.Should().BeTrue();
            AutomationProperties.GetName(cancel).Should().Be("Cancel");
            AutomationProperties.GetAcceleratorKey(cancel).Should().Be("Alt+C");

            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            accepted.Should().Be(1);
        });
    }

    [Fact]
    public void CreateOkOnly_BuildsSingleDefaultClosingButton()
    {
        StaTestRunner.Run(() =>
        {
            var accepted = 0;

            var row = DialogButtonRowFactory.CreateOkOnly(
                () => accepted++,
                buttonWidth: 76,
                rowMargin: new Thickness(0, 8, 0, 0));

            row.Orientation.Should().Be(Orientation.Horizontal);
            row.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
            row.Margin.Should().Be(new Thickness(0, 8, 0, 0));
            row.Children.Count.Should().Be(1);

            var ok = row.Children[0].Should().BeOfType<Button>().Subject;
            ok.Content.Should().Be("_OK");
            ok.Width.Should().Be(76);
            ok.IsDefault.Should().BeTrue();
            ok.IsCancel.Should().BeTrue();
            AutomationProperties.GetName(ok).Should().Be("OK");
            AutomationProperties.GetAcceleratorKey(ok).Should().Be("Alt+O");

            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            accepted.Should().Be(1);
        });
    }

    [Fact]
    public void Create_UsesMnemonicFreeAutomationNameForCustomAcceptContent()
    {
        StaTestRunner.Run(() =>
        {
            var row = DialogButtonRowFactory.Create(
                () => { },
                buttonWidth: 72,
                acceptContent: "_Create");

            var ok = row.Children[0].Should().BeOfType<Button>().Subject;
            ok.Content.Should().Be("_Create");
            AutomationProperties.GetName(ok).Should().Be("Create");
            AutomationProperties.GetAcceleratorKey(ok).Should().Be("Alt+C");
        });
    }
}
