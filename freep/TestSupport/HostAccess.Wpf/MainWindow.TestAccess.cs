using System.Windows;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal PresentationPrintBackstagePlan? LastFilePrintBackstagePlanForTests =>
        _fileSession.LastPrintBackstagePlan;

    internal void ShowBackstageForTests() => ShowBackstage();

    internal bool ActivateBackstageEntryForTests(string label)
    {
        _backstage.Show(label);
        return _backstage.CurrentPaneContent is not null;
    }

    internal UIElement? CurrentBackstagePaneContentForTests => _backstage.CurrentPaneContent;

    internal bool ApplyBackstagePrintCustomRangeForTests(string rangeText) =>
        _backstage.ApplyCustomPrintRangeForTests(rangeText);
}
