using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal IReadOnlyDictionary<string, Action> BuildHomeBorderRibbonActionsForTest()
    {
        var commands = new Dictionary<string, Action>(StringComparer.Ordinal);
        RegisterHomeBorderRibbonActions(commands);
        return commands;
    }

    internal void ApplySelectedRangeBorderPresetForTest(CellBorderPreset preset) =>
        ApplySelectedRangeBorderPreset(preset);

}
