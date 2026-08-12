using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
#if FREEP_WINDOWS_CAPTURE
using Free.Shared.AppServices.Windows;
#endif
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Avalonia.Backstage;
using FreeP.App.Avalonia.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Linq;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal TextBox NotesPaneForAccessibilityTests => _notesBox;

    internal ListBox SlidePaneForAccessibilityTests => _slidePaneList;

    internal Border CommentsPaneForAccessibilityTests => _reviewCommentsPaneHost;

    internal IReadOnlyList<Control> CommentsPaneItemsForAccessibilityTests =>
        _reviewCommentsPanePanel is null
            ? Array.Empty<Control>()
            : _reviewCommentsPanePanel.Children
                .OfType<Control>()
                .Where(item => AutomationProperties.GetAutomationId(item)
                    ?.StartsWith(
                        PresentationSemanticIdentityCatalog.CommentsPaneItemAutomationIdPrefix,
                        StringComparison.Ordinal) == true)
                .ToArray();

    internal SelectionPane SelectionPaneForAccessibilityTests => _selectionPane;

    internal Border AnimationPaneForAccessibilityTests => _animationPaneHost;

    internal IReadOnlyList<Control> SelectionPaneItemsForAccessibilityTests =>
        _selectionPane?.AccessibilityItemsForTests ?? Array.Empty<Control>();

    internal IReadOnlyList<Control> AnimationPaneItemsForAccessibilityTests =>
        _animationPaneItemsPanel?.Children.OfType<Control>().ToArray() ?? Array.Empty<Control>();

    internal IReadOnlyList<Control> SlidePaneItemsForAccessibilityTests =>
        _slidePaneList is null
            ? Array.Empty<Control>()
            : _slidePaneList.Items
                .OfType<ListBoxItem>()
                .Where(item => AutomationProperties.GetAutomationId(item)
                    ?.StartsWith("FreePSlidePaneItem", StringComparison.Ordinal) == true)
                .Cast<Control>()
                .ToArray();

}
