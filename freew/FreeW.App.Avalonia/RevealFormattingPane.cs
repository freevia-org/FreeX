using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// FreeW Avalonia Reveal Formatting pane: shows the effective FONT / PARAGRAPH / SECTION
/// formatting at the caret in a right-docked read-only side pane. Mirrors the WPF host's
/// Shift+F1 / View → Reveal Formatting behaviour using Avalonia controls. Consumes
/// <see cref="RevealFormatting.Describe"/> from the portable model tier and
/// <see cref="DocumentView.GetCaretFormatting"/> from the Avalonia editor surface;
/// does NOT duplicate any model logic.
///
/// Construction: pass the <see cref="DocumentView"/> once. Wire
/// <see cref="DocumentView.DocumentChanged"/> to call <see cref="Refresh"/>. Toggle
/// <see cref="IsVisible"/> via the View ribbon command (<c>freew.reveal-formatting</c>);
/// defaults to hidden.
/// </summary>
public sealed class RevealFormattingPane : UserControl
{
    // ── State ─────────────────────────────────────────────────────────────────

    private readonly DocumentView _editor;
    private readonly StackPanel _content;

    // ── Style constants (mirror NavigationPane / ReviewingPane chrome) ────────

    private static readonly Color PaneBg = Color.FromRgb(0xF3, 0xF3, 0xF3);
    private static readonly Color PaneBorder = Color.FromRgb(0xDD, 0xDD, 0xDD);
    private static readonly Color SectionHeadingColor = Color.FromRgb(0x17, 0x32, 0x4D);
    private static readonly Color LabelColor = Color.FromRgb(0x60, 0x60, 0x60);

    // ── Construction ──────────────────────────────────────────────────────────

    public RevealFormattingPane(DocumentView editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        // --- Scrollable content area -------------------------------------------
        _content = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };

        var scroll = new ScrollViewer
        {
            Content = _content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        // --- Header -------------------------------------------------------------
        var header = new TextBlock
        {
            Text = "Reveal Formatting",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Padding = new Thickness(8, 6),
        };

        // --- Separator below header --------------------------------------------
        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(PaneBorder),
            Margin = new Thickness(0, 0, 0, 2),
        };

        // --- Root layout (DockPanel, right-docked, 260px wide) ----------------
        //   [header]      Dock.Top
        //   [separator]   Dock.Top
        //   [scroll]      fill
        var layout = new DockPanel { Width = 260 };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(separator, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(separator);
        layout.Children.Add(scroll);

        Content = new Border
        {
            Background = new SolidColorBrush(PaneBg),
            BorderBrush = new SolidColorBrush(PaneBorder),
            BorderThickness = new Thickness(1, 0, 0, 0),   // left border (right-docked)
            Child = layout,
        };

        IsVisible = false; // hidden by default; toggled by View ribbon command
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the formatting summary from the editor's current caret position. Call whenever
    /// the document changes or the caret moves (wire to
    /// <see cref="DocumentView.DocumentChanged"/>).
    /// </summary>
    public void Refresh()
    {
        _content.Children.Clear();

        var (run, paragraph) = _editor.GetCaretFormatting();
        var page = _editor.Document.Page;
        var sections = RevealFormatting.Describe(run, paragraph, page);

        foreach (var section in sections)
        {
            // Section heading (e.g. "FONT")
            _content.Children.Add(new TextBlock
            {
                Text = section.Heading,
                FontWeight = FontWeight.Bold,
                FontSize = 11,
                Foreground = new SolidColorBrush(SectionHeadingColor),
                Margin = new Thickness(0, 10, 0, 4),
            });

            // Label → Value rows under the heading
            foreach (var item in section.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = item.Label + ":",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(LabelColor),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                Grid.SetColumn(label, 0);

                var value = new TextBlock
                {
                    Text = item.Value,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top,
                };
                Grid.SetColumn(value, 1);

                row.Children.Add(label);
                row.Children.Add(value);
                _content.Children.Add(row);
            }
        }
    }

    // ── Test-support ──────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <see cref="RevealFormatting.Describe"/> for <paramref name="run"/> and
    /// <paramref name="paragraph"/> against <paramref name="page"/> and returns the section
    /// count. Exposed for headless tests only; the pane itself is not constructed.
    /// </summary>
    internal static int DescribeSectionCount(RunFormatting run, ParagraphFormatting paragraph, PageSettings page) =>
        RevealFormatting.Describe(run, paragraph, page).Count;

    /// <summary>
    /// Calls <see cref="RevealFormatting.Describe"/> and returns the items under the named
    /// <paramref name="sectionHeading"/> as a (Label, Value) list. Exposed for headless tests.
    /// </summary>
    internal static IReadOnlyList<RevealFormattingItem> DescribeSection(
        RunFormatting run, ParagraphFormatting paragraph, PageSettings page, string sectionHeading) =>
        RevealFormatting.Describe(run, paragraph, page)
            .FirstOrDefault(s => s.Heading == sectionHeading)?.Items
            ?? Array.Empty<RevealFormattingItem>();
}
