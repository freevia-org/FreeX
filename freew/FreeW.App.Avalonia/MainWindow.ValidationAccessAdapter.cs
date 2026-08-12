using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow
{
    internal ValidationAccessAdapter CreateValidationAccessAdapter() => new(this);

    internal sealed class ValidationAccessAdapter
    {
        private readonly MainWindow _owner;

        internal ValidationAccessAdapter(MainWindow owner) => _owner = owner;

        internal void StartWhenOpened(Func<Task> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            _owner.Opened += async (_, _) => await operation();
        }

        internal void InsertTable(int rows, int columns) => _owner._editor.InsertTable(rows, columns);

        internal IReadOnlyList<Block> DocumentBlocks => _owner._editor.Document.Blocks;

        internal void PlaceCaretInCell(int tableBlock, int row, int column, int paragraph, int offset) =>
            _owner._editor.PlaceCaretInCell(tableBlock, row, column, paragraph, offset);

        internal ModelTableContext? CaretTableContext() => _owner._editor.CaretTableContext();

        internal async Task<TablePropertiesDialogObservation> ShowTablePropertiesDialogAsync(
            ModelTableContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var dialog = new TablePropertiesDialog(context);
            await dialog.ShowDialog(_owner);
            return new TablePropertiesDialogObservation(
                dialog.Result,
                context.Table.Rows.Count,
                context.Table.Rows.Count == 0 ? 0 : context.Table.Rows[0].Cells.Count,
                dialog.FocusTraceForValidation.ToArray());
        }

        internal void ApplyTableProperties(TablePropertiesValues? values) =>
            ApplyTablePropertiesResult(_owner._editor, values);
    }

    internal sealed record TablePropertiesDialogObservation(
        TablePropertiesValues? Values,
        int TableRows,
        int TableColumns,
        IReadOnlyList<string> FocusTrace);
}
