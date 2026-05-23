using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ToolbarVisualStateTests
{
    [Fact]
    public void From_CapturesFormattingAndUndoRedoState()
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

        var state = ToolbarVisualState.From(style, canUndo: true, canRedo: false);

        state.Should().Be(new ToolbarVisualState(
            CanUndo: true,
            CanRedo: false,
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
