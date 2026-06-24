using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The values returned by <see cref="ParagraphBreaksDialog"/>: the full paragraph formatting (indents +
/// spacing from the Indents and Spacing tab) and a Line and Page Breaks snapshot.
/// </summary>
internal sealed record ParagraphBreaksResult(
    double LeftPt,
    double RightPt,
    double FirstLinePt,
    double SpaceBeforePt,
    double SpaceAfterPt,
    double LineSpacing,
    bool KeepWithNext,
    bool KeepLinesTogether,
    bool WidowControl,
    bool PageBreakBefore,
    bool SuppressAutoHyphens);

/// <summary>
/// A two-tab Paragraph dialog matching Word's Home > Paragraph dialog-launcher. The <b>Indents and
/// Spacing</b> tab exposes the fields already in <see cref="ParagraphIndentDialog"/> (left/right indent,
/// first-line / hanging, space before/after, line spacing). The <b>Line and Page Breaks</b> tab surfaces
/// the existing backed <see cref="ParagraphFormatting"/> toggles:
/// <list type="bullet">
/// <item>Keep with Next (<c>w:keepNext</c>)</item>
/// <item>Keep Lines Together (<c>w:keepLines</c>)</item>
/// <item>Widow/Orphan Control (<c>w:widowControl</c>)</item>
/// <item>Page Break Before (<c>w:pageBreakBefore</c>)</item>
/// <item>Don't add space between paragraphs of same style (<c>w:contextualSpacing</c>) — not yet backed, deferred</item>
/// <item>Suppress line numbers (<c>w:suppressLineNumbers</c>) — not yet backed, deferred</item>
/// <item>Suppress automatic hyphenation (<c>w:suppressAutoHyphens</c>)</item>
/// </list>
/// All fields round-trip through <see cref="ParagraphFormatting"/> and docx.
/// </summary>
internal static class ParagraphBreaksDialog
{
    private enum Special { None, FirstLine, Hanging }

    /// <summary>
    /// Show the Paragraph dialog seeded from <paramref name="current"/>. Returns the edited values, or
    /// null if cancelled.
    /// </summary>
    public static ParagraphBreaksResult? Prompt(Window? owner, ParagraphFormatting current)
    {
        ParagraphBreaksResult? result = null;

        var dialog = new Window
        {
            Title = "Paragraph",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        // ── Indents and Spacing tab ──────────────────────────────────────────
        var leftBox     = NumberBox(current.IndentLeftPt);
        var rightBox    = NumberBox(current.IndentRightPt);
        var spaceBefore = NumberBox(current.SpaceBeforePt);
        var spaceAfter  = NumberBox(current.SpaceAfterPt);
        var lineSpacing = NumberBox(current.LineSpacing);

        // Decode signed first-line into special combo + magnitude
        var firstLinePt = current.FirstLineIndentPt;
        var specialKind = firstLinePt > 0 ? Special.FirstLine : firstLinePt < 0 ? Special.Hanging : Special.None;
        var specialAmt  = Math.Abs(firstLinePt);

        var specialAmtBox = NumberBox(specialAmt);
        var specialBox = new ComboBox { MinWidth = 120, Margin = new Thickness(0, 0, 0, 8) };
        specialBox.Items.Add("(none)");
        specialBox.Items.Add("First line");
        specialBox.Items.Add("Hanging");
        specialBox.SelectedIndex = (int)specialKind;
        specialBox.SelectionChanged += (_, _) =>
            specialAmtBox.IsEnabled = specialBox.SelectedIndex != (int)Special.None;
        specialAmtBox.IsEnabled = specialKind != Special.None;

        var indentsPanel = new Grid { Margin = new Thickness(10) };
        indentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        indentsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++)
            indentsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddGridRow(indentsPanel, 0, "Left indent (pt):",     leftBox);
        AddGridRow(indentsPanel, 1, "Right indent (pt):",    rightBox);
        AddGridRow(indentsPanel, 2, "Special:",              specialBox);
        AddGridRow(indentsPanel, 3, "By (pt):",              specialAmtBox);
        AddGridRow(indentsPanel, 4, "Space before (pt):",    spaceBefore);
        AddGridRow(indentsPanel, 5, "Space after (pt):",     spaceAfter);
        AddGridRow(indentsPanel, 6, "Line spacing (×):",     lineSpacing);
        indentsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Line and Page Breaks tab ─────────────────────────────────────────
        var keepWithNextCheck      = new CheckBox { Content = "Keep with next",         IsChecked = current.KeepWithNext,       Margin = new Thickness(0, 0, 0, 6) };
        var keepLinesTogetherCheck = new CheckBox { Content = "Keep lines together",    IsChecked = current.KeepLinesTogether,  Margin = new Thickness(0, 0, 0, 6) };
        var widowControlCheck      = new CheckBox { Content = "Widow/orphan control",   IsChecked = current.WidowControl,       Margin = new Thickness(0, 0, 0, 6) };
        var pageBreakBeforeCheck   = new CheckBox { Content = "Page break before",      IsChecked = current.PageBreakBefore,    Margin = new Thickness(0, 0, 0, 6) };
        var suppressHyphensCheck   = new CheckBox { Content = "Suppress auto-hyphenation", IsChecked = current.SuppressAutoHyphens, Margin = new Thickness(0, 0, 0, 6) };

        var breaksPanel = new StackPanel { Margin = new Thickness(10) };
        breaksPanel.Children.Add(new TextBlock
        {
            Text = "Pagination",
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        breaksPanel.Children.Add(keepWithNextCheck);
        breaksPanel.Children.Add(keepLinesTogetherCheck);
        breaksPanel.Children.Add(widowControlCheck);
        breaksPanel.Children.Add(pageBreakBeforeCheck);
        breaksPanel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 8) });
        breaksPanel.Children.Add(new TextBlock
        {
            Text = "Formatting exceptions",
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        breaksPanel.Children.Add(suppressHyphensCheck);
        breaksPanel.Children.Add(new TextBlock
        {
            Text = "Note: 'Don’t add space between paragraphs of same style' and 'Suppress line numbers' are not yet modelled.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 10,
            Margin = new Thickness(0, 8, 0, 0),
        });

        // ── Tab control ──────────────────────────────────────────────────────
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Indents and Spacing", Content = new ScrollViewer { Content = indentsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "Line and Page Breaks", Content = new ScrollViewer { Content = breaksPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        // ── Buttons ──────────────────────────────────────────────────────────
        void Accept()
        {
            if (!TryParse(leftBox.Text,     out var left)   || left   < 0
             || !TryParse(rightBox.Text,    out var right)  || right  < 0
             || !TryParse(specialAmtBox.Text, out var spAmt) || spAmt < 0
             || !TryParse(spaceBefore.Text, out var sbPt)   || sbPt  < 0
             || !TryParse(spaceAfter.Text,  out var saPt)   || saPt  < 0
             || !TryParse(lineSpacing.Text, out var ls)     || ls    <= 0)
            {
                DialogMessageHelper.ShowWarning(dialog, "Enter valid non-negative values in points; line spacing must be positive.");
                return;
            }

            var firstLine = specialBox.SelectedIndex switch
            {
                (int)Special.FirstLine => spAmt,
                (int)Special.Hanging   => -spAmt,
                _                      => 0.0,
            };

            result = new ParagraphBreaksResult(
                LeftPt:             left,
                RightPt:            right,
                FirstLinePt:        firstLine,
                SpaceBeforePt:      sbPt,
                SpaceAfterPt:       saPt,
                LineSpacing:        ls,
                KeepWithNext:       keepWithNextCheck.IsChecked      == true,
                KeepLinesTogether:  keepLinesTogetherCheck.IsChecked == true,
                WidowControl:       widowControlCheck.IsChecked      == true,
                PageBreakBefore:    pageBreakBeforeCheck.IsChecked   == true,
                SuppressAutoHyphens: suppressHyphensCheck.IsChecked  == true);
            dialog.DialogResult = true;
        }

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 10, 0, 0));

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(tabs);
        root.Children.Add(buttons);
        dialog.Content = root;

        leftBox.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private static TextBox NumberBox(double value) => new()
    {
        Text = value.ToString("0.##", CultureInfo.CurrentCulture),
        MinWidth = 120,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static void AddGridRow(Grid grid, int row, string label, UIElement field)
    {
        var block = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        grid.Children.Add(block);

        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        if (field is FrameworkElement fe)
            fe.Margin = new Thickness(0, 4, 0, 4);
        grid.Children.Add(field);
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
