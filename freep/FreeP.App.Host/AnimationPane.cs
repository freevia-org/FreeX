using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

// ══════════════════════════════════════════════════════════════════════════════
// Wave 16B: Animation Pane
//
// A right-side collapsible panel that lists the current slide's animations in
// play order.  Each row shows:
//   • Order number (1-based)
//   • Target shape name (looked up by ShapeId in CurrentSlide.Shapes)
//   • Effect (Kind + Preset)
//   • Trigger (OnClick / WithPrevious / AfterPrevious)
//
// Per-row controls:
//   ▲ / ▼  Move up / Move down (Editor.MoveAnimation)
//   ✕       Remove (Editor.RemoveAnimation)
//   Trigger ComboBox + Duration field → Editor.SetAnimation (undoable)
//
// Selecting a row selects the target shape on the canvas
// (Editor.Select(shapeId)).
//
// A "▶ Preview" button at the top launches the slide show at the current slide
// (calls MainWindow.StartSlideShow via the provided callback so we never touch
// SlideShowWindow directly — stays within the 16B scope).
//
// Refreshes on Editor.CurrentSlideChanged and Editor.Changed.
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// WPF control that displays and edits the animation list for the current slide.
/// Host it in MainWindow as a right-side dock (see 16B SEAM regions in MainWindow).
/// </summary>
public sealed class AnimationPane : Border
{
    // ── Colors (mirrors the FreeP orange theme) ───────────────────────────────────

