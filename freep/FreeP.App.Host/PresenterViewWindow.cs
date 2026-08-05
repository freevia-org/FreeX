using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>Native WPF presenter dashboard synchronized with a running slideshow.</summary>
public sealed class PresenterViewWindow : Window
{
    private readonly Presentation _presentation;
    private readonly SlideShowPresenterViewSession _session;
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
    private readonly Button _narrationButton;
    private readonly Button _narrationAndMediaButton;
    private readonly Button _applyRecordingButton;
    private readonly TextBlock _recordingStatusText;
    private readonly TextBox _slideNumberBox;
    private readonly Button _goToSlideButton;
    private readonly ComboBox _pointerModeCombo;
    private bool _notesDirty;
    private bool _refreshing;

    public PresenterViewWindow(
        Presentation presentation,
        Func<SlideShowPresenterState> stateProvider,
        Action? goBack = null,
        Action? goNext = null,
        Action<SlideShowScreenMode>? setScreenMode = null,
        Action<SlideShowPresenterPointerMode>? selectPointerMode = null,
        Action? clearInk = null,
        Action<SlideShowTimingIntent>? setTimingIntent = null,
        Action<SlideShowRecordingMediaIntent>? setMediaIntent = null,
        Func<SlideShowRecordingReviewPlan>? recordingReviewProvider = null,
        Func<SlideShowRecordingReviewApplyResult>? applyRecordingReview = null,
        Action<int>? goToSlide = null,
        Action<int, string?>? setNotesText = null)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _session = new SlideShowPresenterViewSession(
            stateProvider,
            goBack,
            goNext,
            setScreenMode,
            selectPointerMode,
            clearInk,
            setTimingIntent,
            setMediaIntent,
            recordingReviewProvider,
            applyRecordingReview,
            goToSlide,
            setNotesText);

