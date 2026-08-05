using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaRangeEditingSessionTests
{
    [Theory]
    [InlineData("=", false, true, FormulaEditStatusBarMode.Enter)]
    [InlineData("=SUM(", true, true, null)]
    [InlineData("=SUM(", false, false, null)]
    [InlineData("value", false, false, null)]
    [InlineData(null, false, false, null)]
    public void TextChanged_UsesFormulaEntryTransitionMatrix(
        string? text,
        bool initialPointMode,
        bool expectedPointMode,
        FormulaEditStatusBarMode? expectedStatusMode)
    {
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(initialPointMode);

        var plan = session.ApplyTextChanged(text);

        session.PointMode.Should().Be(expectedPointMode);
        plan.StatusBarPlan?.Mode.Should().Be(expectedStatusMode);
    }

    [Theory]
    [InlineData("=A1", false, true, true, FormulaEditStatusBarMode.Point)]
    [InlineData("=A1", true, false, true, FormulaEditStatusBarMode.Edit)]
    [InlineData("value", false, false, false, FormulaEditStatusBarMode.Edit)]
    public void PointModeToggle_UsesFormulaAndCurrentModeMatrix(
        string text,
        bool initialPointMode,
        bool expectedPointMode,
        bool expectedHandled,
        FormulaEditStatusBarMode expectedStatusMode)
    {
        var session = CreatePopulatedSession(initialPointMode);

        var plan = session.TogglePointMode(text);

        session.PointMode.Should().Be(expectedPointMode);
        plan.Handled.Should().Be(expectedHandled);
        plan.StatusBarPlan.Mode.Should().Be(expectedStatusMode);
        if (!expectedPointMode)
        {
            session.ReferenceSpan.Should().BeNull();
            session.SheetSpan.Should().Be(FormulaSheetSpanEntryState.Empty);
        }
    }

    [Theory]
    [InlineData(FormulaEditorKey.F8, FormulaEditorModifiers.None, ExcelSelectionMode.Extend)]
    [InlineData(FormulaEditorKey.F8, FormulaEditorModifiers.Shift, ExcelSelectionMode.Add)]
    [InlineData(FormulaEditorKey.Left, FormulaEditorModifiers.None, ExcelSelectionMode.Normal)]
    public void SelectionModeToggle_UsesKeyboardMatrix(
        FormulaEditorKey key,
        FormulaEditorModifiers modifiers,
        ExcelSelectionMode expectedMode)
    {
        var session = new FormulaRangeEditingSession();

        var handled = session.TryToggleSelectionMode(key, modifiers);

        handled.Should().Be(key == FormulaEditorKey.F8);
        session.SelectionMode.Should().Be(expectedMode);
    }

    [Fact]
    public void PlannerEdit_TracksReferenceSpanAnchorAndCursor()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var anchor = new CellAddress(sheet, 2, 3);
        var cursor = new CellAddress(sheet, 5, 7);
        var edit = new FormulaRangeEntryEdit(
            new ExcelTextEdit("=C2:G5", 6, 0),
            ReferenceStart: 1,
            ReferenceLength: 5);
        var session = new FormulaRangeEditingSession();

        session.ApplyPlannerEdit(edit, anchor, cursor);

        session.ReferenceSpan.Should().Be(new FormulaReferenceEntrySpan(1, 5));
        session.SelectionAnchor.Should().Be(anchor);
        session.SelectionCursor.Should().Be(cursor);
    }

    [Fact]
    public void PlanSelection_ExtendsFromTrackedAnchorAndNormalizesRange()
    {
        var sheet = new SheetId(Guid.NewGuid());
        var anchor = new CellAddress(sheet, 5, 7);
        var target = new CellAddress(sheet, 2, 3);
        var session = new FormulaRangeEditingSession();
        session.TrackSelection(anchor, anchor);

        var plan = session.PlanSelection(target, extendSelection: true);

        plan.Anchor.Should().Be(anchor);
        plan.Cursor.Should().Be(target);
        plan.Range.Should().Be(new GridRange(target, anchor));
    }

    [Fact]
    public void SelectionAndSheetSpanState_ResetAsOneSession()
    {
        var session = CreatePopulatedSession(pointMode: true);

        session.Reset();

        session.PointMode.Should().BeFalse();
        session.SelectionMode.Should().Be(ExcelSelectionMode.Normal);
        session.ReferenceSpan.Should().BeNull();
        session.SelectionAnchor.Should().BeNull();
        session.SelectionCursor.Should().BeNull();
        session.SheetSpan.Should().Be(FormulaSheetSpanEntryState.Empty);
    }

    private static FormulaRangeEditingSession CreatePopulatedSession(bool pointMode)
    {
        var sheet = new SheetId(Guid.NewGuid());
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(pointMode);
        session.TryToggleSelectionMode(FormulaEditorKey.F8, FormulaEditorModifiers.None);
        session.TrackReferenceSpan(2, 4);
        session.TrackSelection(
            new CellAddress(sheet, 2, 3),
            new CellAddress(sheet, 5, 7));
        session.ApplySheetTabSelection("Sheet1", "Sheet2", shiftHeld: false);
        return session;
    }
}
