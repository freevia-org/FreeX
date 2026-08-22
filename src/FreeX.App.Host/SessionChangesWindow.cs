using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Host;

/// <summary>
/// Read-only view of the active workbook session's retained undo/redo labels.
/// It deliberately does not represent persisted revision history or collaboration activity.
/// </summary>
public sealed class SessionChangesWindow : Window
{
    private readonly ObservableCollection<string> _undoItems = [];
    private readonly ObservableCollection<string> _redoItems = [];
    private readonly ListBox _undoList = new();
    private readonly ListBox _redoList = new();
    private readonly TextBlock _emptyMessage = new();

    public SessionChangesWindow(SessionChangesPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Title = SessionChangesPlanner.Title;
        Width = 500;
        Height = 430;
        MinWidth = 380;
        MinHeight = 280;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, SessionChangesPlanner.Title);
        AutomationProperties.SetAutomationId(this, "ReviewSessionChangesWindow");

        Content = CreateContent();
        Refresh(plan);
    }

    public void Refresh(SessionChangesPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Replace(_undoItems, plan.UndoEntries);
        Replace(_redoItems, plan.RedoEntries);
        _emptyMessage.Visibility = plan.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid CreateContent()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scope = new TextBlock
        {
            Text = SessionChangesPlanner.ScopeMessage,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AutomationProperties.SetAutomationId(scope, "ReviewSessionChangesScope");
        root.Children.Add(scope);

        _emptyMessage.Text = SessionChangesPlanner.EmptySectionMessage;
        _emptyMessage.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetAutomationId(_emptyMessage, "ReviewSessionChangesEmptyMessage");
        Grid.SetRow(_emptyMessage, 1);
        root.Children.Add(_emptyMessage);

        var history = new Grid();
        history.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        history.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(history, 2);
        root.Children.Add(history);

        history.Children.Add(CreateSection(
            SessionChangesPlanner.UndoSectionTitle,
            _undoList,
            _undoItems,
            "ReviewSessionChangesUndoList"));
        var redo = CreateSection(
            SessionChangesPlanner.RedoSectionTitle,
            _redoList,
            _redoItems,
            "ReviewSessionChangesRedoList");
        Grid.SetColumn(redo, 1);
        history.Children.Add(redo);

        var close = new Button
        {
            Content = UiText.Ok,
            MinWidth = 76,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        AutomationProperties.SetAutomationId(close, "ReviewSessionChangesCloseButton");
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 3);
        root.Children.Add(close);

        return root;
    }

    private static GroupBox CreateSection(
        string title,
        ListBox list,
        ObservableCollection<string> items,
        string automationId)
    {
        var section = new GroupBox
        {
            Header = title,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(6)
        };

        list.ItemsSource = items;
        list.IsHitTestVisible = false;
        AutomationProperties.SetName(list, title);
        AutomationProperties.SetAutomationId(list, automationId);
        section.Content = list;
        return section;
    }

    private static void Replace(ObservableCollection<string> destination, IReadOnlyList<string> source)
    {
        destination.Clear();
        foreach (var item in source)
            destination.Add(item);
    }
}
