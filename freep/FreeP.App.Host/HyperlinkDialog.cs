using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Modal dialog for inserting or editing a hyperlink on the selected shape (Wave 11A).
///
/// The dialog lets the user choose between:
///  • External URL (http/https/mailto/local file)  — typed into the URL box
///  • Internal slide jump               — selected from the slide list
///
/// An optional tooltip field is always visible.
///
/// OK calls <see cref="EditingSession.SetShapeHyperlink"/> (undoable).
/// Cancel discards.
/// </summary>
public sealed class HyperlinkDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly HyperlinkDialogSession _session;

    // ── Controls ──────────────────────────────────────────────────────────────────

    private readonly RadioButton _urlRadio;
    private readonly RadioButton _slideRadio;
    private readonly TextBox     _urlBox;
    private readonly ComboBox    _slideCombo;
    private readonly TextBox     _tooltipBox;
    private readonly TextBlock   _validationText;

    // ── Result ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The hyperlink the user confirmed, or null if they cancelled or selected "none".
    /// </summary>
    public Hyperlink? Result => _session.Result;

    // ── Construction ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the dialog pre-filled with the current hyperlink (if any) from the first selected shape.
    /// </summary>
    /// <param name="slides">All slides in the presentation (for the internal jump list).</param>
    /// <param name="current">The existing hyperlink to pre-fill, or null for a new link.</param>
    public HyperlinkDialog(IReadOnlyList<Slide> slides, Hyperlink? current = null)
        : this(HyperlinkDialogPlanner.BuildDialogRequest(slides, current))
    {
    }

    public HyperlinkDialog(HyperlinkDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _session = new HyperlinkDialogSession(request);
        var surface = _session.Surface;

        Title                 = surface.Title;
        Width                 = 420;
        SizeToContent         = SizeToContent.Height;
        ResizeMode            = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);

        // ── Layout grid ────────────────────────────────────────────────────────

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // link type radios
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // URL row
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // slide row
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // tooltip row
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // validation
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Radio buttons for link type
        _urlRadio = new RadioButton
        {
            Content = surface.TargetLabel(HyperlinkDialogTargetKind.Url),
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _slideRadio = new RadioButton
        {
            Content = surface.TargetLabel(HyperlinkDialogTargetKind.Slide),
            Margin = new Thickness(0, 0, 0, 8),
        };
        PresentationDialogControlAdapter.ApplySemantic(_urlRadio, surface.TargetField(HyperlinkDialogTargetKind.Url));
        PresentationDialogControlAdapter.ApplySemantic(_slideRadio, surface.TargetField(HyperlinkDialogTargetKind.Slide));

        var radioPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        radioPanel.Children.Add(_urlRadio);
        radioPanel.Children.Add(_slideRadio);
        Grid.SetColumnSpan(radioPanel, 2);
        Grid.SetRow(radioPanel, 0);
        grid.Children.Add(radioPanel);

        // URL box
        var urlField = surface.Field(HyperlinkDialogField.Url);
        var urlLabel = new Label { Content = urlField.Label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(urlLabel, 1); Grid.SetColumn(urlLabel, 0);
        grid.Children.Add(urlLabel);

        _urlBox = new TextBox { Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        PresentationDialogControlAdapter.ApplySemantic(_urlBox, urlField);
        Grid.SetRow(_urlBox, 1); Grid.SetColumn(_urlBox, 1);
        grid.Children.Add(_urlBox);

        // Slide ComboBox
        var slideField = surface.Field(HyperlinkDialogField.Slide);
        var slideLabel = new Label { Content = slideField.Label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(slideLabel, 2); Grid.SetColumn(slideLabel, 0);
        grid.Children.Add(slideLabel);

        _slideCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        PresentationDialogControlAdapter.ApplySemantic(_slideCombo, slideField);
        foreach (var option in _session.SlideOptions)
        {
            _slideCombo.Items.Add(option);
        }
        _slideCombo.SelectedIndex = _session.State.SelectedSlideIndex;
        Grid.SetRow(_slideCombo, 2); Grid.SetColumn(_slideCombo, 1);
        grid.Children.Add(_slideCombo);

        // Tooltip box
        var tooltipField = surface.Field(HyperlinkDialogField.Tooltip);
        var tooltipLabel = new Label { Content = tooltipField.Label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(tooltipLabel, 3); Grid.SetColumn(tooltipLabel, 0);
        grid.Children.Add(tooltipLabel);

        _tooltipBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        PresentationDialogControlAdapter.ApplySemantic(_tooltipBox, tooltipField);
        Grid.SetRow(_tooltipBox, 3); Grid.SetColumn(_tooltipBox, 1);
        grid.Children.Add(_tooltipBox);

        _validationText = new TextBlock
        {
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB7, 0x47, 0x2A)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8),
        };
        PresentationDialogControlAdapter.ApplySemantic(_validationText, surface.Field(HyperlinkDialogField.Validation));
        Grid.SetRow(_validationText, 4);
        Grid.SetColumnSpan(_validationText, 2);
        grid.Children.Add(_validationText);

        var buttonRow = DialogButtonRowFactory.Create(
            OnOk,
            buttonWidth: 75,
            acceptContent: surface.AcceptLabel,
            cancelContent: surface.CancelLabel);
        ApplyAction(
            (Button)buttonRow.Children[0],
            surface.Action(HyperlinkDialogAction.Accept));
        ApplyAction(
            (Button)buttonRow.Children[1],
            surface.Action(HyperlinkDialogAction.Cancel));
        Grid.SetRow(buttonRow, 5); Grid.SetColumnSpan(buttonRow, 2);
        grid.Children.Add(buttonRow);

        Content = grid;

        // ── Event wiring ────────────────────────────────────────────────────────

        // ── Pre-fill from current hyperlink ─────────────────────────────────────

        var state = _session.State;
        RenderInputState(state);

        _urlRadio.Checked += (_, _) => RenderTargetState(
            _session.SelectTarget(HyperlinkDialogTargetKind.Url));
        _slideRadio.Checked += (_, _) => RenderTargetState(
            _session.SelectTarget(HyperlinkDialogTargetKind.Slide));
        _urlBox.TextChanged += (_, _) => _session.SetUrlText(_urlBox.Text);
        _slideCombo.SelectionChanged += (_, _) => _session.SelectSlide(_slideCombo.SelectedIndex);
        _tooltipBox.TextChanged += (_, _) => _session.SetTooltipText(_tooltipBox.Text);

    }

    // ── Enabled state ─────────────────────────────────────────────────────────────

    private void RenderTargetState(HyperlinkDialogViewState state)
    {
        _urlBox.IsEnabled = state.IsUrlInputEnabled;
        _slideCombo.IsEnabled = state.IsSlideInputEnabled;
    }

    private void RenderInputState(HyperlinkDialogViewState state)
    {
        _urlRadio.IsChecked = state.TargetKind == HyperlinkDialogTargetKind.Url;
        _slideRadio.IsChecked = state.TargetKind == HyperlinkDialogTargetKind.Slide;
        _urlBox.Text = state.UrlText;
        _slideCombo.SelectedIndex = state.SelectedSlideIndex;
        _tooltipBox.Text = state.TooltipText;
        RenderTargetState(state);
    }

    // ── OK handler ────────────────────────────────────────────────────────────────

    internal bool ApplyForVisualEvidence(
        HyperlinkDialogTargetKind targetKind,
        string url,
        int selectedSlideIndex,
        string tooltip)
    {
        var state = _session.SetInput(targetKind, url, selectedSlideIndex, tooltip);
        RenderInputState(state);
        return Apply(showValidationDialog: false);
    }

    private void OnOk() => Apply(showValidationDialog: true);

    private bool Apply(bool showValidationDialog)
    {
        var plan = _session.TryAccept();

        if (!plan.ShouldApply)
        {
            var validation = plan.Validation!;
            _validationText.Text = _session.State.ValidationText;
            if (showValidationDialog)
                DialogMessageHelper.ShowWarning(this, validation.Message, validation.Caption);
            FocusField(validation.FocusField);
            return false;
        }

        _validationText.Text = string.Empty;
        if (IsLoaded)
            DialogResult = true;
        return true;
    }

    private void FocusField(HyperlinkDialogField field)
    {
        if (field == HyperlinkDialogField.Url)
            DialogFocus.FocusAndSelect(_urlBox);
        else if (field == HyperlinkDialogField.Slide)
            _slideCombo.Focus();
    }

    private static void ApplyAction(
        DependencyObject control,
        PresentationDialogActionPlan<HyperlinkDialogAction> action)
    {
        AutomationProperties.SetName(control, action.AccessibleName);
        AutomationProperties.SetAutomationId(control, action.AutomationId);
    }

    // ── Helper record ─────────────────────────────────────────────────────────────

}
