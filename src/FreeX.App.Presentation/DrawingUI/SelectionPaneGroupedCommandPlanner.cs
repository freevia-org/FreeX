using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DrawingUI;

public static class SelectionPaneGroupedCommandPlanner
{
    public static bool HasChanges(SelectionPaneDialogResult result) =>
        result.RenameChanges.Count > 0 ||
        result.VisibilityChanges.Count > 0 ||
        result.MoveChanges.Count > 0 ||
        result.DeleteChanges.Count > 0;

    public static IWorkbookCommand CreateCommand(
        Workbook workbook,
        SheetId currentSheetId,
        SheetId targetSheetId,
        SelectionPaneDialogResult result)
    {
        var commands = new List<IWorkbookCommand>(
            result.RenameChanges.Count + result.VisibilityChanges.Count + result.MoveChanges.Count + result.DeleteChanges.Count);
        var isCurrentSheet = targetSheetId == currentSheetId;

        foreach (var change in result.RenameChanges)
        {
            if (TryResolveChangeTarget(workbook, currentSheetId, targetSheetId, change.Kind, change.Id, isCurrentSheet, out var targetId, out var shouldFail))
            {
                commands.Add(new RenameSelectionPaneObjectCommand(targetSheetId, change.Kind, targetId, change.Name));
            }
            else if (shouldFail)
            {
                commands.Add(CreateMissingTargetCommand());
            }
        }

        foreach (var change in result.VisibilityChanges)
        {
            if (TryResolveChangeTarget(workbook, currentSheetId, targetSheetId, change.Kind, change.Id, isCurrentSheet, out var targetId, out var shouldFail))
            {
                commands.Add(new SetSelectionPaneObjectVisibilityCommand(targetSheetId, change.Kind, targetId, change.IsVisible));
            }
            else if (shouldFail)
            {
                commands.Add(CreateMissingTargetCommand());
            }
        }

        foreach (var change in result.MoveChanges)
        {
            if (TryResolveChangeTarget(workbook, currentSheetId, targetSheetId, change.Kind, change.Id, isCurrentSheet, out var targetId, out var shouldFail))
            {
                commands.Add(new MoveSelectionPaneObjectCommand(targetSheetId, change.Kind, targetId, change.Forward));
            }
            else if (shouldFail)
            {
                commands.Add(CreateMissingTargetCommand());
            }
        }

        // R125-selection-pane-delete-wiring: same DeleteDrawingObjectCommand the sheet grid's
        // Delete key / context menu use (DrawingObjectCommandPlanner.BuildDeleteCommand) -- not a
        // second deletion path. Applied last, after any rename/visibility/move on OTHER objects in
        // this same OK, so those aren't disturbed by an id that's about to disappear.
        foreach (var change in result.DeleteChanges)
        {
            if (TryResolveChangeTarget(workbook, currentSheetId, targetSheetId, change.Kind, change.Id, isCurrentSheet, out var targetId, out var shouldFail))
            {
                commands.Add(new DeleteDrawingObjectCommand(targetSheetId, change.Kind, targetId));
            }
            else if (shouldFail)
            {
                commands.Add(CreateMissingTargetCommand());
            }
        }

        return new CompositeWorkbookCommand("Selection Pane", commands);
    }

    private static bool TryResolveChangeTarget(
        Workbook workbook,
        SheetId currentSheetId,
        SheetId targetSheetId,
        SelectionPaneObjectKind kind,
        Guid sourceId,
        bool isCurrentSheet,
        out Guid targetId,
        out bool shouldFail)
    {
        shouldFail = false;
        if (isCurrentSheet)
        {
            targetId = sourceId;
            return true;
        }

        if (!IsGroupedObjectKind(kind))
        {
            targetId = Guid.Empty;
            return false;
        }

        var sourceSheet = workbook.GetSheet(currentSheetId);
        var targetSheet = workbook.GetSheet(targetSheetId);
        if (sourceSheet is null || targetSheet is null)
        {
            targetId = Guid.Empty;
            shouldFail = true;
            return false;
        }

        if (!TryCreateSignature(sourceSheet, kind, sourceId, out var signature))
        {
            targetId = Guid.Empty;
            shouldFail = true;
            return false;
        }

        if (TryGetObjectAtKindIndex(targetSheet, kind, signature.KindIndex, out var indexedTarget) &&
            SameCell(indexedTarget.Anchor, signature.Anchor))
        {
            targetId = indexedTarget.Id;
            return true;
        }

        if (TryGetObjectAtAnchorOrdinal(targetSheet, kind, signature.Anchor, signature.AnchorOrdinal, out var anchorTarget))
        {
            targetId = anchorTarget.Id;
            return true;
        }

        targetId = Guid.Empty;
        shouldFail = true;
        return false;
    }

