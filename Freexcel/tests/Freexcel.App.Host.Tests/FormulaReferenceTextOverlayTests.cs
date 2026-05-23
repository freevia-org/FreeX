using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.App.Host;
using Xunit;

namespace Freexcel.App.Host.Tests;

public sealed class FormulaReferenceTextOverlayTests
{
    [Fact]
    public void Apply_KeepsFormulaOverlayVisibleWithoutReferencesWhenRequested()
    {
        StaTestRunner.Run(() =>
        {
            var overlay = new TextBlock();

            FormulaReferenceTextOverlay.Apply(
                overlay,
                "=SUM(",
                [],
                [Brushes.Blue],
                Brushes.Black,
                keepFormulaVisibleWithoutHighlights: true);

            overlay.Visibility.Should().Be(Visibility.Visible);
            overlay.Inlines.Should().ContainSingle();
        });
    }
}
