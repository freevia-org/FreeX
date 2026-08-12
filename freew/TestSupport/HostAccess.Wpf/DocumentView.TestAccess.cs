using System.Windows;
using System.Windows.Documents;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Editing;

public sealed partial class DocumentView
{
    internal DocumentViewDepthLayoutPlan ViewDepthLayout => _viewDepthLayout;

    internal static bool SuppressNativeSpellCheckForTests { get; set; }

    internal bool NativeSpellCheckEnabledForTest => SpellCheck.IsEnabled;

    internal void MoveCaretToBlockForTest(int modelBlockIndex, int offset) =>
        PlaceCaretAtModelTextOffset(modelBlockIndex, offset);

    internal void SetSelectionRangeForTest(int anchorBlock, int anchorOffset, int caretBlock, int caretOffset)
    {
        var anchor = TextPointerAtModelTextOffset(anchorBlock, anchorOffset);
        var caret = TextPointerAtModelTextOffset(caretBlock, caretOffset);
        if (anchor is not null && caret is not null)
            Selection.Select(anchor, caret);
    }

    internal DocumentTextRange? BodyTextRangeForTest() =>
        TryGetCurrentBodyTextRange(out var range) ? range : null;

    internal bool ApplyFormatPainterToSelectionForTest() => TryApplyFormatPainter();

    internal bool SimulateTypeCharacter(char c)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return false;

        if (AutoCorrectEnabled && Selection.IsEmpty && TryAutoCorrect(c))
            return true;
        InsertText(c.ToString());
        return false;
    }

    internal void SimulateTypeText(string text)
    {
        foreach (var c in text)
            SimulateTypeCharacter(c);
    }

    internal int ActiveShapeEditPointHandleCount => _shapeEditPointsAdorner?.HandleCount ?? 0;

    internal bool MoveActiveShapeEditPoint(int segmentIndex, long x, long y)
    {
        if (_shapeEditPointsTarget is not { } target || !IsCurrentShapeEditPointsTarget(target))
            return false;

        MoveShapeEditPoint(target, segmentIndex, x, y);
        return true;
    }

    internal DocumentPagination? GetPageBreakAdornerPagination() => _pageBreakAdorner?._pagination;

    internal bool MoveSelectedFloatingGroupChild(double dxPt, double dyPt)
    {
        if (_selectedFloatingGroupChild is not { } selected)
            return false;

        CommitToModel();
        var (blockIndex, runIndex) = FindFloatingObjectLocation(selected.RootGroup);
        if (blockIndex < 0)
            return false;

        var result = ObjectEdits.MoveGroupChildBy(
            ObjectTarget(blockIndex, runIndex, selected.ChildPath),
            dxPt,
            dyPt);
        if (!result.Applied)
            return false;
        SyncFloatingObjectsCanvas();
        return true;
    }

    internal bool ResizeSelectedFloatingGroupChild(
        double widthPt,
        double heightPt,
        double dxPt = 0,
        double dyPt = 0)
    {
        if (_selectedFloatingGroupChild is not { } selected || widthPt <= 0 || heightPt <= 0)
            return false;

        CommitToModel();
        var (blockIndex, runIndex) = FindFloatingObjectLocation(selected.RootGroup);
        if (blockIndex < 0)
            return false;

        var result = ObjectEdits.ResizeGroupChild(
            ObjectTarget(blockIndex, runIndex, selected.ChildPath),
            widthPt,
            heightPt,
            dxPt,
            dyPt);
        if (!result.Applied)
            return false;
        SyncFloatingObjectsCanvas();
        return true;
    }

    internal IReadOnlyList<object> SelectedFloatingObjects => _selectedFloatingObjects.AsReadOnly();

    internal (DrawingGroup Group, int ChildIndex)? SelectedFloatingGroupChild =>
        _selectedFloatingGroupChild is { } selected
            ? (selected.RootGroup, selected.ChildIndex)
            : null;

    internal IReadOnlyList<int>? SelectedFloatingGroupChildPath =>
        _selectedFloatingGroupChild?.ChildPath;

    internal static IReadOnlyList<string> MultiLevelMarkerSequence(
        IEnumerable<int> levels,
        IReadOnlyList<ListNumberFormat>? numberFormats = null) =>
        MultiLevelListMarkerFormatter.MarkerSequence(levels, numberFormats);

    internal static IReadOnlyList<(double StopPositionDip, double SegmentStartDip, double AdvanceDip, TabStopAlignment Alignment, TabLeader Leader, bool IsExplicit)> GetRenderedTabStopPlans(WpfParagraph paragraph)
    {
        var plans = new List<(double, double, double, TabStopAlignment, TabLeader, bool)>();
        CollectRenderedTabStopPlans(paragraph.Inlines, plans);
        return plans;
    }

    private static void CollectRenderedTabStopPlans(
        InlineCollection inlines,
        ICollection<(double StopPositionDip, double SegmentStartDip, double AdvanceDip, TabStopAlignment Alignment, TabLeader Leader, bool IsExplicit)> plans)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case InlineUIContainer { Child: FrameworkElement { Tag: RenderedTabStopSpan marker } }:
                    plans.Add((
                        marker.Plan.StopPositionDip,
                        marker.Plan.SegmentStartDip,
                        marker.Plan.AdvanceDip,
                        marker.Plan.Alignment,
                        marker.Plan.Leader,
                        marker.Plan.IsExplicit));
                    break;
                case Span span:
                    CollectRenderedTabStopPlans(span.Inlines, plans);
                    break;
            }
        }
    }

    internal void BackspaceForTest()
    {
        if (!TryApplyBodyBackspace())
            EditingCommands.Backspace.Execute(null, this);
    }

    internal void DeleteForwardForTest()
    {
        if (!TryApplyBodyDeleteForward())
            EditingCommands.Delete.Execute(null, this);
    }

    internal void InsertParagraphBreakForTest()
    {
        if (!TryApplyBodyParagraphBreak())
            EditingCommands.EnterParagraphBreak.Execute(null, this);
    }

    static partial void ApplyNativeSpellCheckOverride(ref bool suppressed) =>
        suppressed = SuppressNativeSpellCheckForTests;
}
