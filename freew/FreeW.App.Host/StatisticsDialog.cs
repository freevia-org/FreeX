using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A small read-only modal showing document statistics — word/character/paragraph/sentence/line counts,
/// estimated reading time, average words per sentence, and the Flesch Reading Ease readability score —
/// computed by the pure <see cref="DocumentStatistics"/> helper. Includes a checkbox to include footnote
/// and endnote text in the counts (Word parity). Code-only to match the rest of the FreeW window style;
/// informational, so it has a single Close button.
/// </summary>
internal sealed class StatisticsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextDocument _document;
    private readonly Grid _grid;
    private bool _includeNotes;
    private int _currentRow;

    // Value TextBlocks keyed by label — updated when the checkbox changes.
    private readonly Dictionary<string, TextBlock> _valueBlocks = new();

    public StatisticsDialog(Window owner, TextDocument document)
    {
        _document = document;
        var initialPlan = StatisticsDialogPlanner.Build(
            document,
            includeNotes: false,
            StatisticsDialogDepth.Detailed);
        Owner = owner;
        Title = initialPlan.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _grid = new Grid { Margin = new Thickness(16, 14, 16, 8) };
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _currentRow = 0;
        foreach (var row in initialPlan.Rows)
        {
            if (row.StartsNewSection)
                AddSeparator();
            AddRow(row.Key, row.Label, row.Value);
        }

        // Include footnotes/endnotes checkbox (Word parity).
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var checkbox = new CheckBox
        {
            Content = StatisticsDialogPlanner.IncludeNotesLabel,
            Margin = new Thickness(0, 8, 0, 2),
            IsChecked = false,
        };
        checkbox.Checked += (_, _) => { _includeNotes = true; RefreshValues(); };
        checkbox.Unchecked += (_, _) => { _includeNotes = false; RefreshValues(); };
        Grid.SetRow(checkbox, _currentRow);
        Grid.SetColumn(checkbox, 0);
        Grid.SetColumnSpan(checkbox, 2);
        _grid.Children.Add(checkbox);
        _currentRow++;

        var buttons = DialogButtonRowFactory.CreateOkOnly(Close, buttonWidth: 84, rowMargin: new Thickness(16, 4, 16, 12));

        var outer = new StackPanel();
        outer.Children.Add(_grid);
        outer.Children.Add(buttons);
        Content = outer;

        // Populate with initial values (no notes).
        RefreshValues();
    }

    private void RefreshValues()
    {
        var plan = StatisticsDialogPlanner.Build(
            _document,
            _includeNotes,
            StatisticsDialogDepth.Detailed);
        foreach (var row in plan.Rows)
            SetValue(row.Key, row.Value);
    }

    private void SetValue(string label, string value)
    {
        if (_valueBlocks.TryGetValue(label, out var block))
            block.Text = value;
    }

    private void AddRow(string key, string label, string initialValue)
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 4, 16, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40))
        };
        Grid.SetRow(labelBlock, _currentRow);
        Grid.SetColumn(labelBlock, 0);
        _grid.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            Text = initialValue,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(valueBlock, _currentRow);
        Grid.SetColumn(valueBlock, 1);
        _grid.Children.Add(valueBlock);

        _valueBlocks[key] = valueBlock;
        _currentRow++;
    }

    private void AddSeparator()
    {
        _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var line = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
        };
        Grid.SetRow(line, _currentRow);
        Grid.SetColumn(line, 0);
        Grid.SetColumnSpan(line, 2);
        _grid.Children.Add(line);
        _currentRow++;
    }
}
