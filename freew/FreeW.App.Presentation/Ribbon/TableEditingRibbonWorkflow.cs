using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record TableEditingRibbonPorts(
    Action PrepareExecution,
    Action ToggleHeaderRow,
    Action ToggleBandedRows,
    Action ToggleLastRow,
    Action ToggleFirstColumn,
    Action ToggleLastColumn,
    Action ToggleBandedColumns,
    Action ToggleGridlines,
    Action SelectTable,
    Action SelectRow,
    Action SelectColumn,
    Action SelectCell,
    Action InsertRowAbove,
    Action InsertRowBelow,
    Action InsertColumnLeft,
    Action InsertColumnRight,
    Action MergeCells,
    IRibbonCommand SplitCell,
    IRibbonCommand Shading,
    IRibbonCommand Borders,
    Action DeleteRow,
    Action DeleteColumn,
    Action DeleteTable,
    Action SplitTable,
    Action DistributeRows,
    Action DistributeColumns,
    Action<AutoFitMode> SetAutoFit,
    Action<TableCellVerticalAlignment, TextAlignment> SetCellAlignment,
    Action<CellTextDirection> SetCellTextDirection,
    Action ToggleRepeatHeaderRow);

/// <summary>
/// Owns renderer-neutral Table Design and Table Layout command policy. WPF and Avalonia provide
/// only native editor adapters; command identity, option mapping, and execution preparation remain
/// shared so the two ribbon hosts cannot drift independently.
/// </summary>
public static class TableEditingRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.TableHeaderRow,
        FreeWRibbonCommandAction.TableBandedRows,
        FreeWRibbonCommandAction.TableLastRow,
        FreeWRibbonCommandAction.TableFirstColumn,
        FreeWRibbonCommandAction.TableLastColumn,
        FreeWRibbonCommandAction.TableBandedCols,
        FreeWRibbonCommandAction.TableViewGridlines,
        FreeWRibbonCommandAction.TableSelectTable,
        FreeWRibbonCommandAction.TableSelectRow,
        FreeWRibbonCommandAction.TableSelectCol,
        FreeWRibbonCommandAction.TableSelectCell,
        FreeWRibbonCommandAction.TableInsertAbove,
        FreeWRibbonCommandAction.TableInsertBelow,
        FreeWRibbonCommandAction.TableInsertColLeft,
        FreeWRibbonCommandAction.TableInsertColRight,
        FreeWRibbonCommandAction.TableMergeCells,
        FreeWRibbonCommandAction.TableSplitCell,
        FreeWRibbonCommandAction.TableShading,
        FreeWRibbonCommandAction.TableBorders,
        FreeWRibbonCommandAction.TableDeleteRow,
        FreeWRibbonCommandAction.TableDeleteCol,
        FreeWRibbonCommandAction.TableDelete,
        FreeWRibbonCommandAction.SplitTable,
        FreeWRibbonCommandAction.TableDistributeRows,
        FreeWRibbonCommandAction.TableDistributeCols,
        FreeWRibbonCommandAction.TableAutofitContents,
        FreeWRibbonCommandAction.TableAutofitWindow,
        FreeWRibbonCommandAction.TableAutofitFixed,
        FreeWRibbonCommandAction.CellAlignTopLeft,
        FreeWRibbonCommandAction.CellAlignTopCenter,
        FreeWRibbonCommandAction.CellAlignTopRight,
        FreeWRibbonCommandAction.CellAlignMiddleLeft,
        FreeWRibbonCommandAction.CellAlignMiddleCenter,
        FreeWRibbonCommandAction.CellAlignMiddleRight,
        FreeWRibbonCommandAction.CellAlignBottomLeft,
        FreeWRibbonCommandAction.CellAlignBottomCenter,
        FreeWRibbonCommandAction.CellAlignBottomRight,
        FreeWRibbonCommandAction.CellTextDirectionHorizontal,
        FreeWRibbonCommandAction.CellTextDirectionRotate90,
        FreeWRibbonCommandAction.CellTextDirectionRotate270,
        FreeWRibbonCommandAction.TableRepeatHeader,
    ];

    public static void Register(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableEditingRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.PrepareExecution);

        Bind(bindings, ports, FreeWRibbonCommandAction.TableHeaderRow, ports.ToggleHeaderRow);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableBandedRows, ports.ToggleBandedRows);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableLastRow, ports.ToggleLastRow);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableFirstColumn, ports.ToggleFirstColumn);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableLastColumn, ports.ToggleLastColumn);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableBandedCols, ports.ToggleBandedColumns);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableViewGridlines, ports.ToggleGridlines);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableSelectTable, ports.SelectTable);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableSelectRow, ports.SelectRow);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableSelectCol, ports.SelectColumn);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableSelectCell, ports.SelectCell);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableInsertAbove, ports.InsertRowAbove);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableInsertBelow, ports.InsertRowBelow);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableInsertColLeft, ports.InsertColumnLeft);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableInsertColRight, ports.InsertColumnRight);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableMergeCells, ports.MergeCells);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.TableSplitCell, ports.SplitCell);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.TableShading, ports.Shading);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.TableBorders, ports.Borders);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableDeleteRow, ports.DeleteRow);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableDeleteCol, ports.DeleteColumn);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableDelete, ports.DeleteTable);
        Bind(bindings, ports, FreeWRibbonCommandAction.SplitTable, ports.SplitTable);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableDistributeRows, ports.DistributeRows);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableDistributeCols, ports.DistributeColumns);
        Bind(bindings, ports, FreeWRibbonCommandAction.TableAutofitContents, () => ports.SetAutoFit(AutoFitMode.Contents));
        Bind(bindings, ports, FreeWRibbonCommandAction.TableAutofitWindow, () => ports.SetAutoFit(AutoFitMode.Window));
        Bind(bindings, ports, FreeWRibbonCommandAction.TableAutofitFixed, () => ports.SetAutoFit(AutoFitMode.Fixed));

        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignTopLeft, TableCellVerticalAlignment.Top, TextAlignment.Left);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignTopCenter, TableCellVerticalAlignment.Top, TextAlignment.Center);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignTopRight, TableCellVerticalAlignment.Top, TextAlignment.Right);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignMiddleLeft, TableCellVerticalAlignment.Center, TextAlignment.Left);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignMiddleCenter, TableCellVerticalAlignment.Center, TextAlignment.Center);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignMiddleRight, TableCellVerticalAlignment.Center, TextAlignment.Right);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignBottomLeft, TableCellVerticalAlignment.Bottom, TextAlignment.Left);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignBottomCenter, TableCellVerticalAlignment.Bottom, TextAlignment.Center);
        BindAlignment(bindings, ports, FreeWRibbonCommandAction.CellAlignBottomRight, TableCellVerticalAlignment.Bottom, TextAlignment.Right);

        Bind(bindings, ports, FreeWRibbonCommandAction.CellTextDirectionHorizontal,
            () => ports.SetCellTextDirection(CellTextDirection.Horizontal));
        Bind(bindings, ports, FreeWRibbonCommandAction.CellTextDirectionRotate90,
            () => ports.SetCellTextDirection(CellTextDirection.Rotate90));
        Bind(bindings, ports, FreeWRibbonCommandAction.CellTextDirectionRotate270,
            () => ports.SetCellTextDirection(CellTextDirection.Rotate270));
        Bind(bindings, ports, FreeWRibbonCommandAction.TableRepeatHeader, ports.ToggleRepeatHeaderRow);
    }

    private static void Bind(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableEditingRibbonPorts ports,
        FreeWRibbonCommandAction action,
        Action execute) =>
        bindings.BindAction(action, execute, prepareExecution: ports.PrepareExecution);

    private static void BindAlignment(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableEditingRibbonPorts ports,
        FreeWRibbonCommandAction action,
        TableCellVerticalAlignment vertical,
        TextAlignment horizontal) =>
        Bind(bindings, ports, action, () => ports.SetCellAlignment(vertical, horizontal));

    private static void BindCommand(
        FreeWRibbonEditorCommandFamilyBuilder bindings,
        TableEditingRibbonPorts ports,
        FreeWRibbonCommandAction action,
        IRibbonCommand command) =>
        bindings.Bind(action, new PreparedCommand(ports.PrepareExecution, command));

    private sealed class PreparedCommand(Action prepareExecution, IRibbonCommand inner) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            prepareExecution();
            inner.Execute(context);
        }

        public RibbonCommandState GetState() =>
            inner is IRibbonStatefulCommand stateful
                ? stateful.GetState()
                : RibbonCommandState.Default;
    }
}
