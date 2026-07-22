using System.Reflection;

using Avalonia.Controls;
using Avalonia.Layout;

using FluentAssertions;

using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R68-render-conditional-format-icon-6-3: the Avalonia icon-set glyph layer
/// (<c>MainWindow.CreateConditionalIconLayer</c>) always pinned the glyph to the physical LEFT edge
/// with left padding, even on a right-to-left sheet -- unlike Excel/WPF (and the shared
/// <see cref="ConditionalIconCellLayoutPlanner"/>'s own <c>isRightToLeft</c> branch, and how this
/// same Avalonia shell already mirrors data bars/row headers/cell alignment for RTL sheets). The fix
/// threads the resolved per-cell RTL flag through <c>CreateDefaultCellContent</c> into the icon layer
/// (and the text gutter), pinning the glyph to the RIGHT edge and reserving the text gutter on the
/// right when the sheet is RTL.
/// </summary>
public sealed class R68_ConditionalIconRtlLayoutTests
{
    private static readonly MethodInfo CreateConditionalIconLayerMethod = typeof(MainWindow).GetMethod(
        "CreateConditionalIconLayer", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void CreateConditionalIconLayer_RightToLeft_PinsGlyphToTheRightEdge()
    {
        var icon = new CfIconRenderInstruction(
            ConditionalIconGlyphKind.Arrow, IconIndex: 1, IconCount: 3, ColorHex: "#C00000", ShowValue: true, TextGutter: 20);

        var outer = (Border)CreateConditionalIconLayerMethod.Invoke(null, [icon, 1.0, true])!;

        outer.HorizontalAlignment.Should().Be(HorizontalAlignment.Right,
            "an RTL sheet must pin the icon-set glyph to the RIGHT edge, mirroring Excel/WPF and " +
            "ConditionalIconCellLayoutPlanner's own isRightToLeft branch");
        outer.Padding.Right.Should().BeGreaterThan(0,
            "the inset from the pinned (right) edge must be reserved on the right, not the left");
        outer.Padding.Left.Should().Be(0);

        var innerGlyphHost = (Border)outer.Child!;
        innerGlyphHost.HorizontalAlignment.Should().Be(HorizontalAlignment.Right);
    }

    [Fact]
    public void CreateConditionalIconLayer_LeftToRight_NoRegression_StillPinsGlyphToTheLeftEdge()
    {
        var icon = new CfIconRenderInstruction(
            ConditionalIconGlyphKind.Arrow, IconIndex: 1, IconCount: 3, ColorHex: "#C00000", ShowValue: true, TextGutter: 20);

        var outer = (Border)CreateConditionalIconLayerMethod.Invoke(null, [icon, 1.0, false])!;

        outer.HorizontalAlignment.Should().Be(HorizontalAlignment.Left);
        outer.Padding.Left.Should().BeGreaterThan(0);
        outer.Padding.Right.Should().Be(0);

        var innerGlyphHost = (Border)outer.Child!;
        innerGlyphHost.HorizontalAlignment.Should().Be(HorizontalAlignment.Left);
    }

    [Fact]
    public void CreateConditionalIconLayer_DefaultParameter_StillDefaultsToLeftToRight()
    {
        // Sibling no-regression check: the new isRightToLeft parameter defaults to false, so any
        // existing call site that doesn't pass it (there are none left in MainWindow.cs, but the
        // parameter itself still carries the safe default) behaves exactly as before the fix.
        var icon = new CfIconRenderInstruction(
            ConditionalIconGlyphKind.Arrow, IconIndex: 1, IconCount: 3, ColorHex: "#C00000", ShowValue: true, TextGutter: 20);

        var parameters = CreateConditionalIconLayerMethod.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[2].Name.Should().Be("isRightToLeft");
        parameters[2].HasDefaultValue.Should().BeTrue();
        parameters[2].DefaultValue.Should().Be(false);
    }
}
