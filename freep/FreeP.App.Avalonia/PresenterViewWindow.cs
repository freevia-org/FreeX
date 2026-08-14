using Avalonia;
using Avalonia.Automation;
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
    private readonly SlideShowPresenterViewHostCoordinator _coordinator;
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

    public PresenterViewWindow(
        Presentation presentation,
        SlideShowPresenterViewOperations operations)
    {
        _presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _coordinator = new SlideShowPresenterViewHostCoordinator(operations);
        var surface = _coordinator.Surface;

        Title = surface.Title;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);
        Width = PresentationPresenterViewVisualMetrics.WindowWidth;
        Height = PresentationPresenterViewVisualMetrics.WindowHeight;
        MinWidth = PresentationPresenterViewVisualMetrics.WindowMinimumWidth;
        MinHeight = PresentationPresenterViewVisualMetrics.WindowMinimumHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = FreePBrushes.PresenterSurface;

        var root = new Grid
        {
            Margin = new Thickness(PresentationPresenterViewVisualMetrics.RootMargin),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(PresentationPresenterViewVisualMetrics.NotesRowHeight),
        });

        var header = new Grid
        {
            Margin = new Thickness(
                0,
                0,
                0,
                PresentationPresenterViewVisualMetrics.SectionBottomMargin),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _statusText = MakeText(
            PresentationPresenterViewVisualMetrics.HeaderFontSize,
            FontWeight.SemiBold);
        _elapsedText = MakeText(
            PresentationPresenterViewVisualMetrics.HeaderFontSize,
            FontWeight.Normal);
        ApplySemantic(_statusText, surface.Field(SlideShowPresenterViewField.Status));
        ApplySemantic(_elapsedText, surface.Field(SlideShowPresenterViewField.Elapsed));
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(
                PresentationPresenterViewVisualMetrics.HeaderControlsSideMargin,
                0,
                PresentationPresenterViewVisualMetrics.HeaderControlsSideMargin,
                0),
        };
        _backButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.Previous),
            () => ExecuteAction(SlideShowPresenterViewAction.Previous));
        _advanceButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.Next),
            () => ExecuteAction(SlideShowPresenterViewAction.Next));
        _slideNumberBox = new TextBox
        {
            Width = PresentationPresenterViewVisualMetrics.SlideNumberWidth,
            Height = PresentationPresenterViewVisualMetrics.SlideNumberHeight,
            Margin = new Thickness(
                PresentationPresenterViewVisualMetrics.SlideNumberLeftMargin,
                0,
                0,
                0),
            VerticalContentAlignment = VerticalAlignment.Center,
            PlaceholderText = surface.Field(SlideShowPresenterViewField.SlideNumber).Label,
        };
        ToolTip.SetTip(
            _slideNumberBox,
            surface.Field(SlideShowPresenterViewField.SlideNumber).HelpText);
        ApplySemantic(_slideNumberBox, surface.Field(SlideShowPresenterViewField.SlideNumber));
        _slideNumberBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                ExecuteAction(SlideShowPresenterViewAction.GoToSlide);
                e.Handled = true;
            }
        };
        _goToSlideButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.GoToSlide),
            () => ExecuteAction(SlideShowPresenterViewAction.GoToSlide));
        _goToSlideButton.IsEnabled = _coordinator.CanGoToSlide;
        _recordTimingsButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.RecordTimings),
            () => ExecuteAction(SlideShowPresenterViewAction.RecordTimings));
        _rehearseTimingsButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.RehearseTimings),
            () => ExecuteAction(SlideShowPresenterViewAction.RehearseTimings));
        _narrationButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.Narration),
            () => ExecuteAction(SlideShowPresenterViewAction.Narration));
        _narrationAndMediaButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.NarrationAndMedia),
            () => ExecuteAction(SlideShowPresenterViewAction.NarrationAndMedia));
        _recordingStatusText = MakeText(
            PresentationPresenterViewVisualMetrics.RecordingStatusFontSize,
            FontWeight.Normal);
        _recordingStatusText.Foreground = FreePBrushes.PresenterMutedText;
        _recordingStatusText.Margin = new Thickness(
            0,
            PresentationPresenterViewVisualMetrics.RecordingStatusTopMargin,
            0,
            0);
        ApplySemantic(
            _recordingStatusText,
            surface.Field(SlideShowPresenterViewField.RecordingStatus));
        _applyRecordingButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.ApplyRecording),
            () => ExecuteAction(SlideShowPresenterViewAction.ApplyRecording));
        controls.Children.Add(_backButton);
        controls.Children.Add(_advanceButton);
        controls.Children.Add(_slideNumberBox);
        controls.Children.Add(_goToSlideButton);
        controls.Children.Add(_recordTimingsButton);
        controls.Children.Add(_rehearseTimingsButton);
        controls.Children.Add(_narrationButton);
        controls.Children.Add(_narrationAndMediaButton);
        controls.Children.Add(_applyRecordingButton);
        var normalButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.ShowScreen),
            () => ExecuteAction(SlideShowPresenterViewAction.ShowScreen));
        var blackButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.BlackScreen),
            () => ExecuteAction(SlideShowPresenterViewAction.BlackScreen));
        var whiteButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.WhiteScreen),
            () => ExecuteAction(SlideShowPresenterViewAction.WhiteScreen));
        var clearInkButton = MakeActionButton(
            surface.Action(SlideShowPresenterViewAction.ClearInk),
            () => ExecuteAction(SlideShowPresenterViewAction.ClearInk));
        normalButton.IsEnabled = _coordinator.CanSetScreenMode;
        blackButton.IsEnabled = _coordinator.CanSetScreenMode;
        whiteButton.IsEnabled = _coordinator.CanSetScreenMode;
        clearInkButton.IsEnabled = _coordinator.CanClearInk;
        _pointerModeCombo = MakePointerModePicker(mode =>
        {
            if (mode is not null)
            {
                _coordinator.SelectPointerMode(mode.Value, RefreshFromState);
            }
        });
        _pointerModeCombo.IsEnabled = _coordinator.CanSelectPointerMode;
        ApplySemantic(_pointerModeCombo, surface.Field(SlideShowPresenterViewField.PointerMode));
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

        var previews = new Grid
        {
            Margin = new Thickness(
                0,
                0,
                0,
                PresentationPresenterViewVisualMetrics.SectionBottomMargin),
        };
        previews.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(
                PresentationPresenterViewVisualMetrics.CurrentPreviewColumnWeight,
                GridUnitType.Star),
        });
        previews.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(
                PresentationPresenterViewVisualMetrics.NextPreviewColumnWeight,
                GridUnitType.Star),
        });
        _currentLabel = MakeText(
            PresentationPresenterViewVisualMetrics.PreviewLabelFontSize,
            FontWeight.SemiBold);
        _nextLabel = MakeText(
            PresentationPresenterViewVisualMetrics.PreviewLabelFontSize,
            FontWeight.SemiBold);
        _currentPreview = MakePreview();
        _nextPreview = MakePreview();
        ApplySemantic(_currentPreview, surface.Field(SlideShowPresenterViewField.CurrentPreview));
        ApplySemantic(_nextPreview, surface.Field(SlideShowPresenterViewField.NextPreview));
        previews.Children.Add(BuildPreviewPanel(
            surface.Field(SlideShowPresenterViewField.CurrentPreview),
            _currentLabel,
            _currentPreview));
        var nextPanel = BuildPreviewPanel(
            surface.Field(SlideShowPresenterViewField.NextPreview),
            _nextLabel,
            _nextPreview);
        Grid.SetColumn(nextPanel, 1);
        previews.Children.Add(nextPanel);
        Grid.SetRow(previews, 1);
        root.Children.Add(previews);

        var notesPanel = new Grid();
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var notesHeading = MakeText(
            PresentationPresenterViewVisualMetrics.NotesHeadingFontSize,
            FontWeight.SemiBold);
        notesHeading.Text = surface.Field(SlideShowPresenterViewField.SpeakerNotes).Label;
        notesHeading.Margin = new Thickness(
            0,
            0,
            0,
            PresentationPresenterViewVisualMetrics.NotesHeadingBottomMargin);
        _notesText = new TextBox
        {
            IsReadOnly = !_coordinator.CanSetNotes,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = FreePBrushes.PresenterPanelSurface,
            Foreground = FreePBrushes.White,
            BorderBrush = FreePBrushes.PresenterBorder,
            Padding = new Thickness(PresentationPresenterViewVisualMetrics.NotesPadding),
        };
        _notesText.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ApplySemantic(_notesText, surface.Field(SlideShowPresenterViewField.SpeakerNotes));
        _notesText.TextChanged += (_, _) =>
        {
            _coordinator.NotifyNotesTextChanged();
        };
        _notesText.LostFocus += (_, _) => CommitNotes();
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
            Interval = SlideShowPresenterViewHostCoordinator.RefreshInterval,
        };
        _refreshTimer.Tick += (_, _) => RefreshFromState();
        Opened += (_, _) =>
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
        _coordinator.Refresh(new SlideShowPresenterViewHostRefreshInput(
            _notesText.IsKeyboardFocusWithin,
            _notesText.Text,
            _slideNumberBox.IsKeyboardFocusWithin), ApplyRefreshPlan);
    }

    private void ApplyRefreshPlan(SlideShowPresenterViewRefreshPlan refresh)
    {
        var plan = refresh.ViewPlan;
        _statusText.Text = plan.StatusText;
        _elapsedText.Text = _coordinator.Surface.FormatElapsed(plan.ElapsedText);
        _currentLabel.Text = plan.CurrentSlideLabel;
        _nextLabel.Text = plan.NextSlideLabel;
        if (refresh.ShouldUpdateNotesText)
            _notesText.Text = plan.NotesText;
        if (refresh.ShouldUpdateSlideNumber && plan.CurrentSlideNumberText is not null)
            _slideNumberBox.Text = plan.CurrentSlideNumberText;
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

    private SlideCanvas MakePreview() => new()
    {
        Presentation = _presentation,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Margin = new Thickness(
            0,
            PresentationPresenterViewVisualMetrics.PreviewTopMargin,
            0,
            0),
    };

    private void ExecuteAction(SlideShowPresenterViewAction action)
    {
        _coordinator.ExecuteAction(
            action,
            new SlideShowPresenterViewHostActionInput(
                _slideNumberBox.Text,
                _notesText.Text),
            RefreshFromState);
    }

    private void CommitNotes() => _coordinator.CommitNotes(_notesText.Text);

    private static Border BuildPreviewPanel(
        PresentationDialogFieldPlan<SlideShowPresenterViewField> field,
        TextBlock label,
        SlideCanvas preview)
    {
        var panel = new Grid
        {
            Margin = new Thickness(
                field.Id == SlideShowPresenterViewField.CurrentPreview
                    ? 0
                    : PresentationPresenterViewVisualMetrics.NextPreviewLeftMargin,
                0,
                0,
                0),
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var title = MakeText(
            PresentationPresenterViewVisualMetrics.PreviewTitleFontSize,
            FontWeight.Normal);
        title.Text = field.Label;
        title.Foreground = FreePBrushes.PresenterMutedText;
        panel.Children.Add(title);
        Grid.SetRow(label, 1);
        label.Margin = new Thickness(
            0,
            PresentationPresenterViewVisualMetrics.PreviewLabelTopMargin,
            0,
            0);
        panel.Children.Add(label);
        Grid.SetRow(preview, 2);
        panel.Children.Add(preview);
        return new Border
        {
            Background = FreePBrushes.PresenterSecondarySurface,
            BorderBrush = FreePBrushes.PresenterBorder,
            BorderThickness = new Thickness(
                PresentationPresenterViewVisualMetrics.PreviewBorderThickness),
            Padding = new Thickness(PresentationPresenterViewVisualMetrics.PreviewPadding),
            Child = panel,
        };
    }

    private static TextBlock MakeText(double size, FontWeight weight) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = FreePBrushes.White,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button MakeActionButton(
        PresentationDialogActionPlan<SlideShowPresenterViewAction> plan,
        Action action)
    {
        var button = new Button
        {
            Content = plan.Label,
            Padding = new Thickness(
                PresentationPresenterViewVisualMetrics.ActionButtonHorizontalPadding,
                PresentationPresenterViewVisualMetrics.ActionButtonVerticalPadding,
                PresentationPresenterViewVisualMetrics.ActionButtonHorizontalPadding,
                PresentationPresenterViewVisualMetrics.ActionButtonVerticalPadding),
            Margin = new Thickness(
                PresentationPresenterViewVisualMetrics.ActionButtonSideMargin,
                0,
                PresentationPresenterViewVisualMetrics.ActionButtonSideMargin,
                0),
            MinWidth = PresentationPresenterViewVisualMetrics.ActionButtonMinimumWidth,
            IsDefault = plan.IsDefault,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        button.Click += (_, _) => action();
        return button;
    }

    private static void ApplySemantic(
        Control control,
        PresentationDialogFieldPlan<SlideShowPresenterViewField> field)
    {
        AutomationProperties.SetName(control, field.AccessibleName);
        AutomationProperties.SetAutomationId(control, field.AutomationId);
        if (!string.IsNullOrWhiteSpace(field.HelpText))
            AutomationProperties.SetHelpText(control, field.HelpText);
    }

    private static ComboBox MakePointerModePicker(Action<SlideShowPresenterPointerMode?> changed)
    {
        var combo = new ComboBox
        {
            ItemsSource = Enum.GetValues<SlideShowPresenterPointerMode>(),
            MinWidth = PresentationPresenterViewVisualMetrics.PointerModeMinimumWidth,
            Margin = new Thickness(
                PresentationPresenterViewVisualMetrics.PointerModeLeftMargin,
                0,
                PresentationPresenterViewVisualMetrics.PointerModeRightMargin,
                0),
        };
        combo.SelectionChanged += (_, _) =>
            changed(combo.SelectedItem is SlideShowPresenterPointerMode mode ? mode : null);
        return combo;
    }
}
