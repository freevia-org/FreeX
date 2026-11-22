using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Model;
using Free.Shared.Localization;

using AvaloniaDockPanel = Avalonia.Controls.DockPanel;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// The docked Format pane (Excel's Format Chart Area / Title / Legend / Axis / Series pane): a layout
/// column at the right edge of the work area, opened by double-clicking a chart element. What it lists
/// and how an edit becomes a command come from the shared <see cref="ChartFormatPanePlanner"/> (the same
/// planner the WPF host uses); this partial only builds the controls and applies the resulting layout
/// command (one undo step per edit). "More Options" reaches the existing full dialog.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly IBrush ChartFormatPaneBackground = new ImmutableSolidColorBrush(Color.FromRgb(255, 255, 255));
    private static readonly IBrush ChartFormatPaneBorder = new ImmutableSolidColorBrush(Color.FromRgb(200, 200, 200));

    private readonly Border _chartFormatPaneHost = new();
    private readonly StackPanel _chartFormatPaneBody = new() { Spacing = 6 };
    private readonly TextBlock _chartFormatPaneTitle = new();
    private Guid _chartFormatPaneChartId;
    private ChartElementRef _chartFormatPaneElement;

    private Control BuildChartFormatPaneChrome()
    {
        _chartFormatPaneHost.Width = 300;
        _chartFormatPaneHost.Background = ChartFormatPaneBackground;
        _chartFormatPaneHost.BorderBrush = ChartFormatPaneBorder;
        _chartFormatPaneHost.BorderThickness = new Thickness(1, 0, 0, 0);
        _chartFormatPaneHost.Focusable = true;
        _chartFormatPaneHost.IsVisible = false;
        AutomationProperties.SetAutomationId(_chartFormatPaneHost, "ChartFormatPane");
        AutomationProperties.SetName(_chartFormatPaneHost, UiText.Get("ChartFormatPane_AutomationName"));

        var close = new Button
        {
            Content = "X",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        AutomationProperties.SetAutomationId(close, "ChartFormatPaneCloseButton");
        AutomationProperties.SetName(close, UiText.Get(ChartFormatPanePlanner.CloseResourceKey));
        close.Click += (_, _) => CloseChartFormatPane();

        _chartFormatPaneTitle.FontWeight = FontWeight.SemiBold;
        _chartFormatPaneTitle.FontSize = 14;
        _chartFormatPaneTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _chartFormatPaneTitle.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;

        var header = new AvaloniaDockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        AvaloniaDockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(_chartFormatPaneTitle);

        var layout = new AvaloniaDockPanel { LastChildFill = true, Margin = new Thickness(10) };
        AvaloniaDockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(new ScrollViewer { Content = _chartFormatPaneBody });
        _chartFormatPaneHost.Child = layout;
        return _chartFormatPaneHost;
    }

    /// <summary>Opens (or retargets) the pane on <paramref name="element"/> of <paramref name="chart"/>.</summary>
    private void ShowChartFormatPane(ChartModel chart, ChartElementRef element)
    {
        _chartFormatPaneChartId = chart.Id;
        _chartFormatPaneElement = element;
        _chartFormatPaneHost.IsVisible = true;
        RebuildChartFormatPane(chart);
    }

    private void CloseChartFormatPane()
    {
        _chartFormatPaneHost.IsVisible = false;
        _chartFormatPaneBody.Children.Clear();
        _chartFormatPaneChartId = Guid.Empty;
    }

    /// <summary>Keeps an open pane in step with the selection; called from every shell refresh.</summary>
    private void RefreshChartFormatPane()
    {
        if (!_chartFormatPaneHost.IsVisible)
            return;

        var chart = _session.ActiveSheet.Charts.FirstOrDefault(c => c.Id == _chartFormatPaneChartId);
        if (chart is null || !IsSelectedChart(chart))
        {
            CloseChartFormatPane();
            return;
        }

        _chartFormatPaneElement = GetSelectedChartElement(chart) ?? ChartElementRef.ChartArea;
        RebuildChartFormatPane(chart);
    }

    private void RebuildChartFormatPane(ChartModel chart)
    {
        var model = ChartFormatPanePlanner.Build(chart, _chartFormatPaneElement, _selectedChartElementSeriesName);
        var name = model.ElementName.Resolve(UiText.Get, UiText.Format);
        _chartFormatPaneTitle.Text = UiText.Format(ChartFormatPanePlanner.TitleResourceKey, name);

        _chartFormatPaneBody.Children.Clear();
        foreach (var row in model.Rows)
            _chartFormatPaneBody.Children.Add(CreateChartFormatPaneRow(chart, model, row));

        var more = new Button
        {
            Content = UiText.Get(ChartFormatPanePlanner.MoreOptionsResourceKey),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        more.Click += (_, _) => RunGuarded(() => OpenChartElementFormatDialogAsync(chart, model.Element));
        _chartFormatPaneBody.Children.Add(more);
    }

    private Control CreateChartFormatPaneRow(ChartModel chart, ChartFormatPaneModel model, ChartFormatPaneRow row)
    {
        var label = UiText.Get(row.LabelKey);
        void Apply(string value)
        {
            if (ChartFormatPanePlanner.Plan(chart, model.Element, row.Id, value) is not { } options)
            {
                RebuildChartFormatPane(chart);
                return;
            }

            ApplyChartLayout(label, chart, options);
        }

        switch (row.Kind)
        {
            case ChartFormatRowKind.Toggle:
            {
                var box = new CheckBox { Content = label, IsChecked = row.Value == "true" };
                AutomationProperties.SetName(box, label);
                box.IsCheckedChanged += (_, _) => Apply(box.IsChecked == true ? "true" : "false");
                return box;
            }
            case ChartFormatRowKind.Button:
            {
                var button = new Button { Content = label, HorizontalAlignment = AvaloniaHorizontalAlignment.Left };
                button.Click += (_, _) => Apply(string.Empty);
                return button;
            }
            case ChartFormatRowKind.Choice:
            {
                var panel = new StackPanel { Spacing = 2 };
                panel.Children.Add(new TextBlock { Text = label });
                var combo = new ComboBox { HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch };
                var items = (row.Choices ?? []).Select(c => new ChartFormatChoiceItem(c.Id, UiText.Get(c.LabelKey))).ToList();
                combo.ItemsSource = items;
                combo.SelectedItem = items.FirstOrDefault(i => i.Id == row.Value);
                AutomationProperties.SetName(combo, label);
                combo.SelectionChanged += (_, args) =>
                {
                    if (args.AddedItems.Count == 1 && args.AddedItems[0] is ChartFormatChoiceItem picked && picked.Id != row.Value)
                        Apply(picked.Id);
                };
                panel.Children.Add(combo);
                return panel;
            }
            default:
            {
                var panel = new StackPanel { Spacing = 2 };
                panel.Children.Add(new TextBlock { Text = label });
                var box = new TextBox { Text = row.Value };
                AutomationProperties.SetName(box, label);
                var committed = false;
                void Commit()
                {
                    if (committed || box.Text == row.Value)
                        return;

                    committed = true;
                    Apply(box.Text ?? string.Empty);
                }

                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter)
                    {
                        Commit();
                        args.Handled = true;
                    }
                };
                panel.Children.Add(box);
                return panel;
            }
        }
    }

    private sealed class ChartFormatChoiceItem(string id, string text)
    {
        public string Id { get; } = id;

        public override string ToString() => text;
    }
}
