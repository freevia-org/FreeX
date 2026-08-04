using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace FreeX.App.Host.Tests;

public sealed class DialogTabChromeTests
{
    [Fact]
    public void Wpf_tab_chrome_overlaps_selected_header_into_body_without_a_seam_gap()
    {
        StaTestRunner.Run(() =>
        {
            var tabs = new TabControl();

            DialogTabChrome.Apply(tabs);

            tabs.Padding.Should().Be(new Thickness(0));
            tabs.BorderThickness.Should().Be(new Thickness(
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness,
                DialogTabChromeMetrics.PaneBorderThickness));

            var style = tabs.ItemContainerStyle;
            style.Should().NotBeNull();
            var selected = style.Triggers.OfType<Trigger>().Single();
            var selectedMarginValue = selected.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == FrameworkElement.MarginProperty)
                .Value;
            var selectedBorderValue = style.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Control.BorderThicknessProperty)
                .Value;
            selectedMarginValue.Should().BeOfType<Thickness>();
            selectedBorderValue.Should().BeOfType<Thickness>();
            var selectedMargin = (Thickness)selectedMarginValue;
            var selectedBorder = (Thickness)selectedBorderValue;

            selectedMargin.Bottom.Should().Be(-DialogTabChromeMetrics.SelectedTabContentOverlap);
            selectedBorder.Bottom.Should().Be(0);
        });
    }
}
