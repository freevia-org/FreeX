using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Backstage;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using AvaloniaGrid = global::Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool IsBackstageOverlayVisibleForTest => _backstageOverlay.IsOpen;

    internal FreeXBackstagePaneId ActiveBackstagePaneForTest =>
        LiveBackstageFramePlan.Entries.FirstOrDefault(entry =>
            string.Equals(entry.StableId, _backstageOverlay.CurrentEntryId, StringComparison.Ordinal))
        ?.PaneFlow?.Pane ?? LiveBackstageFramePlan.Selection.DefaultPane;

    internal Button? BackstagePaneButtonForTest(FreeXBackstagePaneId pane) =>
        _backstageOverlay.GetEntryButton(FreeXBackstageFramePlanner.GetPaneStableId(pane));

    internal Button? BackstageCommandButtonForTest(FreeXBackstageCommandId command) =>
        _backstageOverlay.GetEntryButton(FreeXBackstageFramePlanner.GetCommandStableId(command));

    internal Action<FreeXBackstageCommandId>? BackstageCommandActivationOverrideForTest { get; set; }

    partial void ResolveBackstageCommandActivationOverride(
        ref Action<FreeXBackstageCommandId>? handler) =>
        handler = BackstageCommandActivationOverrideForTest;

}
