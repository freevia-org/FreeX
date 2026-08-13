using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private CellColor _borderPickerColor = CellColor.Black;
    private BorderStyle _borderPickerStyle = BorderStyle.Thin;

    private void RegisterHomeBorderRibbonActions(IDictionary<string, Action> commands)
    {
        commands["Black"] = () => _borderPickerColor = CellColor.Black;
        commands["Gray"] = () => _borderPickerColor = new CellColor(128, 128, 128);
        commands["Accent 1"] = () =>
            _borderPickerColor = _session.Workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1);
        commands["Accent 2"] = () =>
            _borderPickerColor = _session.Workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent2);

        commands["Thin"] = () => _borderPickerStyle = BorderStyle.Thin;
        commands["Medium"] = () => _borderPickerStyle = BorderStyle.Medium;
        commands["Thick"] = () => _borderPickerStyle = BorderStyle.Thick;
        commands["Dashed"] = () => _borderPickerStyle = BorderStyle.Dashed;
        commands["Dotted"] = () => _borderPickerStyle = BorderStyle.Dotted;
        commands["Double"] = () => _borderPickerStyle = BorderStyle.Double;
    }
}
