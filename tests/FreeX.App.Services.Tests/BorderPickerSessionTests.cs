using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class BorderPickerSessionTests
{
    private static readonly CellColor Accent = new(68, 114, 196);

    [Fact]
    public void DefaultsMatchBothRendererPickerDefaults()
    {
        var session = new BorderPickerSession();

        session.Style.Should().Be(BorderStyle.Thin);
        session.Color.Should().Be(CellColor.Black);
        session.DrawMode.Should().Be(BorderDrawMode.None);
        session.IsDrawModeActive.Should().BeFalse();
    }

    [Fact]
    public void ConsumeCapturesCurrentPickerStateAndEndsOnlyDrawMode()
    {
        var session = new BorderPickerSession();
        session.SetStyle(BorderStyle.Double);
        session.SetColor(Accent);
        session.BeginDrawMode(BorderDrawMode.DrawGrid);

        var consumed = session.TryConsumeDrawPlan(out var plan);

        consumed.Should().BeTrue();
        plan.Should().Be(new BorderDrawExecutionPlan(
            BorderDrawMode.DrawGrid,
            BorderStyle.Double,
            Accent));
        session.IsDrawModeActive.Should().BeFalse();
        session.Style.Should().Be(BorderStyle.Double);
        session.Color.Should().Be(Accent);
    }

    [Fact]
    public void CancelEndsDrawModeWithoutResettingPickerChoices()
    {
        var session = new BorderPickerSession();
        session.SetStyle(BorderStyle.Dashed);
        session.SetColor(Accent);
        session.BeginDrawMode(BorderDrawMode.Erase);

        session.CancelDrawMode();

        session.TryConsumeDrawPlan(out _).Should().BeFalse();
        session.Style.Should().Be(BorderStyle.Dashed);
        session.Color.Should().Be(Accent);
    }

    [Fact]
    public void BeginRejectsInactiveMode()
    {
        var session = new BorderPickerSession();

        var act = () => session.BeginDrawMode(BorderDrawMode.None);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("mode");
    }
}
