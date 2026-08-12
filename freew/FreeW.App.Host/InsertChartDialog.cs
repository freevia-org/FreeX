using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Modal "Insert Chart" dialog: a <see cref="ChartKind"/> picker plus an editable data grid
/// (categories as the first column, one row per series value). Returns a <see cref="Chart"/>
/// on OK, or null if the user cancels. A sensible column-chart default is pre-populated so
/// clicking OK with no edits inserts a working chart.
/// </summary>
internal sealed class InsertChartDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // ── Controls ────────────────────────────────────────────────────────────────────────────────
    private readonly ComboBox _kindBox;
    private readonly TextBox _titleBox;
    private readonly DataGrid _dataGrid;
    private Chart? _result;

    // ── Default seed data ────────────────────────────────────────────────────────────────────────
    // ── Constructor ──────────────────────────────────────────────────────────────────────────────
    private InsertChartDialog(Window? owner, Chart? seed)
    {
        var surface = InsertChartDialogPlanner.Surface;
        Owner = owner;
        Title = surface.Title;
        Width = 500;
        MinHeight = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        var state = InsertChartDialogPlanner.BuildInitialState(seed, CultureInfo.CurrentCulture);

        var panel = new StackPanel { Margin = new Thickness(14) };

        // ── Chart type picker ────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock { Text = surface.Field(InsertChartDialogField.ChartType).Label, Margin = new Thickness(0, 0, 0, 4) });
        _kindBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        foreach (ChartKind kind in Enum.GetValues<ChartKind>())
            _kindBox.Items.Add(kind.ToString());
        _kindBox.SelectedItem = state.Kind.ToString();
        WpfDialogSurfaceSemantics.Apply(_kindBox, surface.Field(InsertChartDialogField.ChartType));
        panel.Children.Add(_kindBox);

        // ── Chart title ──────────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock { Text = surface.Field(InsertChartDialogField.Title).Label, Margin = new Thickness(0, 0, 0, 4) });
        _titleBox = new TextBox
        {
            Text = state.Title,
            Margin = new Thickness(0, 0, 0, 10)
        };
        WpfDialogSurfaceSemantics.Apply(_titleBox, surface.Field(InsertChartDialogField.Title));
        panel.Children.Add(_titleBox);

        // ── Data grid ────────────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = surface.Field(InsertChartDialogField.Data).Label,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _dataGrid = BuildDataGrid(state);
        WpfDialogSurfaceSemantics.Apply(_dataGrid, surface.Field(InsertChartDialogField.Data));
        panel.Children.Add(_dataGrid);

        // ── OK / Cancel ──────────────────────────────────────────────────────────────────────────
        var actionPlans = InsertChartDialogPlanner.ActionButtons;
        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 72,
            rowMargin: new Thickness(0, 12, 0, 0),
            acceptContent: actionPlans[0].Label,
            cancelContent: actionPlans[1].Label);
        panel.Children.Add(buttons);

        Content = panel;
        DialogFocus.FocusAndSelect(_titleBox);
    }

    // ── Data grid construction ────────────────────────────────────────────────────────────────────
    private DataGrid BuildDataGrid(InsertChartDialogInitialState state)
    {
        var rows = state.Rows
            .Select(row => new ChartRowViewModel(row.Category, row.SeriesValues.ToArray()))
            .ToList();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 140,
            MaxHeight = 260,
            Margin = new Thickness(0, 0, 0, 0),
            ItemsSource = rows
        };

        // Category column
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = InsertChartDialogPlanner.CategoryColumnHeader,
            Binding = new System.Windows.Data.Binding("Category") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 100
        });

        // Series columns — derive from seed or provide one default
        var seriesCount = state.SeriesNames.Count;
        for (var s = 0; s < seriesCount; s++)
        {
            var header = state.SeriesNames[s];
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding($"[{s}]") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        return grid;
    }

    // ── Accept logic ─────────────────────────────────────────────────────────────────────────────
    private void Accept()
    {
        _dataGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var kind = Enum.Parse<ChartKind>(_kindBox.SelectedItem?.ToString() ?? nameof(ChartKind.Column));
        var rows = (_dataGrid.ItemsSource as IEnumerable<ChartRowViewModel>)?.ToList() ?? [];
        var seriesNames = _dataGrid.Columns.Skip(1).Select(c => c.Header?.ToString() ?? string.Empty).ToArray();
        if (!InsertChartDialogPlanner.TryBuildResult(
                kind,
                _titleBox.Text,
                seriesNames,
                rows.Select(row => new InsertChartDialogRow(row.Category, row.SeriesValues)),
                CultureInfo.CurrentCulture,
                out var chart,
                out var errorMessage))
        {
            DialogMessageHelper.ShowWarning(this, errorMessage ?? InsertChartDialogPlanner.EmptyRowsValidationMessage);
            return;
        }

        _result = chart;
        Close();
    }

    // ── Public API ───────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Show the Insert Chart dialog seeded with an optional existing chart (for "Edit Data" reuse).
    /// Returns the configured chart, or null if the user cancelled.
    /// </summary>
    public static Chart? Prompt(Window? owner, Chart? seed = null)
    {
        var dialog = new InsertChartDialog(owner, seed);
        dialog.ShowDialog();
        return dialog._result;
    }
}

/// <summary>Row view model for the chart data grid.</summary>
internal sealed class ChartRowViewModel
{
    public string Category { get; set; }
    public string[] SeriesValues { get; }

    public string this[int index]
    {
        get => index < SeriesValues.Length ? SeriesValues[index] : string.Empty;
        set { if (index < SeriesValues.Length) SeriesValues[index] = value; }
    }

    public ChartRowViewModel(string category, string[] values)
    {
        Category = category;
        SeriesValues = values;
    }
}
