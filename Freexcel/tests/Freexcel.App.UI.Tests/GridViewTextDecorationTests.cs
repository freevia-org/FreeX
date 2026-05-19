using System.Windows;
using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewTextDecorationTests
{
    [Fact]
    public void BuildTextDecorations_ComposesUnderlineAndStrikethrough()
    {
        var decorations = GridView.BuildTextDecorations(new CellStyle
        {
            Underline = true,
            Strikethrough = true
        });

        decorations.Should().NotBeNull();
        decorations!.Should().Contain(decoration => decoration.Location == TextDecorationLocation.Underline);
        decorations.Should().Contain(decoration => decoration.Location == TextDecorationLocation.Strikethrough);
    }

    [Fact]
    public void BuildTextDecorations_ReturnsNullWhenNoDecorationsAreEnabled()
    {
        GridView.BuildTextDecorations(new CellStyle()).Should().BeNull();
    }
}
