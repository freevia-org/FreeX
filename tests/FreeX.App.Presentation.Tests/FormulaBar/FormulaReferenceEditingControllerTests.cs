using FluentAssertions;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaReferenceEditingControllerTests
{
    [Fact]
    public void TryApplyKeyboardSelection_NormalModeRoutesDirectly()
    {
        var sheetId = SheetId.New();
        var current = new CellAddress(sheetId, 2, 2);
        var target = new CellAddress(sheetId, 2, 3);
        var directCalls = new List<(CellAddress Target, bool Extend)>();

        var applied = FormulaReferenceEditingController.TryApplyKeyboardSelection(
            new FormulaRangeEditingSession(),
            current,
            target,
            extendSelection: true,
            editor: null,
            (address, extend) =>
            {
                directCalls.Add((address, extend));
                return true;
            },
            _ => throw new InvalidOperationException("Direct selection must not edit formula text."),
            afterEditorEdit: null,
            (_, _, _) => throw new InvalidOperationException("Direct selection must not use fallback."),
            out var result);

        applied.Should().BeTrue();
        result.Route.Should().Be(FormulaKeyboardSelectionRoute.Direct);
        result.Range.Should().Be(new GridRange(current, target));
        directCalls.Should().Equal((target, true));
    }

    [Fact]
    public void TryApplyKeyboardSelection_AddModeAppendsReferenceAndTracksSession()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var current = CellAddress.Parse("B2", sheet.Id);
        var target = CellAddress.Parse("C2", sheet.Id);
        var formulaCell = CellAddress.Parse("A1", sheet.Id);
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(true);
        session.TrackReferenceSpan(5, 2);
        session.TrackSelection(current, current);
        session.TryToggleSelectionMode(FormulaEditorKey.F8, FormulaEditorModifiers.Shift);
        ExcelTextEdit? appliedEdit = null;

        var applied = FormulaReferenceEditingController.TryApplyKeyboardSelection(
            session,
            current,
            target,
            extendSelection: false,
            new FormulaRangeEditorSnapshot(
                "=SUM(B2)",
                CaretIndex: 7,
                SelectionLength: 0,
                FormulaCell: formulaCell,
                UseR1C1ReferenceStyle: false,
                SelectedSheetName: sheet.Name),
            (_, _) => throw new InvalidOperationException("Add mode must not replace the selection."),
            edit => appliedEdit = edit,
            afterEditorEdit: null,
            (_, _, _) => throw new InvalidOperationException("A valid reference must not use fallback."),
            out var result);

        applied.Should().BeTrue();
        result.Route.Should().Be(FormulaKeyboardSelectionRoute.DisjointReference);
        appliedEdit.Should().Be(new ExcelTextEdit("=SUM(B2,C2)", 10, 0));
        session.SelectionAnchor.Should().Be(target);
        session.SelectionCursor.Should().Be(target);
        session.ReferenceSpan.Should().Be(new FormulaReferenceEntrySpan(8, 2));
    }

    [Fact]
    public void TryApplyKeyboardSelection_AddModeFallsBackWhenNoReferenceCanBeAppended()
    {
        var sheetId = SheetId.New();
        var current = new CellAddress(sheetId, 2, 2);
        var target = new CellAddress(sheetId, 2, 3);
        var session = new FormulaRangeEditingSession();
        session.TryToggleSelectionMode(FormulaEditorKey.F8, FormulaEditorModifiers.Shift);
        GridRange? fallbackRange = null;

        var applied = FormulaReferenceEditingController.TryApplyKeyboardSelection(
            session,
            current,
            target,
            extendSelection: false,
            new FormulaRangeEditorSnapshot("=SUM(", 5, 0, current, false, "Sheet1"),
            (_, _) => false,
            _ => throw new InvalidOperationException("An invalid append must not edit formula text."),
            afterEditorEdit: null,
            (range, _, _) =>
            {
                fallbackRange = range;
                return true;
            },
            out var result);

        applied.Should().BeTrue();
        result.Route.Should().Be(FormulaKeyboardSelectionRoute.RangeFallback);
        fallbackRange.Should().Be(new GridRange(target, target));
    }

    [Fact]
    public void BuildHighlights_ResolvesWorkbookSheetAndRange()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");

        var highlights = FormulaReferenceEditingController.BuildHighlights(
            "=SUM(B2:C3)",
            workbook,
            sheet.Id,
            CellAddress.Parse("A1", sheet.Id));

        highlights.Should().ContainSingle();
        highlights[0].Range.Should().Be(new GridRange(
            CellAddress.Parse("B2", sheet.Id),
            CellAddress.Parse("C3", sheet.Id)));
    }

    [Fact]
    public void Reset_ClearsPortableStateBetweenRendererCallbacks()
    {
        var session = new FormulaRangeEditingSession();
        session.SetPointMode(true);
        session.TrackReferenceSpan(1, 2);
        var events = new List<string>();

        FormulaReferenceEditingController.Reset(
            session,
            () => events.Add("autocomplete"),
            () =>
            {
                session.PointMode.Should().BeFalse();
                events.Add("highlights");
            });

        events.Should().Equal("autocomplete", "highlights");
        session.ReferenceSpan.Should().BeNull();
    }
}
