using System.Windows;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Owns the in-place equation editor of one <see cref="DocumentView"/>: opens it for a new or the
/// selected equation, routes ribbon symbol/structure insertions to its caret, and commits OK through the
/// view's undoable insert/replace edits. At most one editor is open at a time.
/// </summary>
internal sealed class EquationEditorController(DocumentView view)
{
    private EquationEditorWindow? _window;

    /// <summary>Raised when the editor opens or closes, so the shell can show or hide the Equation tab.</summary>
    public event EventHandler? StateChanged;

    public bool IsOpen => _window is not null;

    /// <summary>The open editor window (for tests and focus routing), or null.</summary>
    internal EquationEditorWindow? Window => _window;

    /// <summary>Opens the editor for a new equation at the caret.</summary>
    public void BeginNew() => Open(EquationEditorSession.ForNew());

    /// <summary>Opens the editor on the selected equation. Returns false when no equation is selected.</summary>
    public bool BeginEditSelected()
    {
        if (view.SelectedEquation() is not { } equation)
            return false;

        Open(EquationEditorSession.ForExisting(equation));
        return true;
    }

    /// <summary>
    /// Inserts linear-format text (a symbol or a structure template) at the editor's caret, opening the
    /// editor first — on the selected equation, or for a new one — when it is not open.
    /// </summary>
    public void InsertText(string text)
    {
        if (_window is null && !BeginEditSelected())
            BeginNew();

        _window?.InsertText(text);
    }

    /// <summary>Closes the editor without committing.</summary>
    public void Cancel() => _window?.Close();

    private void Open(EquationEditorSession session)
    {
        _window?.Close();

        var window = new EquationEditorWindow(session, UiText.Get)
        {
            Owner = System.Windows.Window.GetWindow(view),
        };
        PositionBeside(window, session.Original);
        window.Committed += OnCommitted;
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_window, window))
                return;
            _window = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        };

        _window = window;
        window.Show();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCommitted(EquationEditorCommit commit)
    {
        if (commit.Original is null)
            view.InsertEquationAtCaret(commit.Equation);
        else
            view.ReplaceEquation(commit.Original, commit.Equation);
    }

    /// <summary>Places the editor just below the equation being edited, or centred on the owner.</summary>
    private void PositionBeside(EquationEditorWindow window, Equation? original)
    {
        if (original is null || view.EquationScreenBounds(original) is not { } bounds)
        {
            window.WindowStartupLocation = window.Owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner;
            return;
        }

        // Screen pixels to device-independent units for Window.Left/Top.
        var source = PresentationSource.FromVisual(view);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var anchor = fromDevice.Transform(new Point(bounds.Left, bounds.Bottom));
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = Math.Max(SystemParameters.VirtualScreenLeft, anchor.X);
        window.Top = Math.Min(
            anchor.Y + 6,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 260);
    }
}
