using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
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
    private readonly ListBox _stopList;
    private readonly TextBox _positionBox;
    private readonly TextBox _defaultTabStopBox;
    private readonly ComboBox _alignmentBox;
    private readonly ComboBox _leaderBox;
    private readonly TabsDialogSession _session;
    private TabsDialogResult? _result;

    private TabsDialog(Window? owner, IReadOnlyList<TabStop> tabStops, double defaultTabStopPt)
    {
        _session = new TabsDialogSession(tabStops, defaultTabStopPt, CultureInfo.CurrentCulture);
        Owner = owner;
        Title = TabsDialogPlanner.Title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, TabsDialogPlanner.AutomationId);

        _stopList = new ListBox { Height = 120, MinWidth = 150 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_stopList, TabsDialogPlanner.StopListAutomationId);
        _stopList.SelectionChanged += (_, _) => OnStopSelected();
        RefreshList();

        _positionBox = new TextBox { MinWidth = 120 };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_positionBox, TabsDialogPlanner.PositionAutomationId);
        _defaultTabStopBox = new TextBox
        {
            MinWidth = 120,
            Text = _session.State.DefaultTabStopText
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_defaultTabStopBox, TabsDialogPlanner.DefaultTabStopAutomationId);

        _alignmentBox = new ComboBox { MinWidth = 120 };
        foreach (var alignment in _session.Alignments)
            _alignmentBox.Items.Add(alignment.Label);
        _alignmentBox.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_alignmentBox, TabsDialogPlanner.AlignmentAutomationId);

        _leaderBox = new ComboBox { MinWidth = 120 };
        foreach (var leader in _session.Leaders)
            _leaderBox.Items.Add(leader.Label);
        _leaderBox.SelectedIndex = 0;
        System.Windows.Automation.AutomationProperties.SetAutomationId(_leaderBox, TabsDialogPlanner.LeaderAutomationId);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, TabsDialogPlanner.PositionLabel, _positionBox);
        AddRow(grid, 1, TabsDialogPlanner.StopsLabel, _stopList);
        AddRow(grid, 2, TabsDialogPlanner.AlignmentLabel, _alignmentBox);
        AddRow(grid, 3, TabsDialogPlanner.LeaderLabel, _leaderBox);

        AddRow(grid, 4, TabsDialogPlanner.DefaultTabStopLabel, _defaultTabStopBox);

        // Set / Clear / Clear All row, mirroring Word's Tabs dialog action buttons.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(ActionButton(TabsDialogPlanner.SetButtonAccessLabel, OnSet));
        actions.Children.Add(ActionButton(TabsDialogPlanner.ClearButtonAccessLabel, OnClear));
        actions.Children.Add(ActionButton(TabsDialogPlanner.ClearAllButtonAccessLabel, OnClearAll));
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
        foreach (var row in _session.State.Rows)
            _stopList.Items.Add(row.DisplayText);
    }

    // Reflect the selected stop into the position/alignment/leader editors so Set updates it in place.
    private void OnStopSelected()
    {
        var selection = _session.ProjectSelection(_stopList.SelectedIndex);
        if (selection is null)
            return;
        _positionBox.Text = selection.PositionText;
        _alignmentBox.SelectedIndex = selection.AlignmentIndex;
        _leaderBox.SelectedIndex = selection.LeaderIndex;
    }

    // Add a new stop at the typed position, or replace an existing stop at the same position (so editing a
    // stop's alignment/leader and pressing Set updates it rather than duplicating it). Re-sorts the list.
    private void OnSet()
    {
        var request = new TabsDialogSetRequest(
            _positionBox.Text,
            _alignmentBox.SelectedIndex,
            _leaderBox.SelectedIndex);

        var plan = _session.SetStop(request);
        if (!plan.Applied)
        {
            DialogMessageHelper.ShowWarning(this, plan.ValidationMessage);
            return;
        }

        RefreshList();
        _stopList.SelectedIndex = plan.SelectedIndex;
    }

    // Remove the selected stop (Word's "Clear"), or the one matching the typed position if none is selected.
    private void OnClear()
    {
        _session.ClearStop(_stopList.SelectedIndex, _positionBox.Text);
        RefreshList();
    }

    private void OnClearAll()
    {
        _session.ClearAll();
        RefreshList();
    }

    private void Accept()
    {
        var acceptance = _session.PlanAcceptance(_defaultTabStopBox.Text);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.ValidationMessage);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    /// <summary>
    /// Show the dialog seeded with the paragraph's current tab stops and the document's default tab-stop
    /// spacing; returns the edited stop list and interval to apply, or null if cancelled.
    /// </summary>
    public static TabsDialogResult? Prompt(Window? owner, IReadOnlyList<TabStop> tabStops, double defaultTabStopPt)
    {
        var dialog = new TabsDialog(owner, tabStops, defaultTabStopPt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
