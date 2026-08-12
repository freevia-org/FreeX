using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool IsTextBoxInlineEditorActiveForTest => IsTextBoxInlineEditorActive;

    internal TextBox? TextBoxInlineEditorForTest => _textBoxInlineEditor;

    internal void BeginTextBoxInlineEditForTest(Guid textBoxId) => BeginTextBoxInlineEdit(textBoxId);

    internal void RaiseTextBoxInlineEditorKeyDownForTest(KeyEventArgs args)
    {
        if (_textBoxInlineEditor is null)
            throw new InvalidOperationException("No text box inline editor exists.");

        TextBoxInlineEditor_KeyDown(_textBoxInlineEditor, args);
    }

    internal void InsertTextBoxAtActiveCellForTest() => InsertTextBoxAtActiveCell();

    internal void RefreshShellForViewportPanForTest() => RefreshShellForViewportPan("Ready");

}
