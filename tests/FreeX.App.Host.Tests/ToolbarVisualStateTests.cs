using FluentAssertions;
using FreeX.App.Presentation.Ribbon;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ToolbarVisualStateTests
{
    [Fact]
    public void From_CapturesFormattingState()
    {
        var style = new CellStyle
        {
            Bold = true,
            Italic = true,
            Underline = true,
            FontName = "Aptos",
            FontSize = 11,
            WrapText = true
        };

        var state = ToolbarVisualState.From(style);

        state.Should().Be(new ToolbarVisualState(
            Bold: true,
            Italic: true,
            Underline: true,
            Strikethrough: false,
            VerticalAlignment: VerticalAlignment.Bottom,
            HorizontalAlignment: HorizontalAlignment.General,
            WrapText: true,
            FontName: "Aptos",
            FontSizeText: "11"));
    }
}