    private static readonly Brush BackBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)));
    private static readonly Brush HeaderBg     = Freeze(new SolidColorBrush(Color.FromRgb(0xB7, 0x47, 0x2A)));
    private static readonly Brush HeaderFg     = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush RowNormal    = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)));
    private static readonly Brush RowSelected  = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xD6)));
    private static readonly Brush RowBorder    = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));
    private static readonly Brush TextBrush    = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)));
    private static readonly Brush MutedBrush   = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)));
    private static readonly Brush ButtonBg     = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)));

    // ── Fields ────────────────────────────────────────────────────────────────────

    private readonly EditingSession _editor;
    private readonly AnimationPaneSession _session;
    private readonly AnimationPaneControlSchemaPlan _controlSchema;
    private readonly Action<AnimationPanePlaybackSessionPlan>? _onPreview;
    private readonly Action? _onAccessibilityChanged;
    private readonly Action<int>? _onEditMotionPath;

    private readonly StackPanel _listPanel;
    private readonly StackPanel _playbackControlsPanel;

    internal AnimationPaneTimelinePlan CurrentTimelinePlanForTest => BuildTimelinePlan();
    internal AnimationPaneEffectOptionMutationPlan ApplyAnimationPaneEffectOptionEditForTest(
        int animationIndex,
        string optionId)
        => ApplyEffectOptionMutation(animationIndex, optionId);
    internal AnimationPanePlaybackSessionPlan? CurrentPlaybackSessionPlanForTest => _session.Playback;
    internal AnimationPanePlaybackWorkflowEvidencePlan? CurrentPlaybackWorkflowEvidencePlanForTest =>
        _session.PlaybackWorkflowEvidence;
    internal IReadOnlyList<AnimationPanePlaybackControlDescriptor> CurrentPlaybackControlsForTest =>
        BuildTimelinePlan().PlaybackControls;
    internal AnimationPaneWorkflowViewPlan CurrentWorkflowViewPlanForTest => BuildWorkflowViewPlan();
    internal AnimationPaneWorkflowEvidencePlan CurrentWorkflowEvidencePlanForTest =>
        AnimationPanePlanner.BuildWorkflowEvidencePlan(BuildTimelinePlan(), _editor.CurrentSlideIndex);
    internal AnimationPaneControlSchemaPlan ControlSchemaForTests => _controlSchema;
    internal IReadOnlyList<FrameworkElement> AccessibilityItemsForTests =>
        _listPanel.Children.OfType<FrameworkElement>().ToArray();

    // ── Construction ──────────────────────────────────────────────────────────────

    /// <param name="editor">Active editing session.</param>
    /// <param name="onPreview">
    ///   Optional callback called when the "▶ Preview" button is clicked.
    ///   The session carries the selected animation index so "Play From Selected"
    ///   can launch the slideshow at the same row in both hosts.
    ///   May be null (Preview button is hidden in that case).
    /// </param>
    public AnimationPane(
        EditingSession editor,
        Action<AnimationPanePlaybackSessionPlan>? onPreview = null,
        Action? onAccessibilityChanged = null,
        Action<int>? onEditMotionPath = null)
    {
        _editor    = editor    ?? throw new ArgumentNullException(nameof(editor));
        _session = new AnimationPaneSession(() => _editor);
        _controlSchema = AnimationPanePlanner.BuildControlSchema();
        _onPreview = onPreview;
        _onAccessibilityChanged = onAccessibilityChanged;
        _onEditMotionPath = onEditMotionPath;

        Background      = BackBrush;
        BorderBrush     = RowBorder;
        BorderThickness = new Thickness(1, 0, 0, 0);

        _listPanel = new StackPanel { Orientation = Orientation.Vertical };
        _playbackControlsPanel = new StackPanel { Orientation = Orientation.Horizontal };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content                       = _listPanel,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(header);
        root.Children.Add(scroll);

        Child = root;
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.AnimationPaneId,
            isVisible: false);

        // Subscribe to model events.
        _editor.CurrentSlideChanged += (_, _) => Rebuild();
        _editor.Changed             += Rebuild;

        Rebuild();
    }

    // ── Header ────────────────────────────────────────────────────────────────────

    private UIElement BuildHeader()
    {
        var title = new TextBlock
        {
            Text              = _controlSchema.Heading,
            FontSize          = 12,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = HeaderFg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(8, 0, 0, 0),
        };

        var headerPanel = new DockPanel { LastChildFill = true };

        DockPanel.SetDock(_playbackControlsPanel, Dock.Right);
        headerPanel.Children.Add(_playbackControlsPanel);
        headerPanel.Children.Add(title);

        return new Border
        {
            Background = HeaderBg,
            Padding    = new Thickness(0, 4, 4, 4),
            Child      = headerPanel,
        };
    }

    // ── List rebuild ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fully rebuilds the animation list from the current slide.
    /// Called on CurrentSlideChanged and Changed events.
    /// </summary>
    internal void Rebuild()
    {
        _listPanel.Children.Clear();

        var plan = BuildTimelinePlan();
        PresentationPaneAccessibilityAdapter.ApplyPaneMetadata(
            this,
            PresentationPaneAccessibilityPlanner.AnimationPaneId,
            IsVisible,
            plan.Items.Count,
            plan.SelectedIndex);
        RenderPlaybackControls(plan);
        if (!plan.HasAnimations)
        {
            var viewPlan = BuildWorkflowViewPlan(plan);
            _listPanel.Children.Add(new TextBlock
            {
                Text       = viewPlan.EmptyMessage,
                FontSize   = 11,
                Foreground = MutedBrush,
                Margin     = new Thickness(10, 12, 10, 12),
                TextWrapping = TextWrapping.Wrap,
            });
            _onAccessibilityChanged?.Invoke();
            return;
        }

        foreach (var item in plan.Items)
        {
            var row = BuildRow(item);
            _listPanel.Children.Add(row);
        }

        _onAccessibilityChanged?.Invoke();
    }

    private void RenderPlaybackControls(AnimationPaneTimelinePlan plan)
    {
        _playbackControlsPanel.Children.Clear();
        foreach (var control in plan.PlaybackControls)
        {
            var button = new Button
            {
                Content         = control.Label,
                Tag             = control,
                ToolTip         = control.DisabledReason ?? control.ToolTip,
                IsEnabled       = control.IsEnabled,
                Padding         = new Thickness(6, 2, 6, 2),
                Margin          = new Thickness(0, 4, 6, 4),
                Background      = Freeze(new SolidColorBrush(Color.FromRgb(0x8F, 0x37, 0x21))),
                Foreground      = Freeze(new SolidColorBrush(Colors.White)),
                BorderThickness = new Thickness(0),
                FontSize        = 12,
            };
            button.Click += (_, _) => ExecutePlaybackControl(control);
            _playbackControlsPanel.Children.Add(button);
        }
    }

    private void ExecutePlaybackControl(AnimationPanePlaybackControlDescriptor control)
        => ExecutePlaybackControl(control, invokePreview: true);

    internal AnimationPanePlaybackSessionPlan ExecutePlaybackControlForTest(
        AnimationPanePlaybackControlKind controlKind)
    {
        var control = BuildTimelinePlan()
            .PlaybackControls
            .First(candidate => candidate.Kind == controlKind);
        return ExecutePlaybackControl(control, invokePreview: false);
    }

    internal AnimationPaneReorderMutationPlan MoveAnimationForTest(int animationIndex, int offset)
        => ApplyReorderMutation(animationIndex, offset);

    private AnimationPanePlaybackSessionPlan ExecutePlaybackControl(
        AnimationPanePlaybackControlDescriptor control,
        bool invokePreview)
    {
        var transition = _session.ExecutePlayback(control.Kind);
        Rebuild();

        if (invokePreview && transition.ShouldStartPreview)
            _onPreview?.Invoke(transition.Playback);

        return transition.Playback;
    }

    // ── Row construction ──────────────────────────────────────────────────────────

    private UIElement BuildRow(AnimationPaneTimelineItemPlan item)
    {
        bool selected = item.IsSelected;
        var effectOptionsControl = _controlSchema.GetRequired(AnimationPaneControlKind.EffectOptions);
        var wheelSpokesControl = _controlSchema.GetRequired(AnimationPaneControlKind.WheelSpokes);
        var triggerControl = _controlSchema.GetRequired(AnimationPaneControlKind.Trigger);
        var durationControl = _controlSchema.GetRequired(AnimationPaneControlKind.Duration);
        var delayControl = _controlSchema.GetRequired(AnimationPaneControlKind.Delay);
        var repeatControl = _controlSchema.GetRequired(AnimationPaneControlKind.Repeat);
        var autoReverseControl = _controlSchema.GetRequired(AnimationPaneControlKind.AutoReverse);
        var moveEarlierControl = _controlSchema.GetRequired(AnimationPaneControlKind.MoveEarlier);
        var moveLaterControl = _controlSchema.GetRequired(AnimationPaneControlKind.MoveLater);
        var removeControl = _controlSchema.GetRequired(AnimationPaneControlKind.RemoveAnimation);
        var editMotionPathControl = _controlSchema.GetRequired(AnimationPaneControlKind.EditMotionPath);

        // ── Order number ────────────────────────────────────────────────────────
        var orderLabel = new TextBlock
        {
            Text              = item.OrderText,
            FontSize          = 11,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = TextBrush,
            Width             = 20,
            TextAlignment     = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(4, 0, 4, 0),
        };

        // ── Shape name ──────────────────────────────────────────────────────────
        var shapeName = item.ShapeName;
        var nameLabel = new TextBlock
        {
            Text              = shapeName,
            FontSize          = 11,
            Foreground        = TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxWidth          = 80,
            ToolTip           = shapeName,
        };

        // ── Effect label (Kind + Preset) ────────────────────────────────────────
        var effectText = item.EffectText;
        var effectLabel = new TextBlock
        {
            Text              = effectText,
            FontSize          = 10,
            Foreground        = MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            MaxWidth          = 70,
            ToolTip           = effectText,
            Margin            = new Thickness(4, 0, 4, 0),
        };

        var effectOptionCombo = new ComboBox
        {
            FontSize          = 10,
            Width             = 104,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = item.EffectOptions.CanApply
                ? effectOptionsControl.ToolTip
                : item.EffectOptions.DisabledReason,
            IsEnabled         = item.EffectOptions.CanApply,
            Visibility        = item.EffectOptions.Options.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed,
        };
        foreach (var option in item.EffectOptions.Options)
            effectOptionCombo.Items.Add(option.DisplayText);
        for (var i = 0; i < item.EffectOptions.Options.Count; i++)
        {
            if (item.EffectOptions.Options[i].IsSelected)
            {
                effectOptionCombo.SelectedIndex = i;
                break;
            }
        }

        var wheelSpokeCombo = new ComboBox
        {
            FontSize          = 10,
            Width             = 86,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = wheelSpokesControl.ToolTip,
            IsEnabled         = item.EffectOptions.CanApply,
            Visibility        = item.EffectOptions.WheelSpokeOptions.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed,
        };
        foreach (var option in item.EffectOptions.WheelSpokeOptions)
            wheelSpokeCombo.Items.Add(option.DisplayText);
        for (var i = 0; i < item.EffectOptions.WheelSpokeOptions.Count; i++)
        {
            if (item.EffectOptions.WheelSpokeOptions[i].IsSelected)
            {
                wheelSpokeCombo.SelectedIndex = i;
                break;
            }
        }

        // ── Trigger dropdown ────────────────────────────────────────────────────
        var triggerCombo = new ComboBox
        {
            FontSize          = 10,
            Width             = 110,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = triggerControl.ToolTip,
        };
        foreach (var label in triggerControl.OptionLabels)
            triggerCombo.Items.Add(label);
        triggerCombo.SelectedIndex = item.TriggerIndex;

        var repeatCombo = new ComboBox
        {
            FontSize = 10,
            Width = 82,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
            ToolTip = repeatControl.ToolTip,
        };
        foreach (var option in repeatControl.Options)
            repeatCombo.Items.Add(option.Label);
        repeatCombo.SelectedItem = AnimationPanePlanner.FormatRepeat(
            item.RepeatCount,
            item.RepeatIndefinitely);

        var autoReverseCheck = new CheckBox
        {
            IsChecked = item.AutoReverse,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2),
            ToolTip = autoReverseControl.ToolTip,
        };

        // Capture by value for the closure.
        int capturedIndex = item.Index;
        effectOptionCombo.SelectionChanged += (_, _) =>
        {
            if (effectOptionCombo.SelectedIndex < 0
                || effectOptionCombo.SelectedIndex >= item.EffectOptions.Options.Count)
            {
                return;
            }

            var option = item.EffectOptions.Options[effectOptionCombo.SelectedIndex];
            ApplyEffectOptionMutation(capturedIndex, option.Id);
        };

        wheelSpokeCombo.SelectionChanged += (_, _) =>
        {
            if (wheelSpokeCombo.SelectedIndex < 0
                || wheelSpokeCombo.SelectedIndex >= item.EffectOptions.WheelSpokeOptions.Count)
            {
                return;
            }

            var option = item.EffectOptions.WheelSpokeOptions[wheelSpokeCombo.SelectedIndex];
            ApplyEffectOptionMutation(capturedIndex, option.Id);
        };

        triggerCombo.SelectionChanged += (_, _) =>
        {
            _session.ApplyTrigger(capturedIndex, triggerCombo.SelectedIndex);
        };

        void ApplyRepeat()
        {
            var plan = _session.ApplyRepeat(
                capturedIndex,
                repeatCombo.SelectedItem as string,
                autoReverseCheck.IsChecked == true);
            if (!plan.ShouldApply && plan.DisabledReason is not null)
            {
                repeatCombo.SelectedItem = AnimationPanePlanner.FormatRepeat(
                    plan.RepeatCount,
                    plan.RepeatIndefinitely);
                autoReverseCheck.IsChecked = plan.AutoReverse;
            }
        }

        repeatCombo.SelectionChanged += (_, _) => ApplyRepeat();
        autoReverseCheck.Checked += (_, _) => ApplyRepeat();
        autoReverseCheck.Unchecked += (_, _) => ApplyRepeat();

        // ── Duration field ──────────────────────────────────────────────────────
        var durationBox = new TextBox
        {
            Text              = item.DurationText,
            FontSize          = 10,
            Width             = 48,
            VerticalAlignment = VerticalAlignment.Center,
            Padding           = new Thickness(2, 1, 2, 1),
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = durationControl.ToolTip,
        };
        durationBox.LostFocus += (_, _) =>
        {
            var plan = _session.ApplyDuration(capturedIndex, durationBox.Text);
            if (!plan.ShouldApply)
                durationBox.Text = plan.DisplayText;
        };

        var delayBox = new TextBox
        {
            Text              = item.DelayText,
            FontSize          = 10,
            Width             = 48,
            VerticalAlignment = VerticalAlignment.Center,
            Padding           = new Thickness(2, 1, 2, 1),
            Margin            = new Thickness(2, 2, 2, 2),
            ToolTip           = delayControl.ToolTip,
        };
        delayBox.LostFocus += (_, _) =>
        {
            var plan = _session.ApplyDelay(capturedIndex, delayBox.Text);
            if (!plan.ShouldApply)
                delayBox.Text = plan.DisplayText;
        };

        TextBox? decelerationBox = null;
        var accelerationBox = new TextBox
        {
            Text = AnimationPanePlanner.FormatEasing(item.Acceleration),
            FontSize = 10,
            Width = 48,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 1, 2, 1),
            Margin = new Thickness(2, 2, 2, 2),
            ToolTip = "Smooth start",
        };
        accelerationBox.LostFocus += (_, _) =>
        {
            var plan = ApplyEasingMutation(
                capturedIndex,
                accelerationBox.Text,
                decelerationBox?.Text ?? string.Empty);
            if (!plan.ShouldApply)
                accelerationBox.Text = plan.AccelerationText;
        };

        decelerationBox = new TextBox
        {
            Text = AnimationPanePlanner.FormatEasing(item.Deceleration),
            FontSize = 10,
            Width = 48,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 1, 2, 1),
            Margin = new Thickness(2, 2, 2, 2),
            ToolTip = "Smooth end",
        };
        decelerationBox.LostFocus += (_, _) =>
        {
            var plan = ApplyEasingMutation(
                capturedIndex,
                accelerationBox.Text,
                decelerationBox?.Text ?? string.Empty);
            if (!plan.ShouldApply)
                decelerationBox?.SetCurrentValue(TextBox.TextProperty, plan.DecelerationText);
        };

        // ── Move up button ──────────────────────────────────────────────────────
        var upBtn = new Button
        {
            Content             = "▲",
            FontSize            = 9,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            IsEnabled           = item.CanMoveEarlier,
            ToolTip             = moveEarlierControl.ToolTip,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        upBtn.Click += (_, _) =>
        {
            ApplyReorderMutation(capturedIndex, -1);
        };

        // ── Move down button ────────────────────────────────────────────────────
        var downBtn = new Button
        {
            Content             = "▼",
            FontSize            = 9,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            IsEnabled           = item.CanMoveLater,
            ToolTip             = moveLaterControl.ToolTip,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        downBtn.Click += (_, _) =>
        {
            ApplyReorderMutation(capturedIndex, 1);
        };

        // ── Remove button ───────────────────────────────────────────────────────
        var removeBtn = new Button
        {
            Content             = "✕",
            FontSize            = 9,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Foreground          = Freeze(new SolidColorBrush(Color.FromRgb(0xC0, 0x20, 0x20))),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            ToolTip             = removeControl.ToolTip,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        removeBtn.Click += (_, _) =>
        {
            ApplyRemoveMutation(capturedIndex);
        };

        var paragraphBuildPlan = AnimationPanePlanner.BuildParagraphBuildMutationPlan(
            _editor.CurrentSlide,
            item.ShapeId);
        var paragraphBuildBtn = new Button
        {
            Content             = "¶",
            FontSize            = 10,
            Width               = 18,
            Height              = 18,
            Padding             = new Thickness(0),
            Margin              = new Thickness(1),
            Background          = ButtonBg,
            BorderThickness     = new Thickness(1),
            IsEnabled           = paragraphBuildPlan.ShouldApply,
            ToolTip             = paragraphBuildPlan.DisabledReason ?? paragraphBuildPlan.DisplayText,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        paragraphBuildBtn.Click += (_, _) =>
        {
            var plan = _session.ToggleParagraphBuild(item.ShapeId);
            if (plan.ShouldApply)
                Rebuild();
        };

        Button? editMotionPathBtn = null;
        if (item.Kind == AnimationKind.Motion && _onEditMotionPath is not null)
        {
            editMotionPathBtn = new Button
            {
                Content = editMotionPathControl.Label,
                FontSize = 9,
                MinWidth = 34,
                Height = 18,
                Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(1),
                Background = ButtonBg,
                BorderThickness = new Thickness(1),
                ToolTip = editMotionPathControl.ToolTip,
                VerticalAlignment = VerticalAlignment.Center,
            };
            editMotionPathBtn.Click += (_, _) => _onEditMotionPath(item.Index);
        }

        // ── Assemble button cluster ──────────────────────────────────────────────
        var btnPanel = new StackPanel
        {
            Orientation       = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        btnPanel.Children.Add(upBtn);
        btnPanel.Children.Add(downBtn);
        btnPanel.Children.Add(paragraphBuildBtn);
        if (editMotionPathBtn is not null)
            btnPanel.Children.Add(editMotionPathBtn);
        btnPanel.Children.Add(removeBtn);

        // ── Inner content panel ──────────────────────────────────────────────────
        var innerGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // order
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // name
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // effect
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // effect option
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // wheel spokes
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // trigger
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // duration
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // delay
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // repeat
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // auto reverse
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // smooth start
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // smooth end
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // buttons

        Grid.SetColumn(orderLabel,  0);
        Grid.SetColumn(nameLabel,   1);
        Grid.SetColumn(effectLabel, 2);
        Grid.SetColumn(effectOptionCombo, 3);
        Grid.SetColumn(wheelSpokeCombo, 4);
        Grid.SetColumn(triggerCombo, 5);
        Grid.SetColumn(durationBox, 6);
        Grid.SetColumn(delayBox,    7);
        Grid.SetColumn(repeatCombo, 8);
        Grid.SetColumn(autoReverseCheck, 9);
        Grid.SetColumn(accelerationBox, 10);
        Grid.SetColumn(decelerationBox, 11);
        Grid.SetColumn(btnPanel,    12);

        innerGrid.Children.Add(orderLabel);
        innerGrid.Children.Add(nameLabel);
        innerGrid.Children.Add(effectLabel);
        innerGrid.Children.Add(effectOptionCombo);
        innerGrid.Children.Add(wheelSpokeCombo);
        innerGrid.Children.Add(triggerCombo);
        innerGrid.Children.Add(durationBox);
        innerGrid.Children.Add(delayBox);
        innerGrid.Children.Add(repeatCombo);
        innerGrid.Children.Add(autoReverseCheck);
        innerGrid.Children.Add(accelerationBox);
        innerGrid.Children.Add(decelerationBox);
        innerGrid.Children.Add(btnPanel);

        // ── Row border ───────────────────────────────────────────────────────────
        var row = new Border
        {
            Tag             = item.Index,
            Background      = selected ? RowSelected : RowNormal,
            BorderBrush     = RowBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding         = new Thickness(4, 4, 4, 4),
            Child           = innerGrid,
            Cursor          = System.Windows.Input.Cursors.Hand,
        };
        PresentationPaneAccessibilityAdapter.ApplyItem(
            row,
            PresentationPaneAccessibilityPlanner.AnimationPaneId,
            item.Index,
            item.ShapeName,
            selected ? "Selected" : "Not selected");

        // Click → select this row and select the shape on the canvas.
        row.MouseLeftButtonDown += (sender, _) =>
        {
            _session.SelectAnimation(capturedIndex);
            UpdateRowHighlights();
        };

        return row;
    }

    private AnimationPaneEffectOptionMutationPlan ApplyEffectOptionMutation(
        int animationIndex,
        string optionId)
    {
        var plan = _session.ApplyEffectOption(animationIndex, optionId);
        if (plan.ShouldApply)
            Rebuild();
        return plan;
    }

    // ── Highlight update ──────────────────────────────────────────────────────────

    /// <summary>Updates row backgrounds after a selection change without a full rebuild.</summary>
    private void UpdateRowHighlights()
    {
        for (int i = 0; i < _listPanel.Children.Count; i++)
        {
            if (_listPanel.Children[i] is Border b && b.Tag is int rowIdx)
                b.Background = rowIdx == _session.SelectedAnimationIndex ? RowSelected : RowNormal;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private AnimationPaneReorderMutationPlan ApplyReorderMutation(int animationIndex, int offset)
    {
        return _session.MoveAnimation(animationIndex, offset);
    }

    internal AnimationPaneRemoveMutationPlan RemoveAnimationForTest(int animationIndex) =>
        ApplyRemoveMutation(animationIndex);

    private AnimationPaneRemoveMutationPlan ApplyRemoveMutation(int animationIndex)
    {
        var plan = _session.RemoveAnimation(animationIndex);
        if (plan.ShouldApply)
        {
            Rebuild();
        }

        return plan;
    }

    private AnimationPaneTimelinePlan BuildTimelinePlan()
        => _session.Refresh();

    private AnimationPaneWorkflowViewPlan BuildWorkflowViewPlan()
        => BuildWorkflowViewPlan(BuildTimelinePlan());

    private AnimationPaneWorkflowViewPlan BuildWorkflowViewPlan(AnimationPaneTimelinePlan plan)
        => (_session.WorkflowEvidence ??
            AnimationPanePlanner.BuildWorkflowEvidencePlan(plan, _editor.CurrentSlideIndex)).View;

    // ── Static freeze helper ──────────────────────────────────────────────────────

    private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }
}
