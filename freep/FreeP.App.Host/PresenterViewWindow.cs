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
    private readonly Button _narrationButton;
    private readonly Button _narrationAndMediaButton;
    private readonly Button _applyRecordingButton;
    private readonly TextBlock _recordingStatusText;
    private readonly Action? _goBack;
    private readonly Action? _goNext;
    private readonly Action<int>? _goToSlide;
    private readonly TextBox _slideNumberBox;
    private readonly Button _goToSlideButton;
    private readonly ComboBox _pointerModeCombo;
    private readonly Action<SlideShowScreenMode>? _setScreenMode;
    private readonly Action<SlideShowPresenterPointerMode>? _selectPointerMode;
    private readonly Action? _clearInk;
    private readonly Action<SlideShowTimingIntent>? _setTimingIntent;
    private readonly Action<SlideShowRecordingMediaIntent>? _setMediaIntent;
    private readonly Func<SlideShowRecordingReviewPlan>? _recordingReviewProvider;
    private readonly Func<SlideShowRecordingReviewApplyResult>? _applyRecordingReview;
    private readonly Action<int, string?>? _setNotesText;
    private bool _notesDirty;
    private bool _refreshing;
    private int? _notesSlideIndex;

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
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _goBack = goBack;
        _goNext = goNext;
        _goToSlide = goToSlide;
        _setScreenMode = setScreenMode;
        _selectPointerMode = selectPointerMode;
        _clearInk = clearInk;
        _setTimingIntent = setTimingIntent;
        _setMediaIntent = setMediaIntent;
        _recordingReviewProvider = recordingReviewProvider;
        _applyRecordingReview = applyRecordingReview;
        _setNotesText = setNotesText;

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
            _goBack?.Invoke();
            RefreshFromState();
        });
        _advanceButton = MakeActionButton("Next", () =>
        {
            CommitNotes();
            _goNext?.Invoke();
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
        _goToSlideButton.IsEnabled = _goToSlide is not null;
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
        _narrationButton = MakeActionButton("Narration", () =>
        {
            if (_setMediaIntent is not null)
            {
                var current = _stateProvider().ToolPlan.Recording.MediaIntent;
                _setMediaIntent(current == SlideShowRecordingMediaIntent.Narration
                    ? SlideShowRecordingMediaIntent.None
                    : SlideShowRecordingMediaIntent.Narration);
                RefreshFromState();
            }
        });
        _narrationAndMediaButton = MakeActionButton("Narration + camera", () =>
        {
            if (_setMediaIntent is not null)
            {
                var current = _stateProvider().ToolPlan.Recording.MediaIntent;
                _setMediaIntent(current == SlideShowRecordingMediaIntent.NarrationAndMedia
                    ? SlideShowRecordingMediaIntent.None
                    : SlideShowRecordingMediaIntent.NarrationAndMedia);
                RefreshFromState();
            }
        });
        _recordingStatusText = MakeText(13, FontWeights.Normal);
        _recordingStatusText.Foreground = new SolidColorBrush(Color.FromRgb(170, 178, 194));
        _recordingStatusText.Margin = new Thickness(0, 6, 0, 0);
        _applyRecordingButton = MakeActionButton("Apply recording", () =>
        {
            if (_applyRecordingReview is not null)
            {
                var result = _applyRecordingReview();
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
            IsReadOnly = _setNotesText is null,
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
            if (!_refreshing && _setNotesText is not null)
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

        var state = _stateProvider();
        var plan = SlideShowPresenterViewPlanner.Build(state);
        _refreshing = true;
        try
        {
            _statusText.Text = plan.StatusText;
            _elapsedText.Text = $"Elapsed {plan.ElapsedText}";
            _currentLabel.Text = plan.CurrentSlideLabel;
            _nextLabel.Text = plan.NextSlideLabel;
            if (!_notesText.IsKeyboardFocusWithin && !_notesDirty)
            {
                _notesText.Text = plan.NotesText;
                _notesSlideIndex = state.CurrentSlide?.SlideIndex;
            }
            if (!_slideNumberBox.IsKeyboardFocusWithin && state.HostState.CurrentSlideIndex >= 0)
            {
                _slideNumberBox.Text = (state.HostState.CurrentSlideIndex + 1)
                    .ToString(CultureInfo.InvariantCulture);
            }
            _backButton.IsEnabled = plan.CanGoBack && _goBack is not null;
            _advanceButton.IsEnabled = plan.CanAdvance && _goNext is not null;
            _recordTimingsButton.Content = plan.IsRecordingTimings ? "Stop recording" : "Record timings";
            _recordTimingsButton.IsEnabled = _setTimingIntent is not null;
            _rehearseTimingsButton.Content = plan.IsRehearsingTimings ? "Stop rehearsal" : "Rehearse timings";
            _rehearseTimingsButton.IsEnabled = _setTimingIntent is not null;
            var mediaIntent = state.ToolPlan.Recording.MediaIntent;
            _narrationButton.Content = mediaIntent == SlideShowRecordingMediaIntent.Narration
                ? "Stop narration"
                : "Narration";
            _narrationButton.IsEnabled = _setMediaIntent is not null;
            _narrationAndMediaButton.Content = mediaIntent == SlideShowRecordingMediaIntent.NarrationAndMedia
                ? "Stop narration + camera"
                : "Narration + camera";
            _narrationAndMediaButton.IsEnabled = _setMediaIntent is not null;
            var recordingReview = _recordingReviewProvider?.Invoke();
            _recordingStatusText.Text = recordingReview is null
                ? "Recording review unavailable."
                : BuildRecordingSummary(recordingReview);
            _applyRecordingButton.IsEnabled = _applyRecordingReview is not null &&
                recordingReview is not null &&
                (recordingReview.CanApplyRecordedTimings ||
                 recordingReview.PersistableMediaArtifactCount > 0 ||
                 recordingReview.PersistableCaptionArtifactCount > 0);
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
        CommitNotes();
        if (_goToSlide is null ||
            !SlideShowSlideNumberPlanner.TryParseSlideNumber(
                _slideNumberBox.Text,
                out var oneBasedSlideNumber))
        {
            return;
        }

        _goToSlide(oneBasedSlideNumber);
        RefreshFromState();
    }

    private void CommitNotes()
    {
        if (!_notesDirty || _setNotesText is null)
            return;

        // Commit against the slide the box was populated FOR, not whatever the live
        // current slide happens to be now -- auto-advance can move the show forward
        // while the presenter is still mid-edit, and RefreshFromState intentionally
        // leaves a dirty box unpainted (see the IsKeyboardFocusWithin/_notesDirty
        // guards above), so _stateProvider().CurrentSlide can already point at a
        // different slide than the one on screen.
        var slideIndex = _notesSlideIndex;
        if (slideIndex is not int index)
            return;

        _notesDirty = false;
        _setNotesText(index, _notesText.Text);
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

    private static string BuildRecordingSummary(SlideShowRecordingReviewPlan plan)
    {
        if (plan.CompletedSegmentCount == 0)
        {
            return "Recording: no completed slides yet.";
        }

        return $"Recording: {plan.CompletedSegmentCount} slide(s), " +
            $"{plan.TotalRecordedDurationMs / 1000d:F1}s; " +
            $"{plan.PersistableMediaArtifactCount} media + " +
            $"{plan.PersistableCaptionArtifactCount} caption(s) ready" +
            (plan.DeferredMediaArtifactCount > 0
                ? $"; {plan.DeferredMediaArtifactCount} deferred."
                : ".");
    }

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
