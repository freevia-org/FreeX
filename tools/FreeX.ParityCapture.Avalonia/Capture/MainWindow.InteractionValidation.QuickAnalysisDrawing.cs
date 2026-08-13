using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingInteraction;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

using AvaloniaCanvas = Avalonia.Controls.Canvas;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal async Task<IReadOnlyList<InteractionValidationResult>>
        RunQuickAnalysisDrawingInteractionValidationForTestAsync()
    {
        var results = new List<InteractionValidationResult>();
        await AddQuickAnalysisDrawingInteractionResultsAsync(results);
        return results;
    }

    private async Task AddQuickAnalysisDrawingInteractionResultsAsync(
        List<InteractionValidationResult> results)
    {
        var previousConditionalFormatRuleProbe = _conditionalFormatRuleDialogInspectionCallback;
        _conditionalFormatRuleDialogInspectionCallback = AcceptInteractionValidationConditionalFormatRule;
        try
        {
            await AddValidationResult(results, "quick-analysis.conditional-format", "quick-analysis", async () =>
            {
                var sheet = _session.ActiveSheet;
                var range = SeedQuickAnalysisFixture(sheet);
                _session.SelectRange(range);
                var request = QuickAnalysisShellRequestPlanner.Build(
                    sheet,
                    range,
                    QuickAnalysisShellCapabilities.DialogBacked);
                var item = request.ShellPlan.AllItems().Single(candidate => candidate.Id == "format.databars");
                var beforeRules = sheet.ConditionalFormats.Count;
                var beforeUndo = _session.GetUndoHistory(100).Count;
                await ApplyQuickAnalysisItemAsync(item);
                var afterRules = sheet.ConditionalFormats.Count;
                var afterUndo = _session.GetUndoHistory(100).Count;
                var rule = sheet.ConditionalFormats.LastOrDefault();
                var appliedToSelection = rule?.AppliesTo == range;
                var passed = request.CanOpen && afterRules == beforeRules + 1 &&
                    afterUndo == beforeUndo + 1 && appliedToSelection;
                return ValidationEvidence(
                    passed,
                    $"selection={FormatRangeReference(range)}; rules={beforeRules}->{afterRules}; " +
                    $"undo={beforeUndo}->{afterUndo}; ruleType={rule?.RuleType.ToString() ?? "none"}; " +
                    $"appliesToSelection={appliedToSelection}",
                    "Quick Analysis format.databars was dispatched through the production host operation and conditional-format command path.");
            });

            await AddValidationResult(results, "quick-analysis.total", "quick-analysis", async () =>
            {
                var sheet = _session.ActiveSheet;
                var range = SeedQuickAnalysisFixture(sheet);
                _session.SelectRange(range);
                var targetColumn = range.End.Col + 1;
                var beforeFormulas = CountFormulas(sheet, range.Start.Row, range.End.Row, targetColumn);
                var beforeUndo = _session.GetUndoHistory(100).Count;
                var request = QuickAnalysisShellRequestPlanner.Build(
                    sheet,
                    range,
                    QuickAnalysisShellCapabilities.DialogBacked);
                var item = request.ShellPlan.AllItems().Single(candidate => candidate.Id == "total.sum");
                var operation = QuickAnalysisHostOperationPlanner.Plan(item);
                var expectedEdits = QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(
                    operation,
                    range,
                    out var plannedEdits)
                    ? plannedEdits
                    : [];
                await ApplyQuickAnalysisItemAsync(item);
                var afterFormulas = CountFormulas(sheet, range.Start.Row, range.End.Row, targetColumn);
                var afterUndo = _session.GetUndoHistory(100).Count;
                var exactFormulas = expectedEdits.Count > 0 && expectedEdits.All(edit =>
                    sheet.GetCell(edit.Address)?.FormulaText == edit.NewCell.FormulaText);
                var passed = request.CanOpen && afterFormulas == expectedEdits.Count && exactFormulas &&
                    afterUndo == beforeUndo + 1;
                return ValidationEvidence(
                    passed,
                    $"selection={FormatRangeReference(range)}; formulaCells={beforeFormulas}->{afterFormulas}; " +
                    $"formulasAfter={FormulaSummary(sheet, range.Start.Row, range.End.Row, targetColumn)}; " +
                    $"expectedEdits={expectedEdits.Count}; exactFormulas={exactFormulas}; " +
                    $"undo={beforeUndo}->{afterUndo}; targetColumn={targetColumn}",
                    "Quick Analysis total.sum was dispatched through the production host operation and EditCells command path.");
            });

            var shape = SeedInteractionValidationShape();
            await AddShapePointerValidation(results, "drawing.shape.move", ObjectDragKind.Move, shape);
            await AddShapePointerValidation(results, "drawing.shape.resize", ObjectDragKind.ResizeSE, shape);
            await AddShapePointerValidation(results, "drawing.shape.rotate", ObjectDragKind.Rotate, shape);
            await AddShapeCaptureLossValidation(results, shape);
        }
        finally
        {
            _conditionalFormatRuleDialogInspectionCallback = previousConditionalFormatRuleProbe;
        }
    }

    private static void AcceptInteractionValidationConditionalFormatRule(
        ConditionalFormatRuleDialogInspection probe)
    {
        var dataBarPresetIndex = ConditionalFormatPresetChoices
            .ToList()
            .FindIndex(choice => choice.Preset == ConditionalFormatPreset.DataBar);
        if (dataBarPresetIndex >= 0)
            probe.PresetBox.SelectedIndex = dataBarPresetIndex;

        probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, probe.OkButton));
    }

    private async Task AddShapePointerValidation(
        List<InteractionValidationResult> results,
        string id,
        ObjectDragKind kind,
        DrawingShapeModel shape)
    {
        await AddValidationResult(results, id, "drawing-pointer", () =>
        {
            var before = ShapeGeometrySnapshot.From(shape);
            var beforeUndo = _session.GetUndoHistory(100).Count;
            var (container, surface, adorner, _, renderPlan) = CreateValidationShapeSurface(shape);
            var objectRect = new LayoutRect(
                DrawingObjectSelectionHorizontalPadding,
                DrawingObjectSelectionTopPadding,
                shape.Width,
                shape.Height);
            var start = kind == ObjectDragKind.Rotate
                ? ObjectDragPlanner.RotateHandleCenter(kind, objectRect, shape.RotationDegrees)
                : ObjectDragPlanner.RotateHandleCenter(kind, objectRect, 0);
            var canvasStart = new LayoutPoint(
                AvaloniaCanvas.GetLeft(container) + start.X,
                AvaloniaCanvas.GetTop(container) + start.Y);
            var started = TryBeginDrawingObjectDragAtPoint(
                renderPlan,
                container,
                surface,
                adorner,
                start,
                canvasStart,
                kind);
            if (!started || _drawingObjectDragSession is not { } session)
                return ValidationEvidence(false, $"kind={kind}; started={started}", "Production drawing pointer state machine did not begin the requested gesture.");

            var target = kind switch
            {
                ObjectDragKind.Move => new LayoutPoint(canvasStart.X + 140, canvasStart.Y + 30),
                ObjectDragKind.Rotate => new LayoutPoint(session.StartCanvasRect.Center.X, session.StartCanvasRect.Bottom + 80),
                _ => new LayoutPoint(canvasStart.X + 35, canvasStart.Y + 25),
            };
            ContinueDrawingObjectDragAtPoint(session, target);
            var expectedRotation = session.CurrentRotationDegrees;
            var previewChanged = session.Moved &&
                (kind == ObjectDragKind.Move
                    ? session.CurrentCanvasRect != session.StartCanvasRect
                    : kind == ObjectDragKind.Rotate
                        ? Math.Abs(session.CurrentRotationDegrees - session.StartRotationDegrees) > 0.1
                        : Math.Abs(session.CurrentCanvasRect.Width - session.StartCanvasRect.Width) > 0.1);
            _drawingObjectDragSession = null;
            CommitDrawingObjectDrag(session);
            var after = ShapeGeometrySnapshot.From(shape);
            var afterUndo = _session.GetUndoHistory(100).Count;
            var changed = kind switch
            {
                ObjectDragKind.Move => after.Anchor != before.Anchor,
                ObjectDragKind.Rotate => Math.Abs(after.RotationDegrees - before.RotationDegrees) > 0.1 &&
                    Math.Abs(after.RotationDegrees - expectedRotation) < 0.1,
                _ => Math.Abs(after.Width - before.Width) > 0.1 || Math.Abs(after.Height - before.Height) > 0.1,
            };
            return ValidationEvidence(
                previewChanged && changed && afterUndo == beforeUndo + 1,
                $"kind={kind}; modelBefore={before}; modelAfter={after}; previewChanged={previewChanged}; " +
                $"expectedRotation={expectedRotation}; undo={beforeUndo}->{afterUndo}",
                "The production pointer gesture state machine previewed geometry, committed its real drawing command, and refreshed from model state.");
        });
    }

    private async Task AddShapeCaptureLossValidation(
        List<InteractionValidationResult> results,
        DrawingShapeModel shape)
    {
        await AddValidationResult(results, "drawing.shape.capture-loss-no-op", "drawing-pointer", () =>
        {
            var before = ShapeGeometrySnapshot.From(shape);
            var beforeUndo = _session.GetUndoHistory(100).Count;
            var (container, surface, adorner, _, renderPlan) = CreateValidationShapeSurface(shape);
            var objectRect = new LayoutRect(
                DrawingObjectSelectionHorizontalPadding,
                DrawingObjectSelectionTopPadding,
                shape.Width,
                shape.Height);
            var start = ObjectDragPlanner.RotateHandleCenter(
                ObjectDragKind.ResizeSE,
                objectRect,
                shape.RotationDegrees);
            var canvasStart = new LayoutPoint(
                AvaloniaCanvas.GetLeft(container) + start.X,
                AvaloniaCanvas.GetTop(container) + start.Y);
            var started = TryBeginDrawingObjectDragAtPoint(
                renderPlan,
                container,
                surface,
                adorner,
                start,
                canvasStart,
                ObjectDragKind.ResizeSE);
            if (!started || _drawingObjectDragSession is not { } session)
                return ValidationEvidence(false, $"started={started}", "Production drawing pointer state machine did not begin the capture-loss gesture.");

            ContinueDrawingObjectDragAtPoint(session, new LayoutPoint(canvasStart.X + 45, canvasStart.Y + 35));
            var previewChanged = session.Moved &&
                Math.Abs(session.CurrentCanvasRect.Width - session.StartCanvasRect.Width) > 0.1;
            CancelDrawingObjectDrag(container);
            var after = ShapeGeometrySnapshot.From(shape);
            var afterUndo = _session.GetUndoHistory(100).Count;
            var restored = after == before && _drawingObjectDragSession is null;
            return ValidationEvidence(
                previewChanged && restored && afterUndo == beforeUndo,
                $"modelBefore={before}; modelAfter={after}; previewChanged={previewChanged}; " +
                $"restored={restored}; undo={beforeUndo}->{afterUndo}; commandAdded=false",
                "The production capture-loss cancellation path discarded the live preview, refreshed the shell from model geometry, and added no command.");
        });
    }

    private (Control Container, AvaloniaGrid Surface, AvaloniaCanvas Adorner, AvaloniaCanvas Canvas, DrawingObjectRenderPlan RenderPlan)
        CreateValidationShapeSurface(DrawingShapeModel shape)
    {
        var renderPlan = new DrawingObjectRenderPlan(
            new DrawingObjectBounds(
                SelectionPaneObjectKind.Shape,
                shape.Id,
                shape.Name ?? "Interaction validation shape",
                shape.Anchor.Row,
                shape.Anchor.Col,
                0,
                0,
                shape.Width,
                shape.Height,
                shape.RotationDegrees,
                shape.FlipHorizontal,
                shape.FlipVertical,
                shape.Kind,
                FillColor: shape.FillColor,
                OutlineColor: shape.OutlineColor,
                ShapeText: shape.ShapeText),
            DrawingObjectRenderPrimitiveKind.Shape);
        var container = (AvaloniaGrid)CreateSelectableDrawingObjectVisual(renderPlan, shape.Width, shape.Height);
        var surface = (AvaloniaGrid)container.Children[0]!;
        var adorner = (AvaloniaCanvas)container.Children[1]!;
        var canvas = new AvaloniaCanvas { Width = 900, Height = 700 };
        Canvas.SetLeft(container, 80);
        Canvas.SetTop(container, 100);
        canvas.Children.Add(container);
        container.Measure(new global::Avalonia.Size(900, 700));
        container.Arrange(new global::Avalonia.Rect(0, 0, container.Width, container.Height));
        return (container, surface, adorner, canvas, renderPlan);
    }

    private DrawingShapeModel SeedInteractionValidationShape()
    {
        var sheet = _session.ActiveSheet;
        var shape = new DrawingShapeModel
        {
            Name = "Interaction validation shape",
            Anchor = new CellAddress(sheet.Id, 6, 2),
            Width = 150,
            Height = 90,
            FillColor = new CellColor(91, 155, 213),
            OutlineColor = new CellColor(47, 84, 150),
            Locked = false,
        };
        sheet.DrawingShapes.Add(shape);
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
        _selectedDrawingObjectKind = SelectionPaneObjectKind.Shape;
        _selectedDrawingObjectId = shape.Id;
        RefreshShell("Interaction validation shape ready");
        return shape;
    }

    private static GridRange SeedQuickAnalysisFixture(Sheet sheet)
    {
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 5, 4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Q2"));
        for (uint row = 3; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"R{row - 2}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(row * 15));
        }
        return range;
    }

    private static int CountFormulas(Sheet sheet, uint startRow, uint endRow, uint col) =>
        Enumerable.Range((int)startRow, checked((int)(endRow - startRow + 1)))
            .Count(row => sheet.GetCell(new CellAddress(sheet.Id, (uint)row, col))?.FormulaText is not null);

    private static string FormulaSummary(Sheet sheet, uint startRow, uint endRow, uint col) =>
        string.Join(",", Enumerable.Range((int)startRow, checked((int)(endRow - startRow + 1)))
            .Select(row => sheet.GetCell(new CellAddress(sheet.Id, (uint)row, col))?.FormulaText ?? "<empty>"));

    private static async Task AddValidationResult(
        List<InteractionValidationResult> results,
        string id,
        string category,
        Func<Task<InteractionValidationEvidence>> probe)
    {
        try
        {
            var evidence = await probe();
            results.Add(new InteractionValidationResult(
                id,
                category,
                evidence.Status,
                evidence.Level,
                evidence.Text,
                evidence.Note));
        }
        catch (Exception ex)
        {
            results.Add(new InteractionValidationResult(
                id,
                category,
                "not-proven",
                "production-probe-threw",
                $"{ex.GetType().Name}: {ex.Message}",
                "The bounded production probe could not establish its postcondition."));
        }
    }

    private static Task AddValidationResult(
        List<InteractionValidationResult> results,
        string id,
        string category,
        Func<InteractionValidationEvidence> probe)
    {
        try
        {
            var evidence = probe();
            results.Add(new InteractionValidationResult(
                id,
                category,
                evidence.Status,
                evidence.Level,
                evidence.Text,
                evidence.Note));
        }
        catch (Exception ex)
        {
            results.Add(new InteractionValidationResult(
                id,
                category,
                "not-proven",
                "production-probe-threw",
                $"{ex.GetType().Name}: {ex.Message}",
                "The bounded production probe could not establish its postcondition."));
        }

        return Task.CompletedTask;
    }

    private static InteractionValidationEvidence ValidationEvidence(
        bool passed,
        string text,
        string note) => new(passed ? "passed" : "failed", "production-model-observed", text, note);

    private sealed record InteractionValidationEvidence(
        string Status,
        string Level,
        string Text,
        string Note);

    private readonly record struct ShapeGeometrySnapshot(
        CellAddress Anchor,
        double Width,
        double Height,
        double RotationDegrees,
        bool FlipHorizontal,
        bool FlipVertical)
    {
        public static ShapeGeometrySnapshot From(DrawingShapeModel shape) => new(
            shape.Anchor,
            shape.Width,
            shape.Height,
            shape.RotationDegrees,
            shape.FlipHorizontal,
            shape.FlipVertical);
    }
}
