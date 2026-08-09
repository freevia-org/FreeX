using System.Windows;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private WorkbookApplicationCommandBindings? _workbookApplicationCommandBindings;

    private WorkbookApplicationCommandBindings WorkbookApplicationCommands =>
        _workbookApplicationCommandBindings ??= CreateWorkbookApplicationCommandBindings();

    private WorkbookApplicationCommandBindings CreateWorkbookApplicationCommandBindings()
    {
        var bindings = new WorkbookApplicationCommandBindings();

        bindings.BindAsync(WorkbookApplicationCommandIntent.NewWorkbook, _ => RequestNewWorkbookAsync());
        bindings.Bind(WorkbookApplicationCommandIntent.OpenWorkbook, invocation =>
            OpenButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.SaveWorkbook, invocation =>
            SaveButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.SaveWorkbookAs, invocation =>
            SaveAsButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.PrintWorkbook, invocation =>
        {
            if (invocation.Route.Source == WorkbookApplicationCommandSource.KeyboardShortcut)
                OpenPrintBackstage();
            else
                PrintButton_Click(NativeSource(invocation), RoutedArgs(invocation));
        });
        bindings.Bind(WorkbookApplicationCommandIntent.ExportPdfXps, invocation =>
            ExportPdfButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.Undo, _ => ExecuteUndo());
        bindings.Bind(WorkbookApplicationCommandIntent.Redo, _ => ExecuteRedo());
        bindings.Bind(WorkbookApplicationCommandIntent.Cut, _ => ExecuteCopy(isCut: true));
        bindings.Bind(WorkbookApplicationCommandIntent.Copy, _ => ExecuteCopy());
        bindings.Bind(WorkbookApplicationCommandIntent.Paste, _ => ExecutePaste());
        bindings.Bind(WorkbookApplicationCommandIntent.PasteSpecial, _ =>
            PasteSpecialBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.FormatPainter, invocation =>
            FormatPainterBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleBold, invocation =>
        {
            if (invocation.Route.Source == WorkbookApplicationCommandSource.QuickAccessToolbar)
                ExecuteToggleQuickAccessCommand("Bold", BoldButton_Click);
            else
                ApplyFontToggleShortcut(FontToggleShortcut.Bold);
        });
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleItalic, invocation =>
        {
            if (invocation.Route.Source == WorkbookApplicationCommandSource.QuickAccessToolbar)
                ExecuteToggleQuickAccessCommand("Italic", ItalicButton_Click);
            else
                ApplyFontToggleShortcut(FontToggleShortcut.Italic);
        });
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleUnderline, invocation =>
        {
            if (invocation.Route.Source == WorkbookApplicationCommandSource.QuickAccessToolbar)
                ExecuteToggleQuickAccessCommand("Underline", UnderlineButton_Click);
            else
                ApplyFontToggleShortcut(FontToggleShortcut.Underline);
        });
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleStrikethrough, _ =>
            ApplyFontToggleShortcut(FontToggleShortcut.Strikethrough));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenFillColor, invocation =>
            FillColorBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenFontColor, invocation =>
            FontColorBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenFormatCells, _ => OpenFormatCellsDialog());
        bindings.Bind(WorkbookApplicationCommandIntent.InsertFunction, invocation =>
            InsertFunctionBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.AutoSum, _ => InsertAutoSumFormula("SUM"));
        bindings.Bind(WorkbookApplicationCommandIntent.CalculateNow, invocation =>
            CalcNowBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.CalculateActiveSheet, invocation =>
            CalcSheetBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.RefreshAll, invocation =>
            RefreshAllBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.SortAscending, invocation =>
            SortAscButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.SortDescending, invocation =>
            SortDescButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.CustomSort, _ =>
            SortCustomMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleFilter, invocation =>
            FilterButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ClearFilter, _ =>
            ClearFilterButton_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ReapplyFilter, invocation =>
        {
            if (invocation.Route.Source == WorkbookApplicationCommandSource.KeyboardShortcut)
                ReapplyAutoFilter();
            else
                FilterReapplyMenuItem_Click(this, new RoutedEventArgs());
        });
        bindings.Bind(WorkbookApplicationCommandIntent.OpenDataValidation, invocation =>
            ValidationButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenNameManager, invocation =>
            NamedRangesButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenSpelling, invocation =>
            SpellCheckBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.CheckAccessibility, invocation =>
            AccessibilityCheckerBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ShareWorkbook, invocation =>
            ShareWorkbookBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.Zoom100, invocation =>
            Zoom100Btn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ZoomSelection, invocation =>
            ZoomSelectionBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.FreezePanes, invocation =>
            FreezeAtSelectionMenuItem_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertWorksheet, invocation =>
            AddSheetButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.Find, invocation =>
            FindButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.Replace, invocation =>
            ReplaceButton_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.GoTo, invocation =>
            FindGoToMenuItem_Click(NativeSource(invocation), RoutedArgs(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.OpenSelectionPane, invocation =>
            SelectionPaneBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));

        bindings.Bind(WorkbookApplicationCommandIntent.InsertCopiedCells, _ => ExecuteInsertCopiedCells());
        bindings.Bind(WorkbookApplicationCommandIntent.InsertCells, _ =>
            InsertCellsMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertRowAbove, invocation =>
            InsertRows(TargetAddress(invocation).Row));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertRowBelow, invocation =>
            InsertRows(TargetAddress(invocation).Row + 1));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertColumnLeft, invocation =>
            InsertColumns(TargetAddress(invocation).Col));
        bindings.Bind(WorkbookApplicationCommandIntent.InsertColumnRight, invocation =>
            InsertColumns(TargetAddress(invocation).Col + 1));
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteCells, _ =>
            DeleteCellsMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteRows, _ => DeleteSelectedRows());
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteColumns, _ => DeleteSelectedColumns());
        bindings.Bind(WorkbookApplicationCommandIntent.PickFromDropDown, _ => OpenActiveDropdown());
        bindings.Bind(WorkbookApplicationCommandIntent.QuickAnalysis, _ => ShowQuickAnalysisMenu());
        bindings.Bind(WorkbookApplicationCommandIntent.DefineName, _ =>
            DefineNameBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.CreateTable, _ =>
            TableBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.FormatAsTable, _ =>
            FormatTableBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.TextToColumns, _ =>
            TextToColumnsBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.RemoveDuplicates, _ =>
            RemoveDuplicatesBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.HideRows, _ => ExecuteRowsHidden(hidden: true));
        bindings.Bind(WorkbookApplicationCommandIntent.UnhideRows, _ => ExecuteRowsHidden(hidden: false));
        bindings.Bind(WorkbookApplicationCommandIntent.RowHeight, _ =>
            FormatRowHeightMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.AutoFitRowHeight, _ =>
            FormatAutoRowMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.HideColumns, _ => ExecuteColumnsHidden(hidden: true));
        bindings.Bind(WorkbookApplicationCommandIntent.UnhideColumns, _ => ExecuteColumnsHidden(hidden: false));
        bindings.Bind(WorkbookApplicationCommandIntent.ColumnWidth, _ =>
            FormatColWidthMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.AutoFitColumnWidth, _ =>
            FormatAutoColMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.Group, _ =>
            GroupRowsBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.Ungroup, _ =>
            UngroupRowsBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.NewThreadedComment, _ =>
            ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.EditThreadedComment, _ =>
            ReviewNewThreadedCommentBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ResolveThreadedComment, invocation =>
            ResolveContextThreadedComment(TargetAddress(invocation), resolved: true));
        bindings.Bind(WorkbookApplicationCommandIntent.UnresolveThreadedComment, invocation =>
            ResolveContextThreadedComment(TargetAddress(invocation), resolved: false));
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteThreadedComment, _ =>
            ReviewDeleteThreadedCommentBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.NewNote, _ =>
            ReviewNewCommentBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.EditNote, _ =>
            ReviewNewCommentBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.DeleteNote, _ =>
            ReviewDeleteCommentBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ShowNotes, _ =>
            ReviewShowNotesBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ShowHideNote, invocation =>
            ExecuteShowHideNote(TargetAddress(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ShowAllNotes, _ => ExecuteShowAllNotes());
        bindings.Bind(WorkbookApplicationCommandIntent.OpenHyperlink, invocation =>
            TryOpenHyperlink(TargetAddress(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.EditHyperlink, _ =>
            InsertLinkBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.PivotTableOptions, invocation =>
            ShowPivotTableOptionsDialog(TargetAddress(invocation)));
        bindings.Bind(WorkbookApplicationCommandIntent.ClearAll, _ =>
            ClearAllMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ClearFormats, _ => ClearFormats());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearComments, _ =>
            ClearCommentsMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ClearHyperlinks, _ =>
            ClearHyperlinksMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.RemoveHyperlinks, _ =>
            RemoveHyperlinkMenuItem_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ClearContents, _ => ExecuteClearSelection());

        bindings.Bind(WorkbookApplicationCommandIntent.FillDown, _ =>
        {
            if (!HasSelectedDrawingObject())
                FillDownMenuItem_Click(this, new RoutedEventArgs());
        });
        bindings.Bind(WorkbookApplicationCommandIntent.FillRight, _ =>
        {
            if (!HasSelectedDrawingObject())
                FillRightMenuItem_Click(this, new RoutedEventArgs());
        });
        bindings.Bind(WorkbookApplicationCommandIntent.FlashFill, _ => TryFlashFill());
        bindings.Bind(WorkbookApplicationCommandIntent.ToggleShowFormulas, _ =>
            ShowFormulasBtn_Click(this, new RoutedEventArgs()));
        bindings.Bind(WorkbookApplicationCommandIntent.ActivatePreviousSheet, _ => ActivateAdjacentVisibleSheet(-1));
        bindings.Bind(WorkbookApplicationCommandIntent.ActivateNextSheet, _ => ActivateAdjacentVisibleSheet(1));
        bindings.Bind(WorkbookApplicationCommandIntent.SelectPreviousSheetGroup, _ =>
            SelectAdjacentVisibleSheetGroup(-1));
        bindings.Bind(WorkbookApplicationCommandIntent.SelectNextSheetGroup, _ =>
            SelectAdjacentVisibleSheetGroup(1));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatGeneral, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.General));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatNumber, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.Number));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatTime, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.Time));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatDate, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.Date));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatCurrency, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.Currency));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatPercentage, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.Percentage));
        bindings.Bind(WorkbookApplicationCommandIntent.NumberFormatScientific, _ =>
            ApplyNumberFormatShortcut(NumberFormatShortcut.Scientific));
        bindings.Bind(WorkbookApplicationCommandIntent.ApplyOutlineBorder, _ => ApplyOutlineBorderShortcut());
        bindings.Bind(WorkbookApplicationCommandIntent.ClearOutlineBorder, _ =>
            ApplyStyleDiff(BorderShortcutService.GetClearBorderDiff()));
        bindings.Bind(WorkbookApplicationCommandIntent.WorkbookStatistics, invocation =>
            WorkbookStatisticsBtn_Click(NativeSource(invocation), RoutedArgs(invocation)));

        bindings.EnsureBound(
            WorkbookApplicationCommandRouter.QuickAccessRoutes
                .Concat(WorkbookApplicationCommandRouter.WorksheetContextMenuRoutes)
                .Concat(WorkbookApplicationCommandRouter.KeyboardShortcutRoutes));
        return bindings;
    }

    private static object NativeSource(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeSource ?? invocation;

    private static RoutedEventArgs RoutedArgs(WorkbookApplicationCommandInvocation invocation) =>
        invocation.NativeEventArgs as RoutedEventArgs ?? new RoutedEventArgs();

    private CellAddress TargetAddress(WorkbookApplicationCommandInvocation invocation) =>
        invocation.TargetAddress ?? SheetGrid.SelectedRange?.Start ?? new CellAddress(_currentSheetId, 1, 1);
}