        Title = "Presenter View";
        Width = 1200;
        Height = 760;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(30, 34, 42));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _statusText = MakeText(18, FontWeights.SemiBold);
        _elapsedText = MakeText(18, FontWeights.Normal);
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(18, 0, 18, 0),
        };
        _backButton = MakeActionButton("Previous", () =>
        {
            CommitNotes();
            _session.GoBack(notesDirty: false, notesText: null);
            RefreshFromState();
        });
        _advanceButton = MakeActionButton("Next", () =>
        {
            CommitNotes();
            _session.GoNext(notesDirty: false, notesText: null);
            RefreshFromState();
        });
        _slideNumberBox = new TextBox
        {
            Width = 48,
            Height = 28,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Go to slide number",
        };
        _slideNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SubmitSlideNumber();
                e.Handled = true;
            }
        };
        _goToSlideButton = MakeActionButton("Go", SubmitSlideNumber);
        _goToSlideButton.IsEnabled = _session.CanGoToSlide;
        _recordTimingsButton = MakeActionButton("Record timings", () =>
        {
            _session.ToggleTimingIntent(SlideShowTimingIntent.RecordTimings);
            RefreshFromState();
        });
        _rehearseTimingsButton = MakeActionButton("Rehearse timings", () =>
        {
            _session.ToggleTimingIntent(SlideShowTimingIntent.RehearseTimings);
            RefreshFromState();
        });
        _narrationButton = MakeActionButton("Narration", () =>
        {
            _session.ToggleMediaIntent(SlideShowRecordingMediaIntent.Narration);
            RefreshFromState();
        });
        _narrationAndMediaButton = MakeActionButton("Narration + camera", () =>
        {
            _session.ToggleMediaIntent(SlideShowRecordingMediaIntent.NarrationAndMedia);
            RefreshFromState();
        });
        _recordingStatusText = MakeText(13, FontWeights.Normal);
        _recordingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(170, 178, 194));
        _recordingStatusText.Margin = new Thickness(0, 6, 0, 0);
        _applyRecordingButton = MakeActionButton("Apply recording", () =>
        {
            if (_session.ApplyRecordingReview() is { } result)
            {
                _recordingStatusText.Text = $"Applied {result.TotalArtifactCount} recording artifact(s).";
                RefreshFromState();
            }
        });
        controls.Children.Add(_backButton);
        controls.Children.Add(_advanceButton);
        controls.Children.Add(_slideNumberBox);
        controls.Children.Add(_goToSlideButton);
        controls.Children.Add(_recordTimingsButton);
        controls.Children.Add(_rehearseTimingsButton);
        controls.Children.Add(_narrationButton);
        controls.Children.Add(_narrationAndMediaButton);
        controls.Children.Add(_applyRecordingButton);
        var normalButton = MakeActionButton("Show", () => _session.SetScreenMode(SlideShowScreenMode.Normal));
        var blackButton = MakeActionButton("Black", () => _session.SetScreenMode(SlideShowScreenMode.Black));
        var whiteButton = MakeActionButton("White", () => _session.SetScreenMode(SlideShowScreenMode.White));
        var clearInkButton = MakeActionButton("Clear ink", _session.ClearInk);
        normalButton.IsEnabled = _session.CanSetScreenMode;
        blackButton.IsEnabled = _session.CanSetScreenMode;
        whiteButton.IsEnabled = _session.CanSetScreenMode;
        clearInkButton.IsEnabled = _session.CanClearInk;
        _pointerModeCombo = MakePointerModePicker(mode =>
        {
            if (!_refreshing && mode is not null)
            {
                _session.SelectPointerMode(mode.Value);
                RefreshFromState();
            }
        });
        _pointerModeCombo.IsEnabled = _session.CanSelectPointerMode;
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
        Grid.SetRow(_recordingStatusText, 1);
        Grid.SetColumnSpan(_recordingStatusText, 3);
        header.Children.Add(_recordingStatusText);
        root.Children.Add(header);

        var previews = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _currentLabel = MakeText(14, FontWeights.SemiBold);
        _nextLabel = MakeText(14, FontWeights.SemiBold);
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
        var notesHeading = MakeText(14, FontWeights.SemiBold);
        notesHeading.Text = "Speaker notes";
        notesHeading.Margin = new Thickness(0, 0, 0, 6);
        _notesText = new TextBox
        {
            IsReadOnly = !_session.CanSetNotes,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(45, 50, 61)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 87, 102)),
            Padding = new Thickness(10),
        };
        _notesText.TextChanged += (_, _) =>
        {
            if (!_refreshing && _session.CanSetNotes)
                _notesDirty = true;
        };
        _notesText.LostKeyboardFocus += (_, _) => CommitNotes();
        Grid.SetRow(_notesText, 1);
        notesPanel.Children.Add(notesHeading);
        notesPanel.Children.Add(_notesText);
        Grid.SetRow(notesPanel, 2);
        root.Children.Add(notesPanel);

        Content = root;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _refreshTimer.Tick += (_, _) => RefreshFromState();
        Loaded += (_, _) =>
        {
            RefreshFromState();
            _refreshTimer.Start();
        };
        Closed += (_, _) =>
        {
            CommitNotes();
            _refreshTimer.Stop();
        };
    }

    public void RefreshFromState()
    {
        if (!_notesText.IsKeyboardFocusWithin && _notesDirty)
            CommitNotes();

        var plan = _session.BuildViewPlan();
        _refreshing = true;
        try
        {
            _statusText.Text = plan.StatusText;
            _elapsedText.Text = $"Elapsed {plan.ElapsedText}";
            _currentLabel.Text = plan.CurrentSlideLabel;
            _nextLabel.Text = plan.NextSlideLabel;
            if (!_notesText.IsKeyboardFocusWithin && !_notesDirty)
                _notesText.Text = plan.NotesText;
            if (!_slideNumberBox.IsKeyboardFocusWithin && plan.CurrentSlideNumber is int currentSlideNumber)
                _slideNumberBox.Text = currentSlideNumber.ToString(CultureInfo.InvariantCulture);
            _backButton.IsEnabled = plan.CanGoBack;
            _advanceButton.IsEnabled = plan.CanAdvance;
            _recordTimingsButton.Content = plan.RecordTimingsButtonText;
            _recordTimingsButton.IsEnabled = plan.CanSetTimingIntent;
            _rehearseTimingsButton.Content = plan.RehearseTimingsButtonText;
            _rehearseTimingsButton.IsEnabled = plan.CanSetTimingIntent;
            _narrationButton.Content = plan.NarrationButtonText;
            _narrationButton.IsEnabled = plan.CanSetMediaIntent;
            _narrationAndMediaButton.Content = plan.NarrationAndMediaButtonText;
            _narrationAndMediaButton.IsEnabled = plan.CanSetMediaIntent;
            _recordingStatusText.Text = plan.RecordingStatusText;
            _applyRecordingButton.IsEnabled = plan.CanApplyRecording;
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

    private void SubmitSlideNumber()
    {
        var result = _session.GoToSlide(_slideNumberBox.Text, _notesDirty, _notesText.Text);
        _notesDirty &= !result.NotesCommitted;
        if (!result.CommandInvoked)
            return;

        RefreshFromState();
    }

    private void CommitNotes()
    {
        _notesDirty &= !_session.CommitNotes(_notesDirty, _notesText.Text);
    }

    private static Border BuildPreviewPanel(
        string heading,
        TextBlock label,
        SlideCanvas preview)
    {
        var panel = new Grid { Margin = new Thickness(heading == "Current" ? 0 : 8, 0, 0, 0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = MakeText(13, FontWeights.Normal);
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
