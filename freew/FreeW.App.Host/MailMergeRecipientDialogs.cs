using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Select Recipients &gt; Use an Existing List, after the file is picked: choose the worksheet / named
/// range / table or the text delimiter, say whether the first row holds the column headers, and see a
/// preview. All decisions live in <see cref="RecipientSourceImportSession"/>.
/// </summary>
internal static class RecipientSourceImportDialog
{
    public static MergeData? Ask(Window? owner, RecipientSourceImportSession session)
    {
        var view = Create(owner, session);
        return view.Window.ShowDialog() == true ? view.Result : null;
    }

    internal sealed class View(Window window)
    {
        public Window Window { get; } = window;

        public MergeData? Result { get; set; }

        public Func<bool> TryAccept { get; set; } = () => false;
    }

    internal static View Create(Window? owner, RecipientSourceImportSession session)
    {
        var dialog = RecipientDialogChrome.CreateWindow(owner, session.Title, width: 560);
        var view = new View(dialog);

        var tables = new ComboBox { ItemsSource = session.Tables, SelectedIndex = session.SelectedTableIndex };
        var delimiters = new ComboBox
        {
            ItemsSource = session.Delimiters,
            SelectedIndex = session.SelectedDelimiterIndex,
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var firstRowHeaders = new CheckBox
        {
            Content = MailMergeRecipientSourceText.FirstRowHeaders,
            IsChecked = session.FirstRowIsHeader,
            Margin = new Thickness(0, 4, 0, 8),
        };
        var preview = RecipientDialogChrome.CreateGrid(readOnly: true);
        preview.Height = 150;
        var summary = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(tables, RecipientSourceImportSession.TableAutomationId);
        AutomationProperties.SetAutomationId(delimiters, RecipientSourceImportSession.DelimiterAutomationId);
        AutomationProperties.SetAutomationId(firstRowHeaders, RecipientSourceImportSession.FirstRowHeadersAutomationId);
        AutomationProperties.SetAutomationId(preview, RecipientSourceImportSession.PreviewAutomationId);

        Button? ok = null;
        void Refresh()
        {
            var current = session.Preview;
            RecipientDialogChrome.SetTextColumns(preview, current.Header, readOnly: true);
            preview.ItemsSource = current.Rows.Select(row => new RecipientGridRow(row)).ToArray();
            summary.Text = current.Summary;
            if (ok is not null)
                ok.IsEnabled = session.CanAccept;
        }

        tables.SelectionChanged += (_, _) =>
        {
            session.SelectTable(tables.SelectedIndex);
            Refresh();
        };
        delimiters.SelectionChanged += (_, _) =>
        {
            session.SelectDelimiter(delimiters.SelectedIndex);
            Refresh();
        };
        firstRowHeaders.Click += (_, _) =>
        {
            session.SetFirstRowIsHeader(firstRowHeaders.IsChecked == true);
            Refresh();
        };

        view.TryAccept = () =>
        {
            if (session.Accept() is not { } data)
                return false;
            view.Result = data;
            return true;
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = session.FileLabel,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8),
        });
        if (session.ShowsTables)
        {
            panel.Children.Add(RecipientDialogChrome.Label(MailMergeRecipientSourceText.TableLabel));
            tables.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(tables);
        }
        if (session.ShowsDelimiters)
        {
            panel.Children.Add(RecipientDialogChrome.Label(MailMergeRecipientSourceText.DelimiterLabel));
            delimiters.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(delimiters);
        }
        if (session.SourceNote is { } note)
        {
            panel.Children.Add(new TextBlock
            {
                Text = note,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
        panel.Children.Add(firstRowHeaders);
        panel.Children.Add(RecipientDialogChrome.Label(MailMergeRecipientSourceText.PreviewLabel));
        panel.Children.Add(preview);
        panel.Children.Add(summary);

        var buttons = DialogButtonRowFactory.Create(
            () =>
            {
                if (view.TryAccept())
                    dialog.DialogResult = true;
            },
            buttonWidth: RecipientDialogChrome.ButtonWidth,
            rowMargin: new Thickness(0, 12, 0, 0));
        ok = buttons.Children.OfType<Button>().FirstOrDefault(button => button.IsDefault);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        Refresh();
        return view;
    }
}

/// <summary>
/// Select Recipients &gt; Type a New List (Word's New Address List): an editable grid of entries whose
/// columns can be added, renamed and removed. On OK the list is saved to a file through the supplied
/// callback, as Word does; cancelling the save returns to the list.
/// </summary>
internal static class RecipientListEditorDialog
{
    public static MergeData? Ask(
        Window? owner,
        RecipientListEditorSession session,
        Func<Window, MergeData, bool> save)
    {
        var view = Create(owner, session, save);
        return view.Window.ShowDialog() == true ? view.Result : null;
    }

    internal sealed class View(Window window, DataGrid grid)
    {
        public Window Window { get; } = window;

        public DataGrid Grid { get; } = grid;

        public MergeData? Result { get; set; }

        public Func<bool> TryAccept { get; set; } = () => false;

        public Action NewEntry { get; set; } = () => { };

        public Action DeleteEntry { get; set; } = () => { };

        public Func<string, bool> AddColumn { get; set; } = _ => false;

        public Func<int, string, bool> RenameColumn { get; set; } = (_, _) => false;

        public Func<int, bool> RemoveColumn { get; set; } = _ => false;
    }

    internal static View Create(
        Window? owner,
        RecipientListEditorSession session,
        Func<Window, MergeData, bool> save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var dialog = RecipientDialogChrome.CreateWindow(owner, session.Title, width: 720);
        var grid = RecipientDialogChrome.CreateGrid(readOnly: false);
        grid.Height = 260;
        AutomationProperties.SetAutomationId(grid, RecipientListEditorSession.GridAutomationId);
        var view = new View(dialog, grid);
        var rows = new ObservableCollection<RecipientGridRow>();
        grid.ItemsSource = rows;

        void Rebuild(int selectRow = -1)
        {
            // Commit any cell that is still being edited so its text is not lost by the rebuild.
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            RecipientDialogChrome.SetTextColumns(grid, session.Columns, readOnly: false);
            rows.Clear();
            for (var index = 0; index < session.RowCount; index++)
            {
                var rowIndex = index;
                rows.Add(new RecipientGridRow(
                    session.Row(rowIndex),
                    (column, value) => session.SetCell(rowIndex, column, value)));
            }
            if (selectRow >= 0 && selectRow < rows.Count)
                grid.SelectedIndex = selectRow;
        }

        int CurrentColumn() =>
            grid.CurrentColumn is { } column ? grid.Columns.IndexOf(column) : -1;

        bool Report(RecipientListEditorResult result)
        {
            if (!result.Success)
                DialogMessageHelper.ShowWarning(dialog, result.Message, session.Title);
            return result.Success;
        }

        view.NewEntry = () => Rebuild(session.AddRow());
        view.DeleteEntry = () =>
        {
            var index = grid.SelectedIndex;
            if (index < 0)
                return;
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            session.RemoveRow(index);
            Rebuild(Math.Min(index, session.RowCount - 1));
        };
        view.AddColumn = name =>
        {
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            var added = Report(session.AddColumn(name));
            if (added)
                Rebuild(grid.SelectedIndex);
            return added;
        };
        view.RenameColumn = (index, name) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            var renamed = Report(session.RenameColumn(index, name));
            if (renamed)
                Rebuild(grid.SelectedIndex);
            return renamed;
        };
        view.RemoveColumn = index =>
        {
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            var removed = Report(session.RemoveColumn(index));
            if (removed)
                Rebuild(grid.SelectedIndex);
            return removed;
        };
        view.TryAccept = () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            var data = session.Build();
            if (!save(dialog, data))
                return false;
            view.Result = data;
            return true;
        };

        var newEntry = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.NewEntry,
            RecipientListEditorSession.NewEntryAutomationId,
            () => view.NewEntry());
        var deleteEntry = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.DeleteEntry,
            RecipientListEditorSession.DeleteEntryAutomationId,
            () => view.DeleteEntry());
        var addColumn = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.AddColumn,
            RecipientListEditorSession.AddColumnAutomationId,
            () =>
            {
                if (RecipientColumnNamePrompt.Ask(dialog, string.Empty) is { } name)
                    view.AddColumn(name);
            });
        var renameColumn = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.RenameColumn,
            RecipientListEditorSession.RenameColumnAutomationId,
            () =>
            {
                var index = CurrentColumn();
                if (index >= 0 && RecipientColumnNamePrompt.Ask(dialog, session.Columns[index]) is { } name)
                    view.RenameColumn(index, name);
            });
        var removeColumn = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.RemoveColumn,
            RecipientListEditorSession.RemoveColumnAutomationId,
            () =>
            {
                var index = CurrentColumn();
                if (index >= 0)
                    view.RemoveColumn(index);
            });

        var commands = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in new[] { newEntry, deleteEntry, addColumn, renameColumn, removeColumn })
            commands.Children.Add(button);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = MailMergeRecipientSourceText.NewListHint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(grid);
        panel.Children.Add(commands);
        panel.Children.Add(DialogButtonRowFactory.Create(
            () =>
            {
                if (view.TryAccept())
                    dialog.DialogResult = true;
            },
            buttonWidth: RecipientDialogChrome.ButtonWidth,
            rowMargin: new Thickness(0, 12, 0, 0)));
        dialog.Content = panel;
        Rebuild(selectRow: 0);
        return view;
    }
}

