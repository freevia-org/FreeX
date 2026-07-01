using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class TextBoxInlineEditPlannerTests
{
    [Theory]
    [InlineData(TextBoxInlineEditKey.Escape, false, TextBoxInlineEditKeyAction.Cancel)]
    [InlineData(TextBoxInlineEditKey.Enter, false, TextBoxInlineEditKeyAction.Commit)]
    [InlineData(TextBoxInlineEditKey.Return, false, TextBoxInlineEditKeyAction.Commit)]
    [InlineData(TextBoxInlineEditKey.Tab, false, TextBoxInlineEditKeyAction.Commit)]
    [InlineData(TextBoxInlineEditKey.Tab, true, TextBoxInlineEditKeyAction.Commit)]
    [InlineData(TextBoxInlineEditKey.Escape, true, TextBoxInlineEditKeyAction.None)]
    [InlineData(TextBoxInlineEditKey.Enter, true, TextBoxInlineEditKeyAction.None)]
    [InlineData(TextBoxInlineEditKey.Other, false, TextBoxInlineEditKeyAction.None)]
    public void PlanKeyDown_MapsInlineEditorKeysToCommitOrCancel(
        TextBoxInlineEditKey key,
        bool hasModifiers,
        TextBoxInlineEditKeyAction expected) =>
        TextBoxInlineEditPlanner.PlanKeyDown(key, hasModifiers).Should().Be(expected);

    [Theory]
    [InlineData("Before", "Before", false)]
    [InlineData("Before", "After", true)]
    [InlineData(null, "", true)]
    public void CreateCommitPlan_ReportsOrdinalTextChanges(
        string? originalText,
        string editedText,
        bool expectedChanged)
    {
        var plan = TextBoxInlineEditPlanner.CreateCommitPlan(originalText, editedText);

        plan.Text.Should().Be(editedText);
        plan.TextChanged.Should().Be(expectedChanged);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void ShouldCommitLostFocus_RequiresVisibleEditorWithoutKeyboardOrLogicalFocus(
        bool editorVisible,
        bool editorHasKeyboardFocus,
        bool editorHasLogicalFocus,
        bool expected) =>
        TextBoxInlineEditPlanner
            .ShouldCommitLostFocus(editorVisible, editorHasKeyboardFocus, editorHasLogicalFocus)
            .Should()
            .Be(expected);
}
