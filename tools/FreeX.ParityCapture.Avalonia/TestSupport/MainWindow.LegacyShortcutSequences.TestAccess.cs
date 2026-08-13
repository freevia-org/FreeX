using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Avalonia.Ribbon;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.KeyTips;
using FreeX.App.Presentation.Ribbon;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal FreeXRibbonLegacyKeyTipSequence LegacyKeyTipSequenceForTest =>
        _ribbonKeyTipSession.LegacySequence;

    internal string RibbonKeyTipInputForTest => _ribbonKeyTipSession.Input;

    internal string QuickAccessKeyTipInputForTest =>
        _ribbonKeyTipSession.Scope == FreeXRibbonKeyTipInputScope.QuickAccess
            ? _ribbonKeyTipSession.Input
            : "";

}