/// <summary>
/// Edit Recipient List (Word's Mail Merge Recipients): every loaded record with an include check box,
/// a sort, and a simple filter, backed by <see cref="RecipientListSelectionSession"/>.
/// </summary>
internal static class RecipientListSelectionDialog
{
    public static IReadOnlyList<int>? Ask(Window? owner, RecipientListSelectionSession session)
    {
        var view = Create(owner, session);
        return view.Window.ShowDialog() == true ? view.Result : null;
    }

    internal sealed class View(Window window, DataGrid grid)
    {
        public Window Window { get; } = window;

        public DataGrid Grid { get; } = grid;

        public IReadOnlyList<int>? Result { get; set; }

        public Func<bool> TryAccept { get; set; } = () => false;
    }

    internal static View Create(Window? owner, RecipientListSelectionSession session)
    {
        var dialog = RecipientDialogChrome.CreateWindow(owner, session.Title, width: 720);
        var grid = RecipientDialogChrome.CreateGrid(readOnly: false);
        grid.Height = 260;
        AutomationProperties.SetAutomationId(grid, RecipientListSelectionSession.GridAutomationId);
        var view = new View(dialog, grid);
        var summary = new TextBlock { Margin = new Thickness(0, 6, 0, 0) };

        // A template column so one click toggles the box (a DataGridCheckBoxColumn needs a second
        // click to enter edit mode first).
        var includeBox = new FrameworkElementFactory(typeof(CheckBox));
        includeBox.SetBinding(
            System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding(nameof(RecipientGridRow.IsIncluded))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        includeBox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        includeBox.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var include = new DataGridTemplateColumn
        {
            Header = new TextBlock { Text = MailMergeRecipientSourceText.IncludeColumn },
            CellTemplate = new DataTemplate { VisualTree = includeBox },
            IsReadOnly = true,
        };

        void Refresh()
        {
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            RecipientDialogChrome.SetTextColumns(grid, session.Columns, readOnly: true, include);
            grid.ItemsSource = session.VisibleRows
                .Select(row => new RecipientGridRow(
                    row.Values,
                    setValue: null,
                    row.IsIncluded,
                    included =>
                    {
                        session.SetIncluded(row.SourceIndex, included);
                        summary.Text = session.Summary;
                    }))
                .ToArray();
            summary.Text = session.Summary;
        }

        var sortColumn = new ComboBox { ItemsSource = session.SortColumns, SelectedIndex = 0, MinWidth = 150 };
        var sortDirection = new ComboBox { ItemsSource = session.SortDirections, SelectedIndex = 0, MinWidth = 110 };
        var filterColumn = new ComboBox
        {
            ItemsSource = session.FilterColumns,
            SelectedIndex = session.FilterColumnIndex,
            MinWidth = 150,
        };
        var filterComparison = new ComboBox
        {
            ItemsSource = session.FilterComparisons,
            SelectedIndex = session.FilterComparisonIndex,
            MinWidth = 130,
        };
        var filterValue = new TextBox { MinWidth = 140, Text = session.FilterValue };
        AutomationProperties.SetAutomationId(sortColumn, RecipientListSelectionSession.SortColumnAutomationId);
        AutomationProperties.SetAutomationId(sortDirection, RecipientListSelectionSession.SortDirectionAutomationId);
        AutomationProperties.SetAutomationId(filterColumn, RecipientListSelectionSession.FilterColumnAutomationId);
        AutomationProperties.SetAutomationId(filterComparison, RecipientListSelectionSession.FilterComparisonAutomationId);
        AutomationProperties.SetAutomationId(filterValue, RecipientListSelectionSession.FilterValueAutomationId);
        AutomationProperties.SetName(filterValue, MailMergeRecipientSourceText.FilterLabel.TrimEnd(':', ' '));

        void ApplySort()
        {
            if (sortColumn.SelectedItem is RecipientSourceChoice<int> { Value: >= 0 } column
                && sortDirection.SelectedItem is RecipientSourceChoice<bool> direction)
            {
                session.Sort(column.Value, direction.Value);
                Refresh();
            }
        }

        void ApplyFilter()
        {
            session.SetFilter(
                filterColumn.SelectedItem is RecipientSourceChoice<int> column ? column.Value : -1,
                filterComparison.SelectedItem is RecipientSourceChoice<RecipientFilterComparison> comparison
                    ? comparison.Value
                    : RecipientFilterComparison.Contains,
                filterValue.Text);
            filterValue.IsEnabled = session.FilterNeedsValue;
            Refresh();
        }

        sortColumn.SelectionChanged += (_, _) => ApplySort();
        sortDirection.SelectionChanged += (_, _) => ApplySort();
        filterColumn.SelectionChanged += (_, _) => ApplyFilter();
        filterComparison.SelectionChanged += (_, _) => ApplyFilter();
        filterValue.TextChanged += (_, _) => ApplyFilter();

        var clearFilter = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.ClearFilter,
            RecipientListSelectionSession.ClearFilterAutomationId,
            () =>
            {
                filterValue.Text = string.Empty;
                filterColumn.SelectedIndex = 0;
                filterComparison.SelectedIndex = (int)RecipientFilterComparison.Contains;
                ApplyFilter();
            });
        var selectAll = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.SelectAll,
            RecipientListSelectionSession.SelectAllAutomationId,
            () =>
            {
                session.SetAllVisibleIncluded(true);
                Refresh();
            });
        var clearAll = RecipientDialogChrome.CommandButton(
            MailMergeRecipientSourceText.ClearAll,
            RecipientListSelectionSession.ClearAllAutomationId,
            () =>
            {
                session.SetAllVisibleIncluded(false);
                Refresh();
            });

