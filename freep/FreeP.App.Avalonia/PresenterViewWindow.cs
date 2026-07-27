using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

/// <summary>Native Avalonia presenter dashboard synchronized with a running slideshow.</summary>
public sealed class PresenterViewWindow : Window
{
    private readonly Presentation _presentation;
    private readonly Func<SlideShowPresenterState> _stateProvider;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SlideCanvas _currentPreview;
    private readonly SlideCanvas _nextPreview;
    private readonly TextBlock _statusText;
    private readonly TextBlock _elapsedText;
    private readonly TextBlock _currentLabel;
    private readonly TextBlock _nextLabel;
    private readonly TextBox _notesText;
    private readonly Button _backButton;
    private readonly Button _advanceButton;
    private readonly Button _recordTimingsButton;
    private readonly Button _rehearseTimingsButton;
    private readonly Action? _goBack;
    private readonly Action? _goNext;
    private readonly ComboBox _pointerModeCombo;
    private readonly Action<SlideShowScreenMode>? _setScreenMode;
    private readonly Action<SlideShowPresenterPointerMode>? _selectPointerMode;
    private readonly Action? _clearInk;
    private readonly Action<SlideShowTimingIntent>? _setTimingIntent;
    private bool _refreshing;

    public PresenterViewWindow(
        Presentation presentation,
        Func<SlideShowPresenterState> stateProvider,
        Action? goBack = null,
        Action? goNext = null,
        Action<SlideShowScreenMode>? setScreenMode = null,
        Action<SlideShowPresenterPointerMode>? selectPointerMode = null,
        Action? clearInk = null,
        Action<SlideShowTimingIntent>? setTimingIntent = null)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _goBack = goBack;
        _goNext = goNext;
        _setScreenMode = setScreenMode;
        _selectPointerMode = selectPointerMode;
        _clearInk = clearInk;
        _setTimingIntent = setTimingIntent;

