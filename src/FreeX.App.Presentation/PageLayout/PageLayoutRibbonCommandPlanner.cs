using System.Collections.Generic;

using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Shared command planning for Page Layout ribbon quick actions. Renderers decide how to present menus,
/// dialogs, and status messages; this planner owns the target-sheet-neutral Core command construction.
/// </summary>
public static class PageLayoutRibbonCommandPlanner
{
    public static IWorkbookCommand BuildMarginsCommand(SheetId sheetId, WorksheetPageMargins margins) =>
        new SetPageMarginsCommand(sheetId, margins);

    public static IWorkbookCommand BuildMarginsCommand(
        SheetId sheetId,
        WorksheetPageMargins margins,
        double? headerMargin,
        double? footerMargin) =>
        new SetPageMarginsCommand(sheetId, margins, headerMargin, footerMargin);

    public static IWorkbookCommand BuildOrientationCommand(SheetId sheetId, WorksheetPageOrientation orientation) =>
        new SetPageOrientationCommand(sheetId, orientation);

    public static IWorkbookCommand BuildPaperSizeCommand(SheetId sheetId, WorksheetPaperSize paperSize) =>
        new SetPaperSizeCommand(sheetId, paperSize);

    public static IWorkbookCommand BuildSetPrintAreaCommand(SheetId targetSheetId, GridRange selectedRange) =>
        PageSetupCommandFactory.BuildPrintAreaCommand(targetSheetId, selectedRange);

    public static IWorkbookCommand BuildClearPrintAreaCommand(SheetId sheetId) =>
        PageSetupCommandFactory.BuildPrintAreaCommand(sheetId, null);

    public static IWorkbookCommand BuildSetBackgroundCommand(
        SheetId sheetId,
        WorksheetBackgroundImage background) =>
        new SetWorksheetBackgroundCommand(sheetId, background);

    public static IWorkbookCommand BuildClearBackgroundCommand(SheetId sheetId) =>
        new ClearWorksheetBackgroundCommand(sheetId);

    public static IWorkbookCommand BuildScaleToFitCommand(SheetId sheetId, WorksheetScaleToFit scaleToFit) =>
        new SetScaleToFitCommand(sheetId, scaleToFit);

    public static WorksheetScaleToFit ResolveScaleToFitFromPageDimensions(
        WorksheetScaleToFit current,
        int? pagesWide,
        int? pagesTall) =>
        pagesWide is not null || pagesTall is not null
            ? new WorksheetScaleToFit(null, pagesWide, pagesTall)
            : new WorksheetScaleToFit(current.ScalePercent ?? 100, null, null);

    public static WorksheetScaleToFit ResolveScalePercent(int? percent) =>
        new(percent ?? 100, null, null);

    public static IWorkbookCommand BuildPrintGridlinesCommand(Sheet sheet, bool printGridlines) =>
        BuildPrintOptionsCommand(sheet.Id, printGridlines, sheet.PrintHeadings);

    public static IWorkbookCommand BuildPrintGridlinesCommand(
        SheetId sheetId,
        bool printGridlines,
        bool currentPrintHeadings) =>
        BuildPrintOptionsCommand(sheetId, printGridlines, currentPrintHeadings);

    public static IWorkbookCommand BuildPrintHeadingsCommand(Sheet sheet, bool printHeadings) =>
        BuildPrintOptionsCommand(sheet.Id, sheet.PrintGridlines, printHeadings);

    public static IWorkbookCommand BuildPrintHeadingsCommand(
        SheetId sheetId,
        bool currentPrintGridlines,
        bool printHeadings) =>
        BuildPrintOptionsCommand(sheetId, currentPrintGridlines, printHeadings);

    public static IWorkbookCommand BuildPrintOptionsCommand(
        SheetId sheetId,
        bool printGridlines,
        bool printHeadings) =>
        new SetPrintOptionsCommand(sheetId, printGridlines, printHeadings);

    public static PageBreakSelectionPlan PlanResetPageBreaks() =>
        new([], []);

    public static PageBreakActionPlan PlanPageBreakAction(
        PageBreakMenuAction action,
        GridRange selection,
        IEnumerable<uint> existingRowBreaks,
        IEnumerable<uint> existingColumnBreaks) =>
        PageBreakActionPlanner.Plan(action, selection, existingRowBreaks, existingColumnBreaks);

    public static IWorkbookCommand BuildPageBreaksCommand(SheetId sheetId, PageBreakActionPlan plan) =>
        BuildPageBreaksCommand(sheetId, plan.RowBreaks, plan.ColumnBreaks);

    public static IWorkbookCommand BuildPageBreaksCommand(SheetId sheetId, PageBreakSelectionPlan plan) =>
        BuildPageBreaksCommand(sheetId, plan.RowBreaks, plan.ColumnBreaks);

    public static IWorkbookCommand BuildPageBreaksCommand(
        SheetId sheetId,
        IReadOnlyList<uint> rowBreaks,
        IReadOnlyList<uint> columnBreaks) =>
        new SetPageBreaksCommand(sheetId, rowBreaks, columnBreaks);
}
