using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly string[] DefaultCategories = ["Q1", "Q2", "Q3", "Q4"];
    private static readonly double[] DefaultValues = [8.0, 5.0, 11.0, 7.0];
    private const string DefaultSeriesName = "Sales";
    private const string DefaultTitle = "Quarterly Sales";

    // ── Constructor ──────────────────────────────────────────────────────────────────────────────
    private InsertChartDialog(Window? owner, Chart? seed)
    {
        Owner = owner;
        Title = "Insert Chart";
        Width = 500;
        MinHeight = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };

        // ── Chart type picker ────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock { Text = "Chart type:", Margin = new Thickness(0, 0, 0, 4) });
        _kindBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        foreach (ChartKind kind in Enum.GetValues<ChartKind>())
            _kindBox.Items.Add(kind.ToString());
        _kindBox.SelectedItem = (seed?.Kind ?? ChartKind.Column).ToString();
        panel.Children.Add(_kindBox);

        // ── Chart title ──────────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock { Text = "Title (optional):", Margin = new Thickness(0, 0, 0, 4) });
        _titleBox = new TextBox
        {
            Text = seed?.Title ?? DefaultTitle,
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(_titleBox);

        // ── Data grid ────────────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = "Chart data  (first column = category labels, remaining columns = series values):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _dataGrid = BuildDataGrid(seed);
        panel.Children.Add(_dataGrid);

        // ── OK / Cancel ──────────────────────────────────────────────────────────────────────────
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        panel.Children.Add(buttons);

        Content = panel;
        DialogFocus.FocusAndSelect(_titleBox);
    }

    // ── Data grid construction ────────────────────────────────────────────────────────────────────
    private DataGrid BuildDataGrid(Chart? seed)
    {
        var rows = BuildRows(seed);

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
            Header = "Category",
            Binding = new System.Windows.Data.Binding("Category") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
            Width = 100
        });

        // Series columns — derive from seed or provide one default
        var seriesCount = seed?.Series.Count > 0 ? seed.Series.Count : 1;
        for (var s = 0; s < seriesCount; s++)
        {
            var header = seed?.Series.Count > s && !string.IsNullOrEmpty(seed.Series[s].Name)
                ? seed.Series[s].Name!
                : (s == 0 ? DefaultSeriesName : $"Series {s + 1}");
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding($"[{s}]") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        return grid;
    }

    private static List<ChartRowViewModel> BuildRows(Chart? seed)
    {
        var rows = new List<ChartRowViewModel>();
        if (seed is null)
        {
            for (var i = 0; i < DefaultCategories.Length; i++)
                rows.Add(new ChartRowViewModel(DefaultCategories[i], [DefaultValues[i].ToString("G", CultureInfo.CurrentCulture)]));
        }
        else
        {
            var n = Math.Max(seed.Categories.Count, seed.Series.Count > 0 ? seed.Series[0].Values.Count : 0);
            for (var i = 0; i < n; i++)
            {
                var cat = i < seed.Categories.Count ? seed.Categories[i] : string.Empty;
                var vals = seed.Series.Select(s => i < s.Values.Count ? s.Values[i].ToString("G", CultureInfo.CurrentCulture) : "0").ToArray();
                rows.Add(new ChartRowViewModel(cat, vals));
            }
            if (rows.Count == 0)
                rows.Add(new ChartRowViewModel(DefaultCategories[0], [DefaultValues[0].ToString("G", CultureInfo.CurrentCulture)]));
        }
        return rows;
    }

    // ── Accept logic ─────────────────────────────────────────────────────────────────────────────
    private void Accept()
    {
        _dataGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);

        var kind = Enum.Parse<ChartKind>(_kindBox.SelectedItem?.ToString() ?? nameof(ChartKind.Column));
        var title = string.IsNullOrWhiteSpace(_titleBox.Text) ? null : _titleBox.Text.Trim();

        var rows = (_dataGrid.ItemsSource as IEnumerable<ChartRowViewModel>)?.ToList() ?? [];
        rows = rows.Where(r => !string.IsNullOrWhiteSpace(r.Category) || r.SeriesValues.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();

        if (rows.Count == 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter at least one data row.");
            return;
        }

        var seriesCount = _dataGrid.Columns.Count - 1; // first col = category
        if (seriesCount < 1) seriesCount = 1;

        var seriesNames = _dataGrid.Columns.Skip(1).Select(c => c.Header?.ToString() ?? string.Empty).ToArray();

        var chart = new Chart { Kind = kind, Title = title };
        foreach (var row in rows)
            chart.Categories.Add(row.Category);

        for (var s = 0; s < seriesCount; s++)
        {
            var series = new ChartSeries { Name = s < seriesNames.Length ? seriesNames[s] : null };
            foreach (var row in rows)
            {
                var text = s < row.SeriesValues.Length ? row.SeriesValues[s] : null;
                series.Values.Add(double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v : 0.0);
            }
            chart.Series.Add(series);
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
