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
            ok.Content.Should().Be(UiText.Ok);
            double.IsNaN(ok.Width).Should().BeTrue();
            ok.MinWidth.Should().Be(72);
            ok.Margin.Should().Be(new Thickness(0, 0, 8, 0));
            ok.IsDefault.Should().BeTrue();
            AutomationProperties.GetName(ok).Should().Be(UiText.CreateAutomationName(UiText.Ok));
            AutomationProperties.GetAcceleratorKey(ok).Should().Be("Alt+O");

            var cancel = row.Children[1].Should().BeOfType<Button>().Subject;
            cancel.Content.Should().Be(UiText.Cancel);
            double.IsNaN(cancel.Width).Should().BeTrue();
            cancel.MinWidth.Should().Be(72);
            cancel.IsCancel.Should().BeTrue();
            AutomationProperties.GetName(cancel).Should().Be(UiText.CreateAutomationName(UiText.Cancel));
            AutomationProperties.GetAcceleratorKey(cancel).Should().Be("Alt+C");

            DialogSourceTestSupport.ClickButton(ok);
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
            ok.Content.Should().Be(UiText.Ok);
            double.IsNaN(ok.Width).Should().BeTrue();
            ok.MinWidth.Should().Be(76);
            ok.IsDefault.Should().BeTrue();
            ok.IsCancel.Should().BeTrue();
            AutomationProperties.GetName(ok).Should().Be(UiText.CreateAutomationName(UiText.Ok));
            AutomationProperties.GetAcceleratorKey(ok).Should().Be("Alt+O");

            DialogSourceTestSupport.ClickButton(ok);
            accepted.Should().Be(1);
        });
    }

    [Fact]
    public void Create_WithConfiguredButtons_BuildsSharedDefaultCancelRow()
    {
        StaTestRunner.Run(() =>
        {
            var accept = new Button { Content = "_Keep Result", Width = 104 };
            var cancel = new Button { Content = "_Restore Original Values", Width = 152 };

            var row = DialogButtonRowFactory.Create(accept, cancel, new Thickness(0, 6, 0, 0));

            row.Orientation.Should().Be(Orientation.Horizontal);
            row.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
            row.Margin.Should().Be(new Thickness(0, 6, 0, 0));
            row.Children.Count.Should().Be(2);
            row.Children[0].Should().BeSameAs(accept);
            row.Children[1].Should().BeSameAs(cancel);
            accept.Margin.Should().Be(new Thickness(0, 0, 8, 0));
            accept.IsDefault.Should().BeTrue();
            accept.Width.Should().Be(104);
            cancel.Margin.Should().Be(new Thickness());
            cancel.IsCancel.Should().BeTrue();
            cancel.Width.Should().Be(152);
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

    [Theory]
    [InlineData("_Apply", "Apply", "Alt+A")]
    [InlineData("Save __As", "Save _As", null)]
    [InlineData("Save ___As", "Save _As", "Alt+A")]
    public void Create_UsesSharedWpfMnemonicContract(
        string content,
        string expectedDisplayText,
        string? expectedAccelerator)
    {
        StaTestRunner.Run(() =>
        {
            var row = DialogButtonRowFactory.Create(
                () => { },
                buttonWidth: 72,
                acceptContent: content);

            var button = row.Children[0].Should().BeOfType<Button>().Subject;
            button.Content.Should().Be(content);
            ShellStringText.NormalizeAccessText((string)button.Content)
                .Should().Be(expectedDisplayText);

            var accelerator = AutomationProperties.GetAcceleratorKey(button);
            if (expectedAccelerator is null)
                accelerator.Should().BeNullOrEmpty();
            else
                accelerator.Should().Be(expectedAccelerator);
        });
    }

    [Fact]
    public void Create_AllowsActionButtonsToGrowBeyondMinimumWidth()
    {
        StaTestRunner.Run(() =>
        {
            var row = DialogButtonRowFactory.Create(
                () => { },
                buttonWidth: 72,
                acceptContent: "_Apply All Selected Changes");

            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var ok = row.Children[0].Should().BeOfType<Button>().Subject;
            ok.MinWidth.Should().Be(72);
            ok.DesiredSize.Width.Should().BeGreaterThan(72);
            double.IsNaN(ok.Width).Should().BeTrue();
        });
    }
}
