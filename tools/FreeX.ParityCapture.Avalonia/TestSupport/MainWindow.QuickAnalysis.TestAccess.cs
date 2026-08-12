using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private Action<ConditionalFormatRuleDialogInspection>? _interactionValidationConditionalFormatRuleProbe;

    /// <summary>
    /// R128B test hook: drives the real Quick Analysis "open conditional-format dialog" apply path
    /// (<see cref="ApplyQuickAnalysisItemAsync"/> -&gt; <see cref="ShowQuickAnalysisConditionalFormatDialogAsync"/>)
    /// exactly like production, auto-accepting the rule editor with the given preset -- but, unlike
    /// RunQuickAnalysisDrawingInteractionValidationForTestAsync, it does NOT reset the current
    /// selection first, so a multi-area selection the caller set (e.g. via
    /// WorkbookSession.SelectRanges) survives into the apply step.
    /// </summary>
    internal async Task ApplyQuickAnalysisConditionalFormatItemForTestAsync(string itemId, ConditionalFormatPreset preset)
    {
        var previousProbe = _interactionValidationConditionalFormatRuleProbe;
        _interactionValidationConditionalFormatRuleProbe = probe =>
        {
            var presetIndex = ConditionalFormatPresetChoices.ToList().FindIndex(choice => choice.Preset == preset);
            if (presetIndex >= 0)
                probe.PresetBox.SelectedIndex = presetIndex;
            probe.OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, probe.OkButton));
        };
        try
        {
            var sheet = _session.ActiveSheet;
            var range = _session.SelectedRange;
            var item = _quickAnalysisSession.FindOpenItem(
                sheet,
                range,
                QuickAnalysisShellCapabilities.DialogBacked,
                itemId) ?? throw new InvalidOperationException($"Quick Analysis item '{itemId}' is unavailable.");
            await ApplyQuickAnalysisItemAsync(item);
        }
        finally
        {
            _interactionValidationConditionalFormatRuleProbe = previousProbe;
        }
    }

    partial void ResolveQuickAnalysisConditionalFormatInspection(
        ref Action<ConditionalFormatRuleDialogInspection>? inspection) =>
        inspection = _interactionValidationConditionalFormatRuleProbe;

}
