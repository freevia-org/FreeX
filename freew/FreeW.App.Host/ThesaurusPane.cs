using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Builds and manages the Thesaurus docked pane (Review > Proofing > Thesaurus, Shift+F7).
///
/// The pane is a right-docked border (like the Reviewing Pane) that shows senses + synonyms
/// for the word at the caret, backed by the bundled <see cref="ThesaurusLookup"/> dataset.
/// Insert (replaces the word in the editor) and Copy are supported.
/// </summary>
internal sealed class ThesaurusPane
{
    private readonly DocumentView _editor;

    // ── UI elements owned by this pane ──────────────────────────────────────────────────────────
    private Border _pane = null!;
    private TextBlock _headingText = null!;
    private TextBlock _statusText = null!;
    private StackPanel _sensesPanel = null!;
    private ScrollViewer _scroll = null!;

    public bool IsVisible { get; private set; }

    public ThesaurusPane(DocumentView editor)
    {
        _editor = editor;
    }

    // ── Build ────────────────────────────────────────────────────────────────────────────────────

    public UIElement Build()
    {
        _headingText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 2),
            TextWrapping = TextWrapping.Wrap
        };

        _statusText = new TextBlock
        {
            Text = "Position the cursor on a word and press Shift+F7.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
            Margin = new Thickness(10, 2, 10, 8),
            TextWrapping = TextWrapping.Wrap
        };

        _sensesPanel = new StackPanel { Margin = new Thickness(0) };

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _sensesPanel
        };

        var header = new TextBlock
        {
            Text = "Thesaurus",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 8, 10, 6)
        };

        var layout = new DockPanel { Width = 260 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_headingText, Dock.Top);
        DockPanel.SetDock(_statusText, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_headingText);
        layout.Children.Add(_statusText);
        layout.Children.Add(_scroll); // fill

        _pane = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFB)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            Child = layout
        };
        return _pane;
    }

    // ── Show / Hide ──────────────────────────────────────────────────────────────────────────────

    public void Toggle()
    {
        IsVisible = !IsVisible;
        _pane.Visibility = IsVisible ? Visibility.Visible : Visibility.Collapsed;
        if (IsVisible)
            Lookup();
    }

    public void Show() { IsVisible = true; _pane.Visibility = Visibility.Visible; Lookup(); }
    public void Hide() { IsVisible = false; _pane.Visibility = Visibility.Collapsed; }

    // ── Lookup ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Looks up the word at the editor's caret and populates the pane.</summary>
    public void Lookup()
    {
        if (!IsVisible) return;

        var word = _editor.GetCaretWord();
        _sensesPanel.Children.Clear();

        if (string.IsNullOrWhiteSpace(word))
        {
            _headingText.Text = string.Empty;
            _statusText.Text = "Position the cursor on a word and press Shift+F7.";
            return;
        }

        var plan = ThesaurusPresentationPlanner.Lookup(word);
        _headingText.Text = plan.HeadingText;
        _statusText.Text = plan.StatusText;

        if (!plan.HasSynonyms)
            return;

        PopulateSenses(plan);
    }

    // ── Rendering ────────────────────────────────────────────────────────────────────────────────

    private void PopulateSenses(ThesaurusDisplayPlan plan)
    {
        foreach (var sense in plan.Senses)
        {
            // Sense header (part-of-speech / sense label)
            var senseLabel = new TextBlock
            {
                Text = sense.DisplayLabel,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x32, 0x4D)),
                Margin = new Thickness(10, 8, 10, 2)
            };
            _sensesPanel.Children.Add(senseLabel);

            // Synonym buttons
            var wrapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 0, 10, 4)
            };
            foreach (var action in sense.Actions)
            {
                var btn = BuildSynonymButton(action);
                wrapPanel.Children.Add(btn);
            }
            _sensesPanel.Children.Add(wrapPanel);

            // Separator
            _sensesPanel.Children.Add(new Separator
            {
                Margin = new Thickness(10, 2, 10, 2),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0))
            });
        }
    }

    private Button BuildSynonymButton(ThesaurusActionRow action)
    {
        // Display synonym with underscores replaced by spaces (storage format)
        var display = action.DisplayText;
        var insertBtn = new Button
        {
            Content = "Insert",
            ToolTip = action.InsertToolTip,
            Padding = new Thickness(3, 1, 3, 1),
            Margin = new Thickness(0, 0, 2, 0),
            FontSize = 10,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        insertBtn.Click += (_, _) =>
        {
            _editor.ReplaceCaretWord(display);
            _editor.Focus();
        };

        var copyBtn = new Button
        {
            Content = "⎘",
            ToolTip = action.CopyToolTip,
            Padding = new Thickness(3, 1, 3, 1),
            FontSize = 10,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        copyBtn.Click += (_, _) =>
        {
            try { Clipboard.SetText(display); }
            catch { /* clipboard might be unavailable in tests */ }
        };

        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 2, 4, 2),
            Padding = new Thickness(4, 2, 2, 2)
        };

        var innerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        innerPanel.Children.Add(new TextBlock
        {
            Text = display,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });
        innerPanel.Children.Add(insertBtn);
        innerPanel.Children.Add(copyBtn);
        container.Child = innerPanel;
        return new Button
        {
            Content = container,
            Style = FindBorderlessButtonStyle(),
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
    }

    private static Style? _borderlessStyle;
    private static Style FindBorderlessButtonStyle()
    {
        if (_borderlessStyle is null)
        {
            _borderlessStyle = new Style(typeof(Button));
            _borderlessStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            _borderlessStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        }
        return _borderlessStyle;
    }
}
