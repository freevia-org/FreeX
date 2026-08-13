using System.Runtime.InteropServices;

using Avalonia.Controls;
using Avalonia.Input;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.App.Presentation.Editing;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal static bool TryResolveApplicationShortcutForTest(
        Key key,
        KeyModifiers modifiers,
        out KeyboardCommandShortcut shortcut) =>
        TryResolveApplicationShortcut(key, modifiers, out shortcut);

    internal bool FormulaBarExpandedForTest => _formulaBarExpanded;

    internal ExcelSelectionMode KeyboardSelectionModeForTest => _keyboardSelectionMode;

    internal static Key GetEffectiveWorkbookShortcutKeyForTest(
        Key key,
        PhysicalKey physicalKey) =>
        NormalizeWorkbookShortcutKey(key, physicalKey);

}