    private static bool TryCreateSignature(
        Sheet sheet,
        SelectionPaneObjectKind kind,
        Guid sourceId,
        out SelectionPaneObjectSignature signature)
    {
        var sourceIndex = -1;
        var sourceAnchor = default(CellAddress);
        for (var index = 0; index < GetObjectCount(sheet, kind); index++)
        {
            var item = GetObjectAt(sheet, kind, index);
            if (item.Id == sourceId)
            {
                sourceIndex = index;
                sourceAnchor = item.Anchor;
                break;
            }
        }

        if (sourceIndex < 0)
        {
            signature = default;
            return false;
        }

        var anchorOrdinal = 0;
        for (var index = 0; index < sourceIndex; index++)
        {
            if (SameCell(GetObjectAt(sheet, kind, index).Anchor, sourceAnchor))
                anchorOrdinal++;
        }

        signature = new SelectionPaneObjectSignature(sourceAnchor, sourceIndex, anchorOrdinal);
        return true;
    }

    private static bool TryGetObjectAtKindIndex(
        Sheet sheet,
        SelectionPaneObjectKind kind,
        int index,
        out SelectionPaneObjectSnapshot snapshot)
    {
        if (index >= 0 && index < GetObjectCount(sheet, kind))
        {
            snapshot = GetObjectAt(sheet, kind, index);
            return true;
        }

        snapshot = default;
        return false;
    }

    private static bool TryGetObjectAtAnchorOrdinal(
        Sheet sheet,
        SelectionPaneObjectKind kind,
        CellAddress sourceAnchor,
        int anchorOrdinal,
        out SelectionPaneObjectSnapshot snapshot)
    {
        var currentOrdinal = 0;
        for (var index = 0; index < GetObjectCount(sheet, kind); index++)
        {
            var item = GetObjectAt(sheet, kind, index);
            if (!SameCell(item.Anchor, sourceAnchor))
                continue;

            if (currentOrdinal == anchorOrdinal)
            {
                snapshot = item;
                return true;
            }

            currentOrdinal++;
        }

        snapshot = default;
        return false;
    }

    private static int GetObjectCount(Sheet sheet, SelectionPaneObjectKind kind) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => sheet.Pictures.Count,
            SelectionPaneObjectKind.Shape => sheet.DrawingShapes.Count,
            SelectionPaneObjectKind.TextBox => sheet.TextBoxes.Count,
            _ => 0
        };

    private static SelectionPaneObjectSnapshot GetObjectAt(Sheet sheet, SelectionPaneObjectKind kind, int index) =>
        kind switch
        {
            SelectionPaneObjectKind.Picture => new SelectionPaneObjectSnapshot(
                sheet.Pictures[index].Id,
                sheet.Pictures[index].Anchor),
            SelectionPaneObjectKind.Shape => new SelectionPaneObjectSnapshot(
                sheet.DrawingShapes[index].Id,
                sheet.DrawingShapes[index].Anchor),
            SelectionPaneObjectKind.TextBox => new SelectionPaneObjectSnapshot(
                sheet.TextBoxes[index].Id,
                sheet.TextBoxes[index].Anchor),
            _ => default
        };

    private static bool IsGroupedObjectKind(SelectionPaneObjectKind kind) =>
        kind is SelectionPaneObjectKind.Picture or SelectionPaneObjectKind.Shape or SelectionPaneObjectKind.TextBox;

    private static bool SameCell(CellAddress left, CellAddress right) =>
        left.Row == right.Row && left.Col == right.Col;

    private static IWorkbookCommand CreateMissingTargetCommand() =>
        new MissingSelectionPaneObjectCommand();

    private readonly record struct SelectionPaneObjectSignature(
        CellAddress Anchor,
        int KindIndex,
        int AnchorOrdinal);

    private readonly record struct SelectionPaneObjectSnapshot(Guid Id, CellAddress Anchor);

    private sealed class MissingSelectionPaneObjectCommand : IWorkbookCommand
    {
        public string Label => "Selection Pane";

        public CommandOutcome Apply(ICommandContext ctx) =>
            new(false, "Selection pane object was not found.");

        public void Revert(ICommandContext ctx)
        {
        }
    }
}
