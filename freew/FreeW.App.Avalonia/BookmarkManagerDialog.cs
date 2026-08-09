using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia;

internal sealed class BookmarkManagerDialog : FreeWDialogWindow
{
    private static readonly BookmarkManagerSurfaceSpec Surface = BookmarkManagerDialogPlanner.Surface;
    private readonly DocumentView _editor;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private readonly Button _goTo;
    private readonly Button _delete;
    private readonly BookmarkManagerDialogSession _session = new();
    private bool _applyingState;

    internal BookmarkManagerDialog(DocumentView editor)
    {
        _editor = editor;
        Title = Surface.Title;
        AutomationProperties.SetAutomationId(this, Surface.WindowAutomationId);
        Width = Surface.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        _list = new ListBox { MinWidth = Surface.ListMinWidth, MinHeight = Surface.ListMinHeight, Focusable = true, IsTabStop = true };
        AutomationProperties.SetAutomationId(_list, Surface.ListAutomationId);
        _list.FocusAdorner = null;
        _list.SelectionChanged += (_, _) => UpdateSelection();
        _list.DoubleTapped += (_, _) => GoTo();
        _status = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), Margin = new Thickness(0, Surface.StatusTopMargin, 0, 0) };
        AutomationProperties.SetAutomationId(_status, Surface.StatusAutomationId);
        _goTo = Button(Surface.Action(BookmarkManagerActionKind.GoTo), GoTo);
        _delete = Button(Surface.Action(BookmarkManagerActionKind.Delete), Delete);
        var close = Button(Surface.Action(BookmarkManagerActionKind.Close), Close);
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(
            [_goTo, _delete, close],
            new Thickness(0, Surface.ActionTopMargin, 0, 0),
            AvaloniaCompactDialogChrome.WindowsStyle with { ActionSpacing = 0 });
        Content = new StackPanel
        {
            Margin = new Thickness(Surface.OuterMargin),
            Children =
            {
                Heading(),
                _list,
                buttons,
                _status,
            },
        };
        Opened += (_, _) =>
        {
            AvaloniaCompactDialogChrome.ApplyDescendantChrome(
                this,
                AvaloniaCompactDialogChrome.WindowsStyle with { ButtonPadding = new Thickness(Surface.ButtonHorizontalPadding, Surface.ButtonVerticalPadding) });
            var inputBorder = new SolidColorBrush(Color.FromRgb(0xAB, 0xAD, 0xB3));
            var buttonBorder = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
            _list.BorderBrush = inputBorder;
            _list.BorderThickness = new Thickness(1);
            Styles.Add(new Style(selector => selector.OfType<Button>().Class(":disabled"))
            {
                Setters =
                {
                    new Setter(global::Avalonia.Controls.Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8))),
                    new Setter(global::Avalonia.Controls.Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0))),
                    new Setter(global::Avalonia.Controls.Button.BorderBrushProperty, buttonBorder),
                    new Setter(global::Avalonia.Controls.Button.OpacityProperty, 1d),
                },
            });
            Dispatcher.UIThread.Post(() => _list.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            Close();
            args.Handled = true;
        };
        RefreshList();
    }

    public static Task ShowAsync(Window owner, DocumentView editor) =>
        new BookmarkManagerDialog(editor).ShowDialog(owner);

    internal int ItemCountForTest => _list.ItemCount;
    internal void SelectForTest(int index) => _list.SelectedIndex = index;
    internal void DeleteForTest() => Delete();
    internal void GoToForTest() => GoTo();

    internal string StatusTextForTest => _status.Text ?? string.Empty;

    private TextBlock Heading()
    {
        var heading = new TextBlock { Text = Surface.Heading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, Surface.HeadingBottomMargin) };
        AutomationProperties.SetAutomationId(heading, Surface.HeadingAutomationId);
        return heading;
    }

    private void RefreshList(BookmarkManagerDeleteRefreshPlan? deletePlan = null)
    {
        var locations = Bookmarks.List(_editor.Document);
        var state = deletePlan is null
            ? _session.Refresh(locations)
            : _session.CompleteDelete(deletePlan, locations);
        ApplyState(state);
    }

    private void ApplyState(BookmarkManagerDialogState state)
    {
        _applyingState = true;
        try
        {
            _list.ItemsSource = state.Items;
            _list.SelectedIndex = state.SelectedIndex;
        }
        finally
        {
            _applyingState = false;
        }

        _status.Text = state.StatusText;
        ApplyActionState(state);
    }

    private void UpdateSelection()
    {
        if (_applyingState)
            return;

        ApplyActionState(_session.SelectIndex(_list.SelectedIndex));
    }

    private void ApplyActionState(BookmarkManagerDialogState state)
    {
        _goTo.IsEnabled = state.IsEnabled(BookmarkManagerActionKind.GoTo);
        _delete.IsEnabled = state.IsEnabled(BookmarkManagerActionKind.Delete);
    }

    private void GoTo()
    {
        var intent = _session.PlanGoTo();
        if (intent is null)
            return;
        _editor.GoToBookmark(intent.Name);
        Close();
    }

    private void Delete()
    {
        var plan = _session.PlanDelete();
        if (plan is null)
            return;
        _editor.DeleteBookmark(plan.Name);
        RefreshList(plan);
    }

    private static Button Button(BookmarkManagerActionSpec spec, Action click)
    {
        var button = new Button { Content = spec.Label, IsCancel = spec.IsCancel, MinWidth = Surface.ButtonMinWidth };
        button.Margin = new Thickness(Surface.ButtonLeadingMargin, 0, 0, 0);
        button.Padding = new Thickness(Surface.ButtonHorizontalPadding, Surface.ButtonVerticalPadding);
        AutomationProperties.SetAutomationId(button, spec.AutomationId);
        button.Click += (_, _) => click();
        return button;
    }
}
