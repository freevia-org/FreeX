using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// The docked Format pane (Excel's Format Chart Area / Title / Legend / Axis / Series pane): a layout
/// column at the right of the grid that lists the options of the selected chart element. What it lists
/// and how an edit becomes a command both come from the shared <see cref="ChartFormatPanePlanner"/>
/// (the same planner the Avalonia host uses); this partial only builds the controls and executes the
/// resulting <see cref="SetChartLayoutCommand"/> (one undo step per edit). "More Options" opens the
/// existing full dialog for anything the pane does not list.
/// </summary>
public partial class MainWindow
{
    private Guid _chartFormatPaneChartId;
    private ChartElementRef _chartFormatPaneElement;

    private bool IsChartFormatPaneOpen => ChartFormatPane?.Visibility == Visibility.Visible;

    /// <summary>Opens (or retargets) the pane on <paramref name="element"/> of the selected chart.</summary>
    internal void ShowChartFormatPane(ChartElementRef element)
    {
        if (GetSelectedChartOnCurrentSheet() is not { } chart)
            return;

        if (IsCommentsPaneOpen)
            CloseCommentsPane(restoreGridFocus: false);

        _chartFormatPaneChartId = chart.Id;
        _chartFormatPaneElement = element;
        ChartFormatPane.Visibility = Visibility.Visible;
        RebuildChartFormatPane(chart);
    }

    internal void CloseChartFormatPane()
    {
        if (ChartFormatPane is null)
            return;

        ChartFormatPane.Visibility = Visibility.Collapsed;
        ChartFormatPaneBody.Children.Clear();
        _chartFormatPaneChartId = Guid.Empty;
    }

    private void ChartFormatPaneCloseBtn_Click(object sender, RoutedEventArgs e) => CloseChartFormatPane();

    /// <summary>Keeps an open pane in step with the selection (element changed, chart changed or deselected).</summary>
    private void RefreshChartFormatPaneForSelection()
    {
        if (!IsChartFormatPaneOpen)
            return;

        var chart = GetSelectedChartOnCurrentSheet();
        if (chart is null || chart.Id != _chartFormatPaneChartId)
        {
            CloseChartFormatPane();
            return;
        }

        _chartFormatPaneElement = SheetGrid.SelectedChartElement ?? ChartElementRef.ChartArea;
        RebuildChartFormatPane(chart);
    }

    private void OnChartElementSelectedForPane(Guid chartId, ChartElementRef element, string? seriesName) =>
        RefreshChartFormatPaneForSelection();

    private void OnChartElementLayoutChanged(Guid chartId, ChartLayoutOptions options)
    {
        var chart = GetSelectedChartOnCurrentSheet();
        if (chart is null || chart.Id != chartId)
            return;

        var name = ChartElementCommandPlanner
            .Describe(SheetGrid.SelectedChartElement ?? ChartElementRef.ChartArea, SheetGrid.SelectedChartElementSeriesName)
            .Resolve(UiText.Get, UiText.Format);
        if (ApplyChartLayoutDialogResult(name, chart, options) && IsChartFormatPaneOpen)
            RebuildChartFormatPane(chart);
    }

    private void RebuildChartFormatPane(ChartModel chart)
    {
        var model = ChartFormatPanePlanner.Build(chart, _chartFormatPaneElement, SheetGrid.SelectedChartElementSeriesName);
        var name = model.ElementName.Resolve(UiText.Get, UiText.Format);
        ChartFormatPaneTitle.Text = UiText.Format(ChartFormatPanePlanner.TitleResourceKey, name);

        ChartFormatPaneBody.Children.Clear();
        foreach (var row in model.Rows)
            ChartFormatPaneBody.Children.Add(CreateChartFormatPaneRow(chart, model, row));

        var more = new Button
        {
            Content = UiText.Get(ChartFormatPanePlanner.MoreOptionsResourceKey),
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        more.Click += (_, _) => OpenChartElementFormatDialog(chart, model.Element);
        ChartFormatPaneBody.Children.Add(more);
    }

    private FrameworkElement CreateChartFormatPaneRow(ChartModel chart, ChartFormatPaneModel model, ChartFormatPaneRow row)
    {
        var label = UiText.Get(row.LabelKey);
        void Apply(string value)
        {
            if (ChartFormatPanePlanner.Plan(chart, model.Element, row.Id, value) is not { } options)
            {
                RebuildChartFormatPane(chart);
                return;
            }

            if (ApplyChartLayoutDialogResult(label, chart, options))
                RebuildChartFormatPane(chart);
        }

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        switch (row.Kind)
        {
            case ChartFormatRowKind.Toggle:
            {
                var box = new CheckBox { Content = label, IsChecked = row.Value == "true" };
                System.Windows.Automation.AutomationProperties.SetName(box, label);
                box.Click += (_, _) => Apply(box.IsChecked == true ? "true" : "false");
                panel.Children.Add(box);
                break;
            }
            case ChartFormatRowKind.Button:
            {
                var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
                button.Click += (_, _) => Apply(string.Empty);
                panel.Children.Add(button);
                break;
            }
            case ChartFormatRowKind.Choice:
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
                var combo = new ComboBox { DisplayMemberPath = nameof(ChartFormatChoiceItem.Text) };
                foreach (var choice in row.Choices ?? [])
                {
                    var item = new ChartFormatChoiceItem(choice.Id, UiText.Get(choice.LabelKey));
                    combo.Items.Add(item);
                    if (choice.Id == row.Value)
                        combo.SelectedItem = item;
                }

                System.Windows.Automation.AutomationProperties.SetName(combo, label);
                combo.SelectionChanged += (_, args) =>
                {
                    if (args.AddedItems.Count == 1 && args.AddedItems[0] is ChartFormatChoiceItem picked && picked.Id != row.Value)
                        Apply(picked.Id);
                };
                panel.Children.Add(combo);
                break;
            }
            default:
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
                var box = new TextBox { Text = row.Value };
                System.Windows.Automation.AutomationProperties.SetName(box, label);
                var committed = false;
                void Commit()
                {
                    if (committed || box.Text == row.Value)
                        return;

                    committed = true;
                    Apply(box.Text);
                }

                box.LostKeyboardFocus += (_, _) => Commit();
                box.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter)
                    {
                        Commit();
                        args.Handled = true;
                    }
                };
                panel.Children.Add(box);
                break;
            }
        }

        return panel;
    }

    private sealed record ChartFormatChoiceItem(string Id, string Text);
}