        view.TryAccept = () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
            view.Result = session.BuildSelection();
            return true;
        };

        var sortRow = RecipientDialogChrome.Row(
            RecipientDialogChrome.InlineLabel(MailMergeRecipientSourceText.SortLabel), sortColumn, sortDirection);
        var filterRow = RecipientDialogChrome.Row(
            RecipientDialogChrome.InlineLabel(MailMergeRecipientSourceText.FilterLabel),
            filterColumn,
            filterComparison,
            filterValue,
            clearFilter);
        var checkRow = RecipientDialogChrome.Row(selectAll, clearAll);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = MailMergeRecipientSourceText.EditListHint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(grid);
        panel.Children.Add(summary);
        panel.Children.Add(checkRow);
        panel.Children.Add(sortRow);
        panel.Children.Add(filterRow);
        panel.Children.Add(DialogButtonRowFactory.Create(
            () =>
            {
                if (view.TryAccept())
                    dialog.DialogResult = true;
            },
            buttonWidth: RecipientDialogChrome.ButtonWidth,
            rowMargin: new Thickness(0, 12, 0, 0)));
        dialog.Content = panel;
        Refresh();
        return view;
    }
}

/// <summary>The one-field prompt behind Add Column / Rename Column.</summary>
internal static class RecipientColumnNamePrompt
{
    public static string? Ask(Window owner, string initialName)
    {
        string? result = null;
        var dialog = RecipientDialogChrome.CreateWindow(owner, MailMergeRecipientSourceText.ColumnNameTitle, width: 340);
        var box = new TextBox { Text = initialName, Margin = new Thickness(0, 0, 0, 4) };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(RecipientDialogChrome.Label(MailMergeRecipientSourceText.ColumnNamePrompt));
        panel.Children.Add(box);
        panel.Children.Add(DialogButtonRowFactory.Create(
            () =>
            {
                result = box.Text;
                dialog.DialogResult = true;
            },
            buttonWidth: RecipientDialogChrome.ButtonWidth,
            rowMargin: new Thickness(0, 12, 0, 0)));
        dialog.Content = panel;
        box.SelectAll();
        box.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }
}

