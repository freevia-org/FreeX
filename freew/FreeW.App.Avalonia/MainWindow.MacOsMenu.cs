using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow
{
    private void InstallMacOsMenu()
    {
        if (OperatingSystem.IsMacOS())
            NativeMenu.SetMenu(this, BuildMacOsMenu());
    }

    internal NativeMenu BuildMacOsMenu() => CreateMacOsMenu(
        ExecuteMacOsMenuCommand,
        Close,
        CanExecuteMacOsMenuCommand);

    private void ExecuteMacOsMenuCommand(FreeWKeyboardCommand command)
    {
        // Native key equivalents are dispatched before the Avalonia key route. Keep editing a
        // focused Find/Replace or other text field local to that field, as normal keyboard input is.
        if (FocusManager?.GetFocusedElement() is TextBox textBox)
        {
            switch (command)
            {
                case FreeWKeyboardCommand.Undo: textBox.Undo(); return;
                case FreeWKeyboardCommand.Redo: textBox.Redo(); return;
                case FreeWKeyboardCommand.Cut: textBox.Cut(); return;
                case FreeWKeyboardCommand.Copy: textBox.Copy(); return;
                case FreeWKeyboardCommand.Paste: textBox.Paste(); return;
                case FreeWKeyboardCommand.SelectAll: textBox.SelectAll(); return;
            }
        }

        _applicationCommands.Execute(command);
    }

    private bool CanExecuteMacOsMenuCommand(FreeWKeyboardCommand command)
    {
        if (FocusManager?.GetFocusedElement() is TextBox textBox)
        {
            return command switch
            {
                FreeWKeyboardCommand.Undo => textBox.CanUndo,
                FreeWKeyboardCommand.Redo => textBox.CanRedo,
                FreeWKeyboardCommand.Cut => textBox.CanCut,
                FreeWKeyboardCommand.Copy => textBox.CanCopy,
                FreeWKeyboardCommand.Paste => textBox.CanPaste,
                _ => true,
            };
        }

        return command switch
        {
            FreeWKeyboardCommand.Undo => ResolveHistoryDocumentEditor().CanUndo,
            FreeWKeyboardCommand.Redo => ResolveHistoryDocumentEditor().CanRedo,
            _ => true,
        };
    }

    internal static NativeMenu CreateMacOsMenu(
        Action<FreeWKeyboardCommand> execute,
        Action close,
        Func<FreeWKeyboardCommand, bool> canExecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(canExecute);
        var commands = new List<MacOsMenuCommand>();

        NativeMenuItem Item(string labelKey, FreeWKeyboardCommand command, Key key,
            KeyModifiers modifiers = KeyModifiers.Meta) => ActionItem(
                labelKey, () => execute(command), () => canExecute(command), key, modifiers);

        NativeMenuItem ActionItem(string labelKey, Action action, Func<bool> enabled,
            Key key, KeyModifiers modifiers = KeyModifiers.Meta)
        {
            var command = new MacOsMenuCommand(action, enabled);
            commands.Add(command);
            return new NativeMenuItem
            {
                Header = UiText.Get(labelKey),
                Gesture = new KeyGesture(key, modifiers),
                Command = command,
            };
        }

        var file = new NativeMenu
        {
            Items =
            {
                Item("NativeMenu_New", FreeWKeyboardCommand.NewDocument, Key.N),
                Item("NativeMenu_Open", FreeWKeyboardCommand.OpenDocument, Key.O),
                new NativeMenuItemSeparator(),
                Item("NativeMenu_Save", FreeWKeyboardCommand.SaveDocument, Key.S),
                Item("NativeMenu_SaveAs", FreeWKeyboardCommand.SaveDocumentAs, Key.S,
                    KeyModifiers.Meta | KeyModifiers.Shift),
                new NativeMenuItemSeparator(),
                Item("NativeMenu_Print", FreeWKeyboardCommand.PrintDocument, Key.P),
                ActionItem("NativeMenu_Close", close, () => true, Key.W),
            },
        };
        var edit = new NativeMenu
        {
            Items =
            {
                Item("NativeMenu_Undo", FreeWKeyboardCommand.Undo, Key.Z),
                Item("NativeMenu_Redo", FreeWKeyboardCommand.Redo, Key.Z,
                    KeyModifiers.Meta | KeyModifiers.Shift),
                new NativeMenuItemSeparator(),
                Item("NativeMenu_Cut", FreeWKeyboardCommand.Cut, Key.X),
                Item("NativeMenu_Copy", FreeWKeyboardCommand.Copy, Key.C),
                Item("NativeMenu_Paste", FreeWKeyboardCommand.Paste, Key.V),
                Item("NativeMenu_SelectAll", FreeWKeyboardCommand.SelectAll, Key.A),
                new NativeMenuItemSeparator(),
                Item("NativeMenu_Find", FreeWKeyboardCommand.Find, Key.F),
                // Command+H belongs to the application menu's Hide command on macOS.
                Item("NativeMenu_Replace", FreeWKeyboardCommand.Replace, Key.H, KeyModifiers.Control),
            },
        };
        var menu = new NativeMenu
        {
            Items =
            {
                new NativeMenuItem { Header = UiText.Get("NativeMenu_File"), Menu = file },
                new NativeMenuItem { Header = UiText.Get("NativeMenu_Edit"), Menu = edit },
            },
        };

        void RefreshCommands(object? sender, EventArgs args)
        {
            foreach (var command in commands)
                command.Refresh();
        }

        menu.NeedsUpdate += RefreshCommands;
        file.NeedsUpdate += RefreshCommands;
        edit.NeedsUpdate += RefreshCommands;
        return menu;
    }

    private sealed class MacOsMenuCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
                execute();
        }

        internal void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
