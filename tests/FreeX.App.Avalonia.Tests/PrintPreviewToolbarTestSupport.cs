using System.Linq;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF's Print Preview shows a single icon row by default; page navigation, the page box, the zoom
/// combo, Margins and Page Setup sit behind the overflow chevron. Avalonia matches that, so those
/// controls are not templated (and so are absent from the visual tree) until the chevron is toggled.
/// Tests that drive them expand the row first, exactly as a user would.
/// </summary>
internal static class PrintPreviewToolbarTestSupport
{
    public const string OverflowToggleAutomationId = "PrintPreviewToolbarOverflowButton";

    public static ToggleButton FindOverflowToggle(Control dialog) =>
        dialog.GetVisualDescendants().OfType<ToggleButton>()
            .Single(toggle => AutomationProperties.GetAutomationId(toggle) == OverflowToggleAutomationId);

    public static async Task ExpandPageRowAsync(Control dialog)
    {
        var toggle = FindOverflowToggle(dialog);
        toggle.IsChecked = true;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        toggle.IsChecked.Should().BeTrue();
    }
}
