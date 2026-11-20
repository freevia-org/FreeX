using Avalonia.Controls;
using FreeX.App.Localization;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal Button? ErrorSmartTagButtonForTest => _errorSmartTagButton;

    internal Button? PasteOptionsButtonForTest => _pasteOptionsButton;

    internal Button? InsertOptionsButtonForTest => _insertOptionsButton;

    internal void RefreshShellForTest() => RefreshShell(UiText.Get("MainLoc_Ready"));
}