        Title = "Presenter View";
        Width = 1200;
        Height = 760;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(30, 34, 42));

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _statusText = MakeText(18, FontWeight.SemiBold);
        _elapsedText = MakeText(18, FontWeight.Normal);
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(18, 0, 18, 0),
        };
        _backButton = MakeActionButton("Previous", () =>
        {
            _goBack?.Invoke();
            RefreshFromState();
        });
        _advanceButton = MakeActionButton("Next", () =>
        {
            _goNext?.Invoke();
            RefreshFromState();
        });
        _recordTimingsButton = MakeActionButton("Record timings", () =>
        {
            if (_setTimingIntent is not null)
            {
                var current = _stateProvider().ToolPlan.Recording.TimingIntent;
                _setTimingIntent(current == SlideShowTimingIntent.RecordTimings
                    ? SlideShowTimingIntent.None
                    : SlideShowTimingIntent.RecordTimings);
                RefreshFromState();
            }
        });
        _rehearseTimingsButton = MakeActionButton("Rehearse timings", () =>
        {
            if (_setTimingIntent is not null)
            {
                var current = _stateProvider().ToolPlan.Recording.TimingIntent;
                _setTimingIntent(current == SlideShowTimingIntent.RehearseTimings
                    ? SlideShowTimingIntent.None
                    : SlideShowTimingIntent.RehearseTimings);
                RefreshFromState();
            }
        });
        controls.Children.Add(_backButton);
        controls.Children.Add(_advanceButton);
        controls.Children.Add(_recordTimingsButton);
        controls.Children.Add(_rehearseTimingsButton);
        var normalButton = MakeActionButton("Show", () => _setScreenMode?.Invoke(SlideShowScreenMode.Normal));
        var blackButton = MakeActionButton("Black", () => _setScreenMode?.Invoke(SlideShowScreenMode.Black));
        var whiteButton = MakeActionButton("White", () => _setScreenMode?.Invoke(SlideShowScreenMode.White));
        var clearInkButton = MakeActionButton("Clear ink", () => _clearInk?.Invoke());
        normalButton.IsEnabled = _setScreenMode is not null;
        blackButton.IsEnabled = _setScreenMode is not null;
        whiteButton.IsEnabled = _setScreenMode is not null;
        clearInkButton.IsEnabled = _clearInk is not null;
        _pointerModeCombo = MakePointerModePicker(mode =>
        {
            if (!_refreshing && mode is not null)
            {
                _selectPointerMode?.Invoke(mode.Value);
                RefreshFromState();
            }
        });
        _pointerModeCombo.IsEnabled = _selectPointerMode is not null;
        controls.Children.Add(normalButton);
        controls.Children.Add(blackButton);
        controls.Children.Add(whiteButton);
        controls.Children.Add(clearInkButton);
        controls.Children.Add(_pointerModeCombo);
        Grid.SetColumn(controls, 1);
        Grid.SetColumn(_elapsedText, 2);
        header.Children.Add(_statusText);
        header.Children.Add(controls);
        header.Children.Add(_elapsedText);
        root.Children.Add(header);

        var previews = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _currentLabel = MakeText(14, FontWeight.SemiBold);
        _nextLabel = MakeText(14, FontWeight.SemiBold);
        _currentPreview = MakePreview();
        _nextPreview = MakePreview();
        previews.Children.Add(BuildPreviewPanel("Current", _currentLabel, _currentPreview));
        var nextPanel = BuildPreviewPanel("Next", _nextLabel, _nextPreview);
        Grid.SetColumn(nextPanel, 1);
        previews.Children.Add(nextPanel);
        Grid.SetRow(previews, 1);
        root.Children.Add(previews);

        var notesPanel = new Grid();
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var notesHeading = MakeText(14, FontWeight.SemiBold);
        notesHeading.Text = "Speaker notes";
        notesHeading.Margin = new Thickness(0, 0, 0, 6);
        _notesText = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = new SolidColorBrush(Color.FromRgb(45, 50, 61)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 87, 102)),
            Padding = new Thickness(10),
        };
        Grid.SetRow(_notesText, 1);
        notesPanel.Children.Add(notesHeading);
        notesPanel.Children.Add(_notesText);
        Grid.SetRow(notesPanel, 2);
        root.Children.Add(notesPanel);

        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _refreshTimer.Tick += (_, _) => RefreshFromState();
        Opened += (_, _) =>
        {
            RefreshFromState();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    public void RefreshFromState()
    {
        var plan = SlideShowPresenterViewPlanner.Build(_stateProvider());
        _refreshing = true;
        try
        {
            _statusText.Text = plan.StatusText;
            _elapsedText.Text = $"Elapsed {plan.ElapsedText}";
            _currentLabel.Text = plan.CurrentSlideLabel;
            _nextLabel.Text = plan.NextSlideLabel;
            _notesText.Text = plan.NotesText;
            _backButton.IsEnabled = plan.CanGoBack && _goBack is not null;
            _advanceButton.IsEnabled = plan.CanAdvance && _goNext is not null;
            _recordTimingsButton.Content = plan.IsRecordingTimings ? "Stop recording" : "Record timings";
            _recordTimingsButton.IsEnabled = _setTimingIntent is not null;
            _rehearseTimingsButton.Content = plan.IsRehearsingTimings ? "Stop rehearsal" : "Rehearse timings";
            _rehearseTimingsButton.IsEnabled = _setTimingIntent is not null;
            _pointerModeCombo.SelectedItem = plan.PointerMode;
            _currentPreview.Slide = plan.CurrentSlide;
            _nextPreview.Slide = plan.NextSlide;
            _currentPreview.Refresh();
            _nextPreview.Refresh();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private SlideCanvas MakePreview() => new()
    {
        Presentation = _presentation,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static Border BuildPreviewPanel(
        string heading,
        TextBlock label,
        SlideCanvas preview)
    {
        var panel = new Grid { Margin = new Thickness(heading == "Current" ? 0 : 8, 0, 0, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = MakeText(13, FontWeight.Normal);
        title.Text = heading;
        title.Foreground = new SolidColorBrush(Color.FromRgb(170, 178, 194));
        panel.Children.Add(title);
        Grid.SetRow(label, 1);
        label.Margin = new Thickness(0, 3, 0, 0);
        panel.Children.Add(label);
        Grid.SetRow(preview, 2);
        panel.Children.Add(preview);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(38, 43, 53)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 87, 102)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            Child = panel,
        };
    }

    private static TextBlock MakeText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button MakeActionButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(3, 0, 3, 0),
            MinWidth = 78,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static ComboBox MakePointerModePicker(Action<SlideShowPresenterPointerMode?> changed)
    {
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<SlideShowPresenterPointerMode>(),
            MinWidth = 104,
            Margin = new Thickness(6, 0, 3, 0),
        };
        combo.SelectionChanged += (_, _) =>
            changed(combo.SelectedItem is SlideShowPresenterPointerMode mode ? mode : null);
        return combo;
    }
}
