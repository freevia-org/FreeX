using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private void RegisterHomeBorderRibbonActions(IDictionary<string, Action> commands)
    {
        commands["Black"] = () => _borderPickerSession.SetColor(CellColor.Black);
        commands["Gray"] = () => _borderPickerSession.SetColor(new CellColor(128, 128, 128));
        commands["Accent 1"] = () =>
            _borderPickerSession.SetColor(_session.Workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent1));
        commands["Accent 2"] = () =>
            _borderPickerSession.SetColor(_session.Workbook.Theme.GetColor(WorkbookThemeColorSlot.Accent2));

        commands["Thin"] = () => _borderPickerSession.SetStyle(BorderStyle.Thin);
        commands["Medium"] = () => _borderPickerSession.SetStyle(BorderStyle.Medium);
        commands["Thick"] = () => _borderPickerSession.SetStyle(BorderStyle.Thick);
        commands["Dashed"] = () => _borderPickerSession.SetStyle(BorderStyle.Dashed);
        commands["Dotted"] = () => _borderPickerSession.SetStyle(BorderStyle.Dotted);
        commands["Double"] = () => _borderPickerSession.SetStyle(BorderStyle.Double);
    }
}
