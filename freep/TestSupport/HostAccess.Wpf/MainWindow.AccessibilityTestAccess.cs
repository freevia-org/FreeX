using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Drawing;
using Free.Shared.Ribbon.Wpf;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using Free.Shared.Theme;
using Free.Shared.Theme.Wpf;
using FreeP.App.Compositor;
using FreeP.App.Host.Backstage;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;
using ModelHyperlink = FreeP.Core.Model.Hyperlink;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal TextBox NotesPaneForAccessibilityTests => _notesBox;

    internal Border CommentsPaneForAccessibilityTests => _commentListHost;

    internal IReadOnlyList<FrameworkElement> CommentsPaneItemsForAccessibilityTests =>
        _commentListPanel is null
            ? Array.Empty<FrameworkElement>()
            : _commentListPanel.Children
                .OfType<FrameworkElement>()
                .Where(item => AutomationProperties.GetAutomationId(item)
                    .StartsWith(
                        PresentationSemanticIdentityCatalog.CommentsPaneItemAutomationIdPrefix,
                        StringComparison.Ordinal))
                .ToArray();

    internal SelectionPane SelectionPaneForAccessibilityTests => _selectionPane;

    internal IReadOnlyList<FrameworkElement> SelectionPaneItemsForAccessibilityTests =>
        _selectionPane?.AccessibilityItemsForTests ?? Array.Empty<FrameworkElement>();

    internal AnimationPane? AnimationPaneForAccessibilityTests => _animPane;

    internal IReadOnlyList<FrameworkElement> AnimationPaneItemsForAccessibilityTests =>
        _animPane?.AccessibilityItemsForTests ?? Array.Empty<FrameworkElement>();

    internal IReadOnlyList<FrameworkElement> SlidePaneItemsForAccessibilityTests =>
        (SlidePaneHost.Child as SlidePane)?.AccessibilityItemsForTests
        ?? Array.Empty<FrameworkElement>();

}
