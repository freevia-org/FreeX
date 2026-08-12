using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.Shell;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal void RaiseSheetTabModifierClickForTest(SheetId sheetId, KeyModifiers modifiers)
    {
        BeginSheetTabPointer(sheetId, modifiers);
        CompleteSheetTabClick(sheetId);
    }

    internal void RaiseSheetTabModifierReleaseThenKeyboardClickForTest(SheetId sheetId, KeyModifiers modifiers)
    {
        BeginSheetTabPointer(sheetId, modifiers);
        CompleteSheetTabPointerRelease();
        CompleteSheetTabClick(sheetId);
    }

}
