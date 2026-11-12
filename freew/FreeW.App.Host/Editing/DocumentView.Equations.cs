using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTextAlignment = System.Windows.TextAlignment;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Equation selection and editing for the continuous editor: which equation the selection is on,
/// click/double-click/Enter/Alt+= activation, and the undoable insert and replace edits the equation
/// editor commits through the document command bus.
/// </summary>
public sealed partial class DocumentView
{
    private EquationEditorController? _equationEditor;

    /// <summary>The in-place equation editor attached to this view.</summary>
    internal EquationEditorController EquationEditor => _equationEditor ??= new EquationEditorController(this);

    /// <summary>
    /// The equation the selection is exactly on (the selection covers one equation and nothing else), or
    /// null. A caret merely next to an equation does not select it, so typing beside one stays ordinary
    /// text editing.
    /// </summary>
    public Equation? SelectedEquation() =>
        SelectedEquationContainer() is { Child: FrameworkElement { Tag: Equation equation } } ? equation : null;

    /// <summary>Builds the built-up ("Professional") visual of an equation, as the document renders it.</summary>
    internal static FrameworkElement BuildEquationPreview(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        return BuildEquationVisualContent(EquationVisualPlanner.Build(equation));
    }

    /// <summary>
    /// Inserts an equation at the caret as one undoable edit. A display equation is given a line of its
    /// own in the same edit.
    /// </summary>
    public void InsertEquationAtCaret(Equation equation)
    {
        ArgumentNullException.ThrowIfNull(equation);
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return;

        Focus();
        CommitToModel();
        var before = _model.Blocks.ToList();

        var caret = CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? CaretPosition;
        var paragraph = caret.Paragraph ?? Document.Blocks.OfType<WpfParagraph>().LastOrDefault();
        if (paragraph is null)
        {
            paragraph = new WpfParagraph();
            Document.Blocks.Add(paragraph);
        }

        InsertInlineAt(paragraph, caret, BuildEquationRun(equation));
        CommitToModel();
        if (equation.IsDisplayMath && !IsOnItsOwnLine(equation))
            EquationEditPlanner.TryReplace(_model, equation, equation);

        _commands.Execute(new ReplaceAllBlocksCommand(before, _model.Blocks.ToList(), "Insert Equation"));
        SelectEquation(equation);
    }

