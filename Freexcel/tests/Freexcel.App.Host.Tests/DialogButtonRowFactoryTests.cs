using System.Windows;
using System.Windows.Controls;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

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
            ok.Content.Should().Be("OK");
            ok.Width.Should().Be(72);
            ok.Margin.Should().Be(new Thickness(0, 0, 8, 0));
            ok.IsDefault.Should().BeTrue();

            var cancel = row.Children[1].Should().BeOfType<Button>().Subject;
            cancel.Content.Should().Be("Cancel");
            cancel.Width.Should().Be(72);
            cancel.IsCancel.Should().BeTrue();

            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            accepted.Should().Be(1);
        });
    }
}
