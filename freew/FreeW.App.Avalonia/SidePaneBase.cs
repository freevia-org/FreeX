using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FreeW.App.Avalonia.Editing;

namespace FreeW.App.Avalonia;

/// <summary>
/// Abstract base for FreeW Avalonia docked side panes (Navigation, Reviewing, Reveal Formatting).
/// Owns the common chrome: background/border colours, header text block, horizontal separator, and
/// the <see cref="DocumentView"/> reference. Subclasses provide the panel-specific content and
/// implement <see cref="Refresh"/> to rebuild it on document change.
///
/// Construction pattern:
/// <list type="number">
///   <item>Subclass calls <c>base(editor, "Title", width, borderThickness)</c>.</item>
///   <item>Base sets <see cref="UserControl.Content"/> to the chrome wrapper; subclass receives the
///         inner <see cref="DockPanel"/> via <see cref="InnerLayout"/> and appends its controls.</item>
///   <item><see cref="IsVisible"/> is set to <see langword="false"/> (hidden by default).</item>
/// </list>
/// </summary>
public abstract class SidePaneBase : UserControl
{
    // ── Chrome palette (shared across all side panes) ─────────────────────────

    /// <summary>Light grey panel background (#F3F3F3). Mirrors NavigationPane / ReviewingPane / RevealFormattingPane.</summary>
    protected static readonly Color PaneBg = Color.FromRgb(0xF3, 0xF3, 0xF3);

    /// <summary>Subtle border / separator colour (#DDDDDD).</summary>
    protected static readonly Color PaneBorderColor = Color.FromRgb(0xDD, 0xDD, 0xDD);

    // ── Shared state ─────────────────────────────────────────────────────────

    /// <summary>The document view whose document drives this pane's content.</summary>
    protected readonly DocumentView _editor;

    /// <summary>
    /// The inner <see cref="DockPanel"/> (fixed width, no explicit height). Subclasses dock their
    /// controls into this panel; the base has already added the header (and optional separator)
    /// as <see cref="Dock.Top"/> children. The final child added by the subclass fills the
    /// remaining space.
    /// </summary>
    protected readonly DockPanel InnerLayout;

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises the chrome: outer border, header, optional separator, and inner layout panel.
    /// </summary>
    /// <param name="editor">The document view. Must not be <see langword="null"/>.</param>
    /// <param name="title">Header label text (e.g. "Navigation", "Tracked Changes").</param>
    /// <param name="width">Fixed pixel width of the pane.</param>
    /// <param name="chromeBorderThickness">
    /// Outer border thickness. Left-docked panes use <c>0,0,1,0</c> (right edge separator);
    /// right-docked panes use <c>1,0,0,0</c> (left edge separator).
    /// </param>
    /// <param name="includeSeparator">
    /// When <see langword="true"/> a 1 px horizontal rule is inserted below the header.
    /// NavigationPane omits this; Reviewing and RevealFormatting include it.
    /// </param>
    protected SidePaneBase(
        DocumentView editor,
        string title,
        double width,
        Thickness chromeBorderThickness,
        bool includeSeparator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));

        // --- Header text block -------------------------------------------------
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            Padding = new Thickness(8, 6),
        };

        // --- Inner layout (subclass docks content here) -----------------------
        InnerLayout = new DockPanel { Width = width };
        DockPanel.SetDock(header, Dock.Top);
        InnerLayout.Children.Add(header);

        if (includeSeparator)
        {
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(PaneBorderColor),
                Margin = new Thickness(0, 0, 0, 2),
            };
            DockPanel.SetDock(separator, Dock.Top);
            InnerLayout.Children.Add(separator);
        }

        // --- Chrome wrapper ---------------------------------------------------
        Content = new Border
        {
            Background = new SolidColorBrush(PaneBg),
            BorderBrush = new SolidColorBrush(PaneBorderColor),
            BorderThickness = chromeBorderThickness,
            Child = InnerLayout,
        };

        IsVisible = false; // hidden by default; toggled by ribbon commands
    }

    // ── Abstract refresh ──────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the pane content from the editor's current document/caret state. Called by
    /// <see cref="MainWindow"/> whenever <see cref="DocumentView.DocumentChanged"/> fires and the
    /// pane is visible.
    /// </summary>
    public abstract void Refresh();
}