    /// <summary>
    /// Replaces an equation that is in the document with another, as one undoable edit. Returns false
    /// when the original is no longer in the document or editing is not allowed.
    /// </summary>
    public bool ReplaceEquation(Equation original, Equation replacement)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.BodyTextEdit))
            return false;

        // This view records undo as whole-document snapshots (every commit rebuilds the model from the
        // FlowDocument, so a command holding on to model paragraphs could not find them again). Commit
        // twice so the "before" snapshot and the edited model are separate object graphs, then apply the
        // shared, tested equation plan to the fresh one.
        CommitToModel();
        if (EquationLocator.Find(_model, original) is null)
            return false;

        var before = _model.Blocks.ToList();
        CommitToModel();
        if (!EquationEditPlanner.TryReplace(_model, original, replacement))
            return false;

        _commands.Execute(new ReplaceAllBlocksCommand(before, _model.Blocks.ToList(), "Edit Equation"));
        SelectEquation(replacement);
        return true;
    }

    /// <summary>Selects the given equation (if it is rendered in this view) and brings it into view.</summary>
    public bool SelectEquation(Equation equation)
    {
        if (FindEquationContainer(equation) is not { } container)
            return false;

        Selection.Select(container.ElementStart, container.ElementEnd);
        container.BringIntoView();
        return true;
    }

    /// <summary>The screen rectangle of an equation rendered in this view, for placing the editor beside it.</summary>
    internal Rect? EquationScreenBounds(Equation equation)
    {
        if (FindEquationContainer(equation)?.Child is not FrameworkElement element
            || !element.IsVisible
            || PresentationSource.FromVisual(element) is null)
        {
            return null;
        }

        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Clicking an equation selects it as an object (showing the Equation tab); a double-click edits it.
        if (EquationContainerAt(e.GetPosition(this)) is { } container)
        {
            Focus();
            Selection.Select(container.ElementStart, container.ElementEnd);
            e.Handled = true;
            if (e.ClickCount == 2)
                EquationEditor.BeginEditSelected();
            return;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    /// <summary>
    /// Alt+= starts a new equation; Enter on a selected equation edits it. Returns true when the key was
    /// one of these and has been handled.
    /// </summary>
    private bool TryHandleEquationShortcut(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.OemPlus && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            EquationEditor.BeginNew();
            return true;
        }

        if (key is Key.Enter or Key.Return && Keyboard.Modifiers == ModifierKeys.None && SelectedEquation() is not null)
        {
            EquationEditor.BeginEditSelected();
            return true;
        }

        return false;
    }

    /// <summary>
    /// The alignment of a paragraph that holds nothing but display equations: the equation's own
    /// justification (m:oMathParaPr/m:jc), centred unless it says left or right.
    /// </summary>
    private static WpfTextAlignment DisplayEquationAlignment(ModelParagraph paragraph) =>
        paragraph.Runs.Select(run => run.Equation).FirstOrDefault(equation => equation is not null)?.Justification switch
        {
            MathJustification.Left => WpfTextAlignment.Left,
            MathJustification.Right => WpfTextAlignment.Right,
            _ => WpfTextAlignment.Center,
        };

    private bool IsOnItsOwnLine(Equation equation) =>
        EquationLocator.Find(_model, equation) is { } location
        && EquationEditPlanner.IsDisplayOnly(location.Paragraph);

    private static bool IsEquationContainer(InlineUIContainer container) =>
        container.Child is FrameworkElement { Tag: Equation };

    private InlineUIContainer? SelectedEquationContainer()
    {
        if (Selection.IsEmpty)
            return null;

        InlineUIContainer? found = null;
        var end = Selection.End;
        for (var pointer = Selection.Start;
             pointer is not null && pointer.CompareTo(end) < 0;
             pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            switch (pointer.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    // Any text inside the selection means it is not just the equation.
                    if (pointer.GetTextRunLength(LogicalDirection.Forward) > 0)
                        return null;
                    break;

                case TextPointerContext.ElementStart when pointer.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer container:
                    if (!IsEquationContainer(container) || (found is not null && !ReferenceEquals(found, container)))
                        return null;
                    found = container;
                    break;
            }
        }

        return found;
    }

    private InlineUIContainer? FindEquationContainer(Equation equation)
    {
        for (var pointer = Document.ContentStart;
             pointer is not null;
             pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
                && pointer.GetAdjacentElement(LogicalDirection.Forward) is InlineUIContainer { Child: FrameworkElement { Tag: Equation candidate } } container
                && ReferenceEquals(candidate, equation))
            {
                return container;
            }
        }

        return null;
    }

    private InlineUIContainer? EquationContainerAt(Point point)
    {
        var position = GetPositionFromPoint(point, snapToText: true);
        if (position is null)
            return null;

        InlineUIContainer?[] candidates =
        [
            position.Parent as InlineUIContainer,
            position.GetAdjacentElement(LogicalDirection.Forward) as InlineUIContainer,
            position.GetAdjacentElement(LogicalDirection.Backward) as InlineUIContainer,
        ];

        foreach (var candidate in candidates)
        {
            if (candidate is null
                || !IsEquationContainer(candidate)
                || candidate.Child is not FrameworkElement element
                || !element.IsVisible
                || !element.IsDescendantOf(this))
            {
                continue;
            }

            // snapToText resolves any point to the nearest position; only a click on the equation counts.
            var bounds = element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
            if (bounds.Contains(point))
                return candidate;
        }

        return null;
    }
}
