using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Tabs" dialog (Home / Paragraph &gt; Tabs…): manage a paragraph's custom tab stops. Lists the
/// existing stops, lets the user type a position (points) and pick an alignment (Left / Center / Right /
/// Decimal) and a leader (none / dots / dashes / underline), then Set a new (or update an existing) stop,
/// Clear the selected one, or Clear All. Returns the edited list of <see cref="TabStop"/>s and default tab
/// interval to apply, or null if cancelled.
///
/// <para>
/// The model's <see cref="TabStop"/> already round-trips to docx (pPr/w:tabs) — position in points,
/// alignment in w:val, optional leader in w:leader — so this dialog only edits that list; the apply path
/// (<see cref="FreeW.App.Host.Editing.DocumentView.SetParagraphTabStops"/>) routes through the undo/redo
/// bus. The default tab-stop spacing lives in word/settings.xml's w:defaultTabStop and is edited as a
/// document-wide setting through the same page-settings path as other Layout-backed document settings.
/// </para>
/// </summary>
internal sealed class TabsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // Alignment / leader option order shown in the drop-downs; indexes map to the enums below.
    private static readonly string[] Alignments = ["Left", "Center", "Right", "Decimal"];
    private static readonly string[] Leaders = ["1 None", "2 ....", "3 ----", "4 ____"];

    private readonly ListBox _stopList;
    private readonly TextBox _positionBox;
    private readonly TextBox _defaultTabStopBox;
    private readonly ComboBox _alignmentBox;
    private readonly ComboBox _leaderBox;
    private readonly List<TabStop> _stops;
    private Result? _result;

    internal sealed record Result(IReadOnlyList<TabStop> TabStops, double DefaultTabStopPt);

    private TabsDialog(Window? owner, IReadOnlyList<TabStop> tabStops, double defaultTabStopPt)
    {
        Owner = owner;
        Title = "Tabs";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // Work on a sorted, de-duplicated copy so Set/Clear edit a stable list and re-applying preserves order.
        _stops = tabStops.OrderBy(s => s.PositionPt).ToList();

        _stopList = new ListBox { Height = 120, MinWidth = 150 };
        _stopList.SelectionChanged += (_, _) => OnStopSelected();
        RefreshList();

        _positionBox = new TextBox { MinWidth = 120 };
        _defaultTabStopBox = new TextBox
        {
            MinWidth = 120,
            Text = defaultTabStopPt.ToString("0.##", CultureInfo.CurrentCulture)
        };

        _alignmentBox = new ComboBox { MinWidth = 120 };
        foreach (var alignment in Alignments)
            _alignmentBox.Items.Add(alignment);
        _alignmentBox.SelectedIndex = 0;

        _leaderBox = new ComboBox { MinWidth = 120 };
        foreach (var leader in Leaders)
            _leaderBox.Items.Add(leader);
        _leaderBox.SelectedIndex = 0;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Tab stop position (pt):", _positionBox);
        AddRow(grid, 1, "Stops:", _stopList);
        AddRow(grid, 2, "Alignment:", _alignmentBox);
        AddRow(grid, 3, "Leader:", _leaderBox);

        AddRow(grid, 4, "Default tab stops (pt):", _defaultTabStopBox);

        // Set / Clear / Clear All row, mirroring Word's Tabs dialog action buttons.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(ActionButton("_Set", OnSet));
        actions.Children.Add(ActionButton("C_lear", OnClear));
        actions.Children.Add(ActionButton("Clear _All", OnClearAll));
        Grid.SetRow(actions, 5);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX's dialogs.
        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 6);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_positionBox);
    }

    private static Button ActionButton(string content, System.Action onClick)
    {
        var button = new Button { Content = content, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static void AddRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    // Rebuild the list box from the model list, formatting each stop "<pos> pt  <alignment>  <leader>".
    private void RefreshList()
    {
        _stopList.Items.Clear();
        foreach (var stop in _stops)
            _stopList.Items.Add(Describe(stop));
    }

    private static string Describe(TabStop stop)
    {
        var leader = stop.Leader == TabLeader.None ? "" : $"  {stop.Leader}";
        return $"{stop.PositionPt.ToString("0.##", CultureInfo.CurrentCulture)} pt  {stop.Alignment}{leader}";
    }

    // Reflect the selected stop into the position/alignment/leader editors so Set updates it in place.
    private void OnStopSelected()
    {
        var index = _stopList.SelectedIndex;
        if (index < 0 || index >= _stops.Count)
            return;
        var stop = _stops[index];
        _positionBox.Text = stop.PositionPt.ToString("0.##", CultureInfo.CurrentCulture);
        _alignmentBox.SelectedIndex = (int)stop.Alignment;
        _leaderBox.SelectedIndex = (int)stop.Leader;
    }

    // Add a new stop at the typed position, or replace an existing stop at the same position (so editing a
    // stop's alignment/leader and pressing Set updates it rather than duplicating it). Re-sorts the list.
    private void OnSet()
    {
        if (!TryParse(_positionBox.Text, out var position) || position < 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a non-negative tab-stop position in points.");
            return;
        }

        var stop = new TabStop(position, (TabStopAlignment)_alignmentBox.SelectedIndex, (TabLeader)_leaderBox.SelectedIndex);
        // Replace any existing stop within a small tolerance of this position; otherwise add a new one.
        var existing = _stops.FindIndex(s => System.Math.Abs(s.PositionPt - position) < 0.01);
        if (existing >= 0)
            _stops[existing] = stop;
        else
            _stops.Add(stop);
        _stops.Sort((a, b) => a.PositionPt.CompareTo(b.PositionPt));

        RefreshList();
        _stopList.SelectedIndex = _stops.FindIndex(s => System.Math.Abs(s.PositionPt - position) < 0.01);
    }

    // Remove the selected stop (Word's "Clear"), or the one matching the typed position if none is selected.
    private void OnClear()
    {
        var index = _stopList.SelectedIndex;
        if (index < 0 && TryParse(_positionBox.Text, out var position))
            index = _stops.FindIndex(s => System.Math.Abs(s.PositionPt - position) < 0.01);
        if (index < 0 || index >= _stops.Count)
            return;
        _stops.RemoveAt(index);
        RefreshList();
    }

    private void OnClearAll()
    {
        _stops.Clear();
        RefreshList();
    }

    private void Accept()
    {
        if (!TryParse(_defaultTabStopBox.Text, out var defaultTabStop) || defaultTabStop <= 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter a positive default tab-stop interval in points.");
            return;
        }

        _result = new Result(_stops.OrderBy(s => s.PositionPt).ToList(), defaultTabStop);
        Close();
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    /// <summary>
    /// Show the dialog seeded with the paragraph's current tab stops and the document's default tab-stop
    /// spacing; returns the edited stop list and interval to apply, or null if cancelled.
    /// </summary>
    public static Result? Prompt(Window? owner, IReadOnlyList<TabStop> tabStops, double defaultTabStopPt)
    {
        var dialog = new TabsDialog(owner, tabStops, defaultTabStopPt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
