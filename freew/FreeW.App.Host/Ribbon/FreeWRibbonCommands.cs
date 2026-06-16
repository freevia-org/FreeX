using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host;

/// <summary>
/// Binds FreeW's ribbon command ids (declared in <see cref="FreeWRibbon"/>) to behavior over the
/// editing surface, implementing the shared <see cref="IRibbonCommandRegistry"/>. Formatting and
/// clipboard route through WPF's <see cref="EditingCommands"/>/<see cref="ApplicationCommands"/>
/// against the focused RichTextBox (inline edit + undo); bold/italic/underline are stateful so the
/// ribbon can reflect the selection.
/// </summary>
internal static class FreeWRibbonCommands
{
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore)
    {
        var registry = new RibbonCommandRegistry();
        var stateful = new List<(RibbonCommandId Id, IRibbonStatefulCommand Command)>();

        void Routed(string id, RoutedCommand command) =>
            registry.Register(id, new RoutedEditCommand(editor, command));

        void Toggle(string id, RoutedCommand command, DependencyProperty property, Func<object?, bool> isOn)
        {
            var cmd = new ToggleFormatCommand(editor, command, property, isOn);
            registry.Register(id, cmd);
            stateful.Add((id, cmd));
        }

        Toggle("freew.bold", EditingCommands.ToggleBold, TextElement.FontWeightProperty,
            v => v is FontWeight w && w >= FontWeights.Bold);
        Toggle("freew.italic", EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
            v => v is FontStyle s && s == FontStyles.Italic);
        Toggle("freew.underline", EditingCommands.ToggleUnderline, Inline.TextDecorationsProperty,
            v => v is TextDecorationCollection d && d.Count > 0);

        // Live ribbon state: when the caret/selection moves, recompute the toggle states and push
        // them into the shared RibbonStateStore, which the toggle buttons observe.
        editor.SelectionChanged += (_, _) =>
        {
            foreach (var (id, command) in stateful)
                stateStore.SetState(id, command.GetState());
        };

        Routed("freew.grow-font", EditingCommands.IncreaseFontSize);
        Routed("freew.shrink-font", EditingCommands.DecreaseFontSize);
        Routed("freew.align-left", EditingCommands.AlignLeft);
        Routed("freew.align-center", EditingCommands.AlignCenter);
        Routed("freew.align-right", EditingCommands.AlignRight);
        Routed("freew.bullets", EditingCommands.ToggleBullets);
        Routed("freew.numbering", EditingCommands.ToggleNumbering);
        Routed("freew.cut", ApplicationCommands.Cut);
        Routed("freew.copy", ApplicationCommands.Copy);
        Routed("freew.paste", ApplicationCommands.Paste);

        return registry;
    }

    private sealed class RoutedEditCommand(DocumentView editor, RoutedCommand command) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }
    }

    private sealed class ToggleFormatCommand(
        DocumentView editor,
        RoutedCommand command,
        DependencyProperty property,
        Func<object?, bool> isOn) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }

        public RibbonCommandState GetState()
        {
            var value = editor.Selection.GetPropertyValue(property);
            return new RibbonCommandState(IsEnabled: true, IsChecked: value != DependencyProperty.UnsetValue && isOn(value));
        }
    }
}
