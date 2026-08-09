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

        var handled = session.TryToggleSelectionMode(key, modifiers, out var plan);

        handled.Should().Be(key == FormulaEditorKey.F8);
        session.SelectionMode.Should().Be(expectedMode);
        session.ShouldAppendKeyboardSelection.Should().Be(expectedMode == ExcelSelectionMode.Add);
        if (handled && expectedMode == ExcelSelectionMode.Normal)
        {
            plan.EditStatusBarPlan?.Mode.Should().Be(FormulaEditStatusBarMode.Point);
            plan.StatusBarModeResourceKey.Should().BeNull();
        }
        else if (handled)
        {
            plan.EditStatusBarPlan.Should().BeNull();
            plan.StatusBarModeResourceKey.Should().NotBeNullOrWhiteSpace();
        }
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
    public void PointModeWorkflow_OwnsEligibilityDispatchAndRoutedCommandPolicy()
    {
        var sheet = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheet, 2, 3),
            new CellAddress(sheet, 4, 5));
        var selection = new FormulaPointModeEditSelection(
            "Data",
            range,
            ExternalWorkbookName: null,
            Mode: FormulaPointModeSelectionMode.Append,
            ExtendSelection: false);
        var appended = false;
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(true);

        session.TryApplyPointModeSelection(
                selection,
                hasRangeEditor: true,
                hasFormulaEditCell: true,
                append => appended = append == selection,
                _ => throw new InvalidOperationException("Replace path was not expected."))
            .Should().BeTrue();

        appended.Should().BeTrue();
        session.GetRoutedPointModeCommand(
                FormulaEditorKey.F4,
                hasRangeEditor: true,
                hasFormulaEditCell: true)
            .Should().BeNull();
        session.GetRoutedPointModeCommand(
                FormulaEditorKey.F4,
                hasRangeEditor: false,
                hasFormulaEditCell: false)
            .Should().Be(FormulaPointModeCommand.CycleReference);
        session.GetRoutedPointModeCommand(
                FormulaEditorKey.Escape,
                hasRangeEditor: false,
                hasFormulaEditCell: false)
            .Should().Be(FormulaPointModeCommand.Cancel);
        session.GetRoutedPointModeCommand(
                FormulaEditorKey.Enter,
                hasRangeEditor: false,
                hasFormulaEditCell: false)
            .Should().Be(FormulaPointModeCommand.Commit);
        session.ShouldAppendDisjointReference(FormulaEditorModifiers.Control).Should().BeTrue();
        session.ShouldAppendDisjointReference(FormulaEditorModifiers.Meta).Should().BeTrue();
        session.ShouldAppendDisjointReference(FormulaEditorModifiers.Shift).Should().BeFalse();
        session.ShouldOfferCellValueAutoComplete(enabled: true).Should().BeFalse();
    }

    [Fact]
    public void RangeSelectionEdit_RecoversReferenceAndTracksAppliedPlan()
    {
        var sheet = SheetId.New();
        var formulaCell = new CellAddress(sheet, 1, 1);
        var range = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(true);

        var planned = session.TryPlanRangeSelectionEdit(
            new FormulaRangeEditorSnapshot(
                "=SUM(B2)",
                CaretIndex: 7,
                SelectionLength: 0,
                FormulaCell: formulaCell,
                UseR1C1ReferenceStyle: false,
                SelectedSheetName: "Sheet1"),
            range,
            range.Start,
            range.End,
            replacementText: null,
            out var plan);

        planned.Should().BeTrue();
        plan.Edit.TextEdit.Text.Should().Be("=SUM(B2:C3)");
        plan.UpdateLocalSelection.Should().BeTrue();

        session.ApplySelectionEdit(plan);

        session.ReferenceSpan.Should().Be(new FormulaReferenceEntrySpan(5, 5));
        session.SelectionAnchor.Should().Be(range.Start);
        session.SelectionCursor.Should().Be(range.End);
    }

    [Fact]
    public void DisjointAppendAndKeyboardNavigation_UseSessionState()
    {
        var sheet = SheetId.New();
        var formulaCell = new CellAddress(sheet, 1, 1);
        var current = new CellAddress(sheet, 2, 2);
        var target = new CellAddress(sheet, 2, 3);
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(true);
        session.TrackReferenceSpan(5, 2);
        session.TrackSelection(current, current);
        session.TryToggleSelectionMode(FormulaEditorKey.F8, FormulaEditorModifiers.Shift);

        var navigation = session.PlanKeyboardNavigation(
            new GridRange(current, current),
            current,
            FormulaEditorKey.Right,
            FormulaEditorKey.None,
            FormulaEditorModifiers.None,
            sheet: null,
            rowPageSize: 10,
            columnPageSize: 10);

        navigation.Should().Be(new FormulaRangeKeyboardNavigationPlan(
            current,
            target,
            ExtendSelection: false));
        session.TryPlanKeyboardDisjointRangeSelectionEdit(
                new FormulaRangeEditorSnapshot(
                    "=SUM(B2)",
                    CaretIndex: 7,
                    SelectionLength: 0,
                    FormulaCell: formulaCell,
                    UseR1C1ReferenceStyle: false,
                    SelectedSheetName: "Sheet1"),
                current,
                target,
                extendSelection: false,
                out var plan)
            .Should().BeTrue();
        plan.Edit.TextEdit.Text.Should().Be("=SUM(B2,C2)");
        plan.Range.Should().Be(new GridRange(target, target));
    }

    [Fact]
    public void EditKeyPlan_DerivesFormulaSurfaceFlagsFromSessionState()
    {
        var sheet = SheetId.New();
        var current = new CellAddress(sheet, 2, 2);
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(true);

        var intent = session.PlanEditKey(
            FormulaEditorKey.Right,
            FormulaEditorKey.None,
            FormulaEditorModifiers.None,
            current,
            pageSize: 10,
            text: "=B2",
            hasFormulaEditCell: true,
            surface: FormulaEditorSurfaceKind.FormulaBar,
            enteredViaEditKey: false,
            moveSelectionAfterEnter: true,
            enterDirection: FormulaEditorEnterDirection.Down);

        intent.Action.Should().Be(ExcelEditKeyAction.SelectFormulaReference);
        intent.Target.Should().Be(new CellAddress(sheet, 2, 3));
    }

    [Fact]
    public void ReferenceDragLifecycle_PlansPreviewResizeAndTrackedSpan()
    {
        var sheet = SheetId.New();
        var originalRange = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 3));
        var highlight = new FormulaReferenceHighlight(
            TextStart: 5,
            TextLength: 5,
            PaletteIndex: 0,
            Text: "B2:C3",
            SheetName: null,
            Range: originalRange);
        var session = new FormulaRangeEditingSession();

        session.TryBeginReferenceDrag(highlight).Should().BeTrue();
        session.PlanActiveReferenceDrag(new CellAddress(sheet, 5, 5))
            .Should().Be(new GridRange(originalRange.Start, new CellAddress(sheet, 5, 5)));
        session.EndReferenceDrag().Should().BeSameAs(highlight);
        session.IsReferenceDragActive.Should().BeFalse();

        var edit = session.PlanReferenceResizeEdit(
            "=SUM(B2:C3)",
            highlight,
            new GridRange(originalRange.Start, new CellAddress(sheet, 5, 5)),
            useR1C1ReferenceStyle: false);
        edit.Text.Should().Be("=SUM(B2:E5)");

        session.ApplyReferenceResizeEdit(highlight, edit);
        session.ReferenceSpan.Should().Be(new FormulaReferenceEntrySpan(5, 5));
    }

    [Fact]
    public void FunctionAutocompleteWorkflow_OwnsCandidatesNavigationAndCommitSpan()
    {
        var session = new FormulaRangeEditingSession();

        var candidates = session.RefreshFunctionAutocomplete(
            "=SU",
            caretIndex: 3,
            functionNames: ["SUM", "SUBTOTAL"],
            definedNames: ["Summary"],
            tableNames: null);

        candidates.Should().Equal("SUBTOTAL", "SUM", "Summary");
        session.MoveFunctionAutocompleteSelection(currentIndex: 0, delta: 1).Should().Be(1);

        var edit = session.CommitFunctionAutocomplete("=SU", "SUM", ["SUM", "SUBTOTAL"]);

        edit.Should().Be(new ExcelTextEdit("=SUM(", 5, 0));
        session.FunctionAutocompleteCandidates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FormulaEditorKey.Down, FormulaFunctionAutocompleteKeyAction.MoveSelection, 1, true)]
    [InlineData(FormulaEditorKey.Up, FormulaFunctionAutocompleteKeyAction.MoveSelection, 2, true)]
    [InlineData(FormulaEditorKey.Tab, FormulaFunctionAutocompleteKeyAction.CommitSelection, 0, true)]
    [InlineData(FormulaEditorKey.Enter, FormulaFunctionAutocompleteKeyAction.CommitSelection, 0, true)]
    [InlineData(FormulaEditorKey.Escape, FormulaFunctionAutocompleteKeyAction.Dismiss, 0, true)]
    [InlineData(FormulaEditorKey.Left, FormulaFunctionAutocompleteKeyAction.None, 0, false)]
    public void FunctionAutocompleteKeyPolicy_IsRendererNeutral(
        FormulaEditorKey key,
        FormulaFunctionAutocompleteKeyAction expectedAction,
        int expectedIndex,
        bool expectedHandled)
    {
        var session = new FormulaRangeEditingSession();
        session.RefreshFunctionAutocomplete(
            "=SU",
            caretIndex: 3,
            functionNames: ["SUM", "SUBTOTAL"],
            definedNames: ["Summary"],
            tableNames: null);

        var plan = session.PlanFunctionAutocompleteKey(key, currentIndex: 0);

        plan.Action.Should().Be(expectedAction);
        plan.SelectionIndex.Should().Be(expectedIndex);
        plan.Handled.Should().Be(expectedHandled);
    }

    [Fact]
    public void CellValueAutocompleteWorkflow_OwnsEligibilitySuggestionAndSuppression()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("California"));
        var address = new CellAddress(sheet.Id, 2, 1);
        var session = new FormulaRangeEditingSession();

        var plan = session.PlanCellValueAutocomplete(
            enabled: true,
            text: "Cal",
            caretIndex: 3,
            selectionLength: 0,
            sheet: sheet,
            address: address);

        plan.Should().Be(new FormulaCellValueAutocompletePlan(
            "California",
            SelectionStart: 3,
            SelectionLength: 7));

        session.SuppressNextCellValueAutocomplete();
        session.ConsumeCellValueAutocompleteSuppression().Should().BeTrue();
        session.ConsumeCellValueAutocompleteSuppression().Should().BeFalse();

        session.SetPointMode(true);
        session.PlanCellValueAutocomplete(true, "Cal", 3, 0, sheet, address)
            .Should().BeNull();
    }

    [Fact]
    public void CaretExitPolicy_ClearsOnlyWhenRendererEventIsAuthoritative()
    {
        var session = new FormulaRangeEditingSession();
        session.TrackReferenceSpan(2, 4);

        session.ClearReferenceSpanIfCaretLeft(
                textLength: 10,
                selectionStart: 1,
                selectionLength: 6,
                caretIndex: 1,
                preserveWhileSelectionActive: true)
            .Should().BeFalse();
        session.ReferenceSpan.Should().NotBeNull();

        session.ClearReferenceSpanIfCaretLeft(
                textLength: 10,
                selectionStart: 1,
                selectionLength: 6,
                caretIndex: 1,
                preserveWhileSelectionActive: false)
            .Should().BeTrue();
        session.ReferenceSpan.Should().BeNull();
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
        session.FunctionAutocompleteCandidates.Should().BeEmpty();
        session.ConsumeCellValueAutocompleteSuppression().Should().BeFalse();
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
        session.RefreshFunctionAutocomplete("=SU", 3, ["SUM"], null, null);
        session.SuppressNextCellValueAutocomplete();
        return session;
    }
}
