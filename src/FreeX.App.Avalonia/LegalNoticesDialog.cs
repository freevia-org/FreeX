using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

/// <summary>FreeX localized adapter for the shared Avalonia Legal Notices surface.</summary>
internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
{
    // WPF SharedLegalNoticesDialog hosts each notice TextBox directly in its TabItem, so the
    // default TabControl padding (2px) separates the document border from the tab pane on every
    // side. The shared Avalonia renderer keeps the left/right inset; FreeX restores the top and
    // bottom inset so the pane's top edge stays visible below the tab strip.
    private const double WpfTabPaneVerticalInset = 2;

    public LegalNoticesDialog()
        : base(
            FreeXLegalNoticesPresentation.Create(LegalNoticeProvider.GetDocuments(), UiText.Get),
            acceptsTab: false,
            enableKeyboardLifecycle: true)
    {
        // The shared 840x620 metrics are the WPF outer (resizable) window; FreeX sizes the Avalonia
        // client area to the measured Windows client (FreeW/FreeP keep the shared size).
        Width = LegalNoticesDialogMetrics.Width - MainWindow.WpfResizableFrameWidth;
        Height = LegalNoticesDialogMetrics.Height - MainWindow.WpfResizableFrameHeight;
        // Registered after the shared renderer's Opened handler, which resets the document margin.
        Opened += (_, _) => ApplyWpfTabPaneVerticalInset();
    }

    private void ApplyWpfTabPaneVerticalInset()
    {
        foreach (var tabItem in this.GetLogicalDescendants().OfType<TabItem>())
        {
            if (tabItem.Content is not TextBox document)
                continue;

            document.Margin = new Thickness(
                document.Margin.Left,
                WpfTabPaneVerticalInset,
                document.Margin.Right,
                WpfTabPaneVerticalInset);
        }
    }
}
