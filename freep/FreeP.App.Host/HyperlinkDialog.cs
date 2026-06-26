using System.Windows;
using System.Windows.Controls;
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
public sealed class HyperlinkDialog : Window
{
    // ── Controls ──────────────────────────────────────────────────────────────────

    private readonly RadioButton _urlRadio;
    private readonly RadioButton _slideRadio;
    private readonly TextBox     _urlBox;
    private readonly ComboBox    _slideCombo;
    private readonly TextBox     _tooltipBox;

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
    {
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
        for (int i = 0; i < slides.Count; i++)
        {
            var slide = slides[i];
            var title = !string.IsNullOrWhiteSpace(slide.Title) ? slide.Title : $"Slide {i + 1}";
            _slideCombo.Items.Add(new SlideItem(slide.Id, $"{i + 1}. {title}"));
        }
        if (_slideCombo.Items.Count > 0) _slideCombo.SelectedIndex = 0;
        Grid.SetRow(_slideCombo, 2); Grid.SetColumn(_slideCombo, 1);
        grid.Children.Add(_slideCombo);

        // Tooltip box
        var tooltipLabel = new Label { Content = "Tooltip:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(tooltipLabel, 3); Grid.SetColumn(tooltipLabel, 0);
        grid.Children.Add(tooltipLabel);

        _tooltipBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(_tooltipBox, 3); Grid.SetColumn(_tooltipBox, 1);
        grid.Children.Add(_tooltipBox);

        // Buttons
        var okBtn     = new Button { Content = "OK",     Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancelBtn = new Button { Content = "Cancel", Width = 75, Margin = new Thickness(0, 0, 0, 0), IsCancel = true  };
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttonRow.Children.Add(okBtn);
        buttonRow.Children.Add(cancelBtn);
        Grid.SetRow(buttonRow, 4); Grid.SetColumnSpan(buttonRow, 2);
        grid.Children.Add(buttonRow);

        Content = grid;

        // ── Event wiring ────────────────────────────────────────────────────────

        _urlRadio.Checked   += (_, _) => UpdateEnabled();
        _slideRadio.Checked += (_, _) => UpdateEnabled();

        okBtn.Click     += OnOk;
        cancelBtn.Click += (_, _) => DialogResult = false;

        // ── Pre-fill from current hyperlink ─────────────────────────────────────

        if (current is not null)
        {
            if (current.IsExternal)
            {
                _urlRadio.IsChecked = true;
                _urlBox.Text        = current.Url ?? string.Empty;
            }
            else
            {
                _slideRadio.IsChecked = true;
                // Select the matching slide in the ComboBox.
                for (int i = 0; i < _slideCombo.Items.Count; i++)
                {
                    if (_slideCombo.Items[i] is SlideItem si && si.Id == current.TargetSlideId)
                    {
                        _slideCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            _tooltipBox.Text = current.Tooltip ?? string.Empty;
        }

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

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_urlRadio.IsChecked == true)
        {
            var url = _urlBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, "Please enter a URL (e.g. https://example.com).", "Insert Hyperlink",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Validate: only http, https, mailto.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https" or "mailto"))
            {
                MessageBox.Show(this, "Only http, https, and mailto URLs are supported.", "Insert Hyperlink",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = new Hyperlink { Url = url, Tooltip = NullIfEmpty(_tooltipBox.Text) };
        }
        else
        {
            if (_slideCombo.SelectedItem is not SlideItem selected)
            {
                MessageBox.Show(this, "Please select a target slide.", "Insert Hyperlink",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = new Hyperlink { TargetSlideId = selected.Id, Tooltip = NullIfEmpty(_tooltipBox.Text) };
        }

        DialogResult = true;
    }

    private static string? NullIfEmpty(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Helper record ─────────────────────────────────────────────────────────────

    private sealed class SlideItem(string id, string display)
    {
        public string Id      { get; } = id;
        public override string ToString() => display;
    }
}
