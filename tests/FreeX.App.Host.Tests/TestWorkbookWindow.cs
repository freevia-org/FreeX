using System.Collections.Generic;
using System.Windows;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

internal sealed class TestWorkbookWindow : IWorkbookWindow
{
    // Defaults to the same (default) id for every fake, so tests that model several views of
    // one document need no setup; multi-document tests assign distinct ids explicitly.
    public WorkbookId DocumentId { get; set; }

    public string? Suffix { get; private set; }
    public int RefreshCount { get; private set; }
    public int RefreshTitleBarCount { get; private set; }
    public int ActivateCount { get; private set; }
    public bool IsWindowVisible { get; private set; } = true;
    public int SetVisibleTrueCount { get; private set; }
    public int SetVisibleFalseCount { get; private set; }
    public WorkbookScrollOffset Offset { get; set; }
    public int SetScrollOffsetCount { get; private set; }
    public List<Rect> ArrangedBounds { get; } = [];
    public List<Rect> TiledBounds => ArrangedBounds;
    public int FormulaBarVisibilityAppliedCount { get; private set; }
    public bool? LastAppliedFormulaBarVisibility { get; private set; }
    public int SaveGateHoldCount { get; private set; }
    public int SaveInProgressAppliedCount { get; private set; }
    public bool? LastAppliedSaveInProgress { get; private set; }

    public void ApplyWindowTitleSuffix(string suffix) => Suffix = suffix;

    public void RefreshFromSharedWorkbook() => RefreshCount++;

    public void RefreshTitleBar() => RefreshTitleBarCount++;

    public void ActivateWindow() => ActivateCount++;

    public void SetWindowVisible(bool visible)
    {
        IsWindowVisible = visible;
        if (visible)
            SetVisibleTrueCount++;
        else
            SetVisibleFalseCount++;
    }

    public WorkbookScrollOffset GetScrollOffset() => Offset;

    public void SetScrollOffset(WorkbookScrollOffset offset)
    {
        Offset = offset;
        SetScrollOffsetCount++;
    }

    public void TileToWorkArea(Rect bounds) => ArrangedBounds.Add(bounds);

    public void ApplyFormulaBarVisibility(bool visible)
    {
        LastAppliedFormulaBarVisibility = visible;
        FormulaBarVisibilityAppliedCount++;
    }

    public void ApplySaveInProgress(bool inProgress)
    {
        LastAppliedSaveInProgress = inProgress;
        SaveInProgressAppliedCount++;
        SaveGateHoldCount = inProgress ? SaveGateHoldCount + 1 : Math.Max(0, SaveGateHoldCount - 1);
    }
}