/// <summary>
/// A grid row over a recipient's field values. WPF binds each text column to the indexer, and the
/// optional callbacks forward edits to the owning presentation session.
/// </summary>
internal sealed class RecipientGridRow(
    IReadOnlyList<string> values,
    Action<int, string>? setValue = null,
    bool isIncluded = true,
    Action<bool>? setIncluded = null)
{
    private readonly string[] _values = [.. values];
    private bool _isIncluded = isIncluded;

    public string this[int index]
    {
        get => index >= 0 && index < _values.Length ? _values[index] : string.Empty;
        set
        {
            if (index < 0 || index >= _values.Length)
                return;
            _values[index] = value ?? string.Empty;
            setValue?.Invoke(index, _values[index]);
        }
    }

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            _isIncluded = value;
            setIncluded?.Invoke(value);
        }
    }
}

/// <summary>Shared construction for the recipient-list dialogs.</summary>
internal static class RecipientDialogChrome
{
    public const double ButtonWidth = 80;

    public static FreeWDialogWindow CreateWindow(Window? owner, string title, double width) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        ResizeMode = ResizeMode.NoResize,
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        Owner = owner,
        ShowInTaskbar = false,
    };

    public static DataGrid CreateGrid(bool readOnly) => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = readOnly,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        CanUserReorderColumns = false,
        CanUserSortColumns = false,
        CanUserResizeRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.FullRow,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    /// <summary>
    /// Replaces the grid's columns: any <paramref name="leading"/> columns, then one text column per
    /// field bound to the row indexer. A header is set as plain text, so an underscore in a field name
    /// is shown, not treated as an access key.
    /// </summary>
    public static void SetTextColumns(
        DataGrid grid,
        IReadOnlyList<string> header,
        bool readOnly,
        params DataGridColumn[] leading)
    {
        grid.Columns.Clear();
        foreach (var column in leading)
            grid.Columns.Add(column);
        for (var index = 0; index < header.Count; index++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = new TextBlock { Text = header[index] },
                Binding = new Binding($"[{index}]")
                {
                    Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
                },
                IsReadOnly = readOnly,
                MinWidth = 90,
            });
        }
    }

    public static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 0, 0, 2) };

    public static TextBlock InlineLabel(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 56,
        Margin = new Thickness(0, 0, 6, 0),
    };

    public static Button CommandButton(string label, string automationId, Action execute)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0, 6, 0),
        };
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => execute();
        return button;
    }

    public static StackPanel Row(params FrameworkElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var child in children)
        {
            if (child is not Button)
                child.Margin = new Thickness(child.Margin.Left, 0, 6, 0);
            row.Children.Add(child);
        }
        return row;
    }
}
