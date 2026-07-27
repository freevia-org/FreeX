using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class RightToLeftTextSurfaceTests
{
    private const string HebrewSample = "\u05D0\u05D1\u05D2";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SlideHeadlessApp).Assembly);

    [Fact]
    public async Task AvaloniaRichTextSurface_UsesResolvedParagraphFlowDirections()
    {
        await Session.Dispatch(() =>
        {
            var body = new TextBody
            {
                DefaultParaRightToLeft = true,
                Paragraphs =
                {
                    new Paragraph { Runs = { new Run { Text = HebrewSample } } },
                    new Paragraph { RightToLeft = false, Runs = { new Run { Text = "abc" } } },
                },
            };
            var surface = new AvaloniaRichTextEditingSurface
            {
                Width = 320,
                Height = 100,
            };
            surface.UpdateBody(body, "Arial", 18);
            var window = new Window { Width = 320, Height = 100, Content = surface };
            window.Show();
            try
            {
                surface.Measure(new Size(320, 100));
                surface.Arrange(new Rect(0, 0, 320, 100));

                surface.LayoutFlowDirections.Should().ContainInOrder(
                    FlowDirection.RightToLeft,
                    FlowDirection.LeftToRight);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
