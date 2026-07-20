using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>
/// Modal dialog for inserting or editing a hyperlink on the selected shape (Wave 11A).
///
/// The dialog lets the user choose between:
///  • External URL (http/https/mailto)  — typed into the URL box
///  • Internal slide jump               — selected from the slide list
///
/// An optional tooltip field is always visible.
///
/// OK calls <see cref="EditingSession.SetShapeHyperlink"/> (undoable).
/// Cancel discards.
/// </summary>
public sealed class HyperlinkDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
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
    public Hyperlink? Result { get; private set; }

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

        Title                 = "Insert Hyperlink";
        Width                 = 420;
        SizeToContent         = SizeToContent.Height;
        ResizeMode            = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
        _urlRadio   = new RadioButton { Content = "Web address:", IsChecked = true, Margin = new Thickness(0, 0, 0, 4) };
        _slideRadio = new RadioButton { Content = "Slide in this presentation:", Margin = new Thickness(0, 0, 0, 8) };

        var radioPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        radioPanel.Children.Add(_urlRadio);
        radioPanel.Children.Add(_slideRadio);
        Grid.SetColumnSpan(radioPanel, 2);
        Grid.SetRow(radioPanel, 0);
        grid.Children.Add(radioPanel);

        // URL box
        var urlLabel = new Label { Content = "URL:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(urlLabel, 1); Grid.SetColumn(urlLabel, 0);
        grid.Children.Add(urlLabel);

        _urlBox = new TextBox { Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(_urlBox, 1); Grid.SetColumn(_urlBox, 1);
        grid.Children.Add(_urlBox);

        // Slide ComboBox
        var slideLabel = new Label { Content = "Target slide:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(slideLabel, 2); Grid.SetColumn(slideLabel, 0);
        grid.Children.Add(slideLabel);

        _slideCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 4), VerticalAlignment = VerticalAlignment.Center };
        foreach (var option in request.SlideOptions)
        {
            _slideCombo.Items.Add(option);
        }
        _slideCombo.SelectedIndex = request.SelectedSlideIndex;
        Grid.SetRow(_slideCombo, 2); Grid.SetColumn(_slideCombo, 1);
        grid.Children.Add(_slideCombo);

        // Tooltip box
        var tooltipLabel = new Label { Content = "Tooltip:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(tooltipLabel, 3); Grid.SetColumn(tooltipLabel, 0);
        grid.Children.Add(tooltipLabel);

        _tooltipBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(_tooltipBox, 3); Grid.SetColumn(_tooltipBox, 1);
        grid.Children.Add(_tooltipBox);

        _validationText = new TextBlock
        {
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB7, 0x47, 0x2A)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 8),
        };
        Grid.SetRow(_validationText, 4);
        Grid.SetColumnSpan(_validationText, 2);
        grid.Children.Add(_validationText);

        var buttonRow = DialogButtonRowFactory.Create(OnOk, buttonWidth: 75);
        Grid.SetRow(buttonRow, 5); Grid.SetColumnSpan(buttonRow, 2);
        grid.Children.Add(buttonRow);

        Content = grid;

        // ── Event wiring ────────────────────────────────────────────────────────

        _urlRadio.Checked   += (_, _) => UpdateEnabled();
        _slideRadio.Checked += (_, _) => UpdateEnabled();

        // ── Pre-fill from current hyperlink ─────────────────────────────────────

        var initial = request.InitialState;
        _urlRadio.IsChecked = initial.TargetKind == HyperlinkDialogTargetKind.Url;
        _slideRadio.IsChecked = initial.TargetKind == HyperlinkDialogTargetKind.Slide;
        _urlBox.Text = initial.UrlText;
        _tooltipBox.Text = initial.TooltipText;

        UpdateEnabled();
    }

    // ── Enabled state ─────────────────────────────────────────────────────────────

    private void UpdateEnabled()
    {
        bool isUrl = _urlRadio.IsChecked == true;
        _urlBox.IsEnabled    = isUrl;
        _slideCombo.IsEnabled = !isUrl;
    }

    // ── OK handler ────────────────────────────────────────────────────────────────

    internal bool ApplyForVisualEvidence(
        HyperlinkDialogTargetKind targetKind,
        string url,
        int selectedSlideIndex,
        string tooltip)
    {
        _urlRadio.IsChecked = targetKind == HyperlinkDialogTargetKind.Url;
        _slideRadio.IsChecked = targetKind == HyperlinkDialogTargetKind.Slide;
        _urlBox.Text = url;
        _slideCombo.SelectedIndex = selectedSlideIndex;
        _tooltipBox.Text = tooltip;
        UpdateEnabled();
        return Apply(showValidationDialog: false);
    }

    private void OnOk() => Apply(showValidationDialog: true);

    private bool Apply(bool showValidationDialog)
    {
        var targetKind = _urlRadio.IsChecked == true
            ? HyperlinkDialogTargetKind.Url
            : HyperlinkDialogTargetKind.Slide;
        var selectedSlideId = (_slideCombo.SelectedItem as HyperlinkDialogSlideOption)?.Id;
        var plan = HyperlinkDialogPlanner.BuildResult(
            targetKind,
            _urlBox.Text,
            selectedSlideId,
            _tooltipBox.Text);

        if (!plan.ShouldApply)
        {
            var validation = plan.Validation!;
            _validationText.Text = validation.Message;
            if (showValidationDialog)
                DialogMessageHelper.ShowWarning(this, validation.Message, validation.Caption);
            FocusField(validation.FocusField);
            return false;
        }

        _validationText.Text = string.Empty;
        Result = plan.Result;
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

    // ── Helper record ─────────────────────────────────────────────────────────────

}
