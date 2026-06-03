using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class DataToolDialogTests
{
    [Fact]
    public void TextToColumnsResult_ParsesFixedWidthBreakPositions()
    {
        TextToColumnsDialog.ParseFixedWidthBreakPositions("12, 4; 8 4")
            .Should()
            .Equal(4, 8, 12);

        var result = TextToColumnsDialog.CreateFixedWidthResult("4,8");
        result.SplitMode.Should().Be(TextToColumnsSplitMode.FixedWidth);
        result.FixedWidthBreakPositions.Should().Equal(4, 8);
    }

    [Theory]
    [InlineData("4,bad", 12)]
    [InlineData("0,4", 12)]
    [InlineData("4,12", 12)]
    [InlineData("", 12)]
    [InlineData("   ", 12)]
    [InlineData("1", 1)]
    public void TextToColumnsResult_RejectsInvalidFixedWidthBreakPositions(string text, int maxLength)
    {
        TextToColumnsDialog.TryParseFixedWidthBreakPositions(text, maxLength, out var positions).Should().BeFalse();
        positions.Should().BeEmpty();
    }

    [Fact]
    public void TextToColumnsResult_TryParseFixedWidthBreakPositionsRequiresPreviewRange()
    {
        TextToColumnsDialog.TryParseFixedWidthBreakPositions("8, 4; 4", 12, out var positions).Should().BeTrue();
        positions.Should().Equal(4, 8);
    }

    [Fact]
    public void TextToColumnsFixedWidthBreakHelpers_AddMoveAndRemoveBreaks()
    {
        TextToColumnsDialog.AddFixedWidthBreakPosition([8, 4], 12, maxLength: 20)
            .Should()
            .Equal(4, 8, 12);
        TextToColumnsDialog.AddFixedWidthBreakPosition([4, 8], 99, maxLength: 20)
            .Should()
            .Equal(4, 8, 19);

        TextToColumnsDialog.MoveFixedWidthBreakPosition([4, 8, 12], index: 1, position: 10, maxLength: 20)
            .Should()
            .Equal(4, 10, 12);

        TextToColumnsDialog.RemoveFixedWidthBreakPosition([4, 8, 12], index: 1)
            .Should()
            .Equal(4, 12);
    }

    [Fact]
    public void TextToColumnsFixedWidthBreakPlanner_ParsesAndMutatesBreaks()
    {
        TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions("12, 4; x 8 4")
            .Should()
            .Equal(4, 8, 12);
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("8, 4; 4", 12, out var parsed)
            .Should()
            .BeTrue();
        parsed.Should().Equal(4, 8);
        TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions("8, 12", 12, out _)
            .Should()
            .BeFalse();
        TextToColumnsFixedWidthBreakPlanner.AddBreakPosition([8, 4], 99, maxLength: 20)
            .Should()
            .Equal(4, 8, 19);
        TextToColumnsFixedWidthBreakPlanner.MoveBreakPosition([4, 8, 12], index: 1, position: 10, maxLength: 20)
            .Should()
            .Equal(4, 10, 12);
        TextToColumnsFixedWidthBreakPlanner.RemoveBreakPosition([4, 8, 12], index: 1)
            .Should()
            .Equal(4, 12);
    }

    [Fact]
    public void TextToColumnsDialogHelpers_ForwardFixedWidthBreakWorkToPlanner()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Helpers.cs"));

        source.Should().Contain("TextToColumnsDialogPlanner.BuildPreviewRows");
        source.Should().Contain("TextToColumnsDialogPlanner.TryParseDestination");
        source.Should().Contain("TextToColumnsDialogPlanner.NormalizeColumnFormats");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.AddBreakPosition");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.MoveBreakPosition");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.RemoveBreakPosition");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.ParseBreakPositions");
        source.Should().Contain("TextToColumnsFixedWidthBreakPlanner.TryParseBreakPositions");
    }

    [Fact]
    public void TextToColumnsFixedWidthRulerPlanner_MapsBreaksAndNearestHit()
    {
        TextToColumnsFixedWidthRulerPlanner.PositionFromRulerX(110, rulerWidth: 440, maxLength: 20)
            .Should().Be(5);
        TextToColumnsFixedWidthRulerPlanner.RulerXFromPosition(10, rulerWidth: 440, maxLength: 20)
            .Should().Be(220);
        TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex([4, 8, 12], x: 178, tolerance: 5, rulerWidth: 440, maxLength: 20)
            .Should().Be(1);
        TextToColumnsFixedWidthRulerPlanner.FindNearestBreakIndex([4, 8, 12], x: 178, tolerance: 1, rulerWidth: 440, maxLength: 20)
            .Should().Be(-1);
    }

    [Fact]
    public void TextToColumnsFixedWidthRulerDrag_CancelsOnReleasedButtonOrLostCapture()
    {
        var dialogSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.cs"));
        var rulerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.FixedWidth.cs"));

        var mouseMove = rulerSource[
            rulerSource.IndexOf("private void FixedWidthRuler_MouseMove", StringComparison.Ordinal)..
            rulerSource.IndexOf("private void FixedWidthRuler_MouseLeftButtonUp", StringComparison.Ordinal)];
        var mouseUpAndLostCapture = rulerSource[
            rulerSource.IndexOf("private void FixedWidthRuler_MouseLeftButtonUp", StringComparison.Ordinal)..
            rulerSource.IndexOf("private void FixedWidthRuler_MouseRightButtonDown", StringComparison.Ordinal)];
        var cancelHelper = rulerSource[
            rulerSource.IndexOf("private void CancelFixedWidthRulerDrag", StringComparison.Ordinal)..
            rulerSource.IndexOf("private int FindNearestBreakIndex", StringComparison.Ordinal)];

        dialogSource.Should().Contain("_fixedWidthRuler.LostMouseCapture += FixedWidthRuler_LostMouseCapture;");
        mouseMove.Should().Contain("if (_dragBreakIndex is not { } index)");
        mouseMove.Should().Contain("if (e.LeftButton != MouseButtonState.Pressed)");
        mouseMove.Should().Contain("CancelFixedWidthRulerDrag();");
        mouseMove.Should().Contain("e.Handled = true;");
        mouseMove.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseMove.IndexOf("var positions = ParseFixedWidthBreakPositions", StringComparison.Ordinal));

        mouseUpAndLostCapture.Should().Contain("CancelFixedWidthRulerDrag();");
        mouseUpAndLostCapture.Should().Contain("if (_dragBreakIndex is null && !_fixedWidthRuler.IsMouseCaptured)");
        mouseUpAndLostCapture.Should().Contain("return;");
        mouseUpAndLostCapture.Should().Contain("private void FixedWidthRuler_LostMouseCapture");
        mouseUpAndLostCapture.Should().Contain("_dragBreakIndex = null;");
        mouseUpAndLostCapture.IndexOf("if (_dragBreakIndex is null && !_fixedWidthRuler.IsMouseCaptured)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(mouseUpAndLostCapture.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal));
        cancelHelper.Should().Contain("_dragBreakIndex = null;");
        cancelHelper.Should().Contain("if (_fixedWidthRuler.IsMouseCaptured)");
        cancelHelper.Should().Contain("_fixedWidthRuler.ReleaseMouseCapture();");
    }

    [Fact]
    public void TextToColumnsFixedWidthRulerRightClick_RemovesNearestBreakAndHandlesMouseEvent()
    {
        var rulerSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.FixedWidth.cs"));

        var rightClick = rulerSource[
            rulerSource.IndexOf("private void FixedWidthRuler_MouseRightButtonDown", StringComparison.Ordinal)..
            rulerSource.IndexOf("private int AddFixedWidthBreakAt", StringComparison.Ordinal)];

        rightClick.Should().Contain("if (_fixedWidthButton.IsChecked != true)");
        rightClick.Should().Contain("CancelFixedWidthRulerDrag();");
        rightClick.Should().Contain("var positions = ParseFixedWidthBreakPositions(_fixedWidthBreaksBox.Text);");
        rightClick.Should().Contain("FindNearestBreakIndex(positions, e.GetPosition(_fixedWidthRuler).X, tolerance: 10)");
        rightClick.Should().Contain("UpdateFixedWidthBreakPositions(RemoveFixedWidthBreakPosition(positions, nearest));");
        rightClick.Should().Contain("e.Handled = true;");
        rightClick.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClick.IndexOf("var positions = ParseFixedWidthBreakPositions", StringComparison.Ordinal));
        rightClick.IndexOf("UpdateFixedWidthBreakPositions(RemoveFixedWidthBreakPosition(positions, nearest));", StringComparison.Ordinal)
            .Should()
            .BeLessThan(rightClick.IndexOf("e.Handled = true;", StringComparison.Ordinal));
    }

    [Fact]
    public void TextToColumnsModeSwitch_CancelsFixedWidthRulerDragWhenLeavingFixedWidth()
    {
        var wizardSource = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "TextToColumnsDialog.Wizard.cs"));

        var refreshMode = wizardSource[
            wizardSource.IndexOf("private void RefreshMode", StringComparison.Ordinal)..
            wizardSource.IndexOf("private void FocusCurrentWizardStepTarget", StringComparison.Ordinal)];

        refreshMode.Should().Contain("if (_fixedWidthButton.IsChecked != true)");
        refreshMode.Should().Contain("CancelFixedWidthRulerDrag();");
        refreshMode.IndexOf("CancelFixedWidthRulerDrag();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(refreshMode.IndexOf("_fixedWidthRuler.IsEnabled = plan.FixedWidthControlsEnabled;", StringComparison.Ordinal));
    }
}
