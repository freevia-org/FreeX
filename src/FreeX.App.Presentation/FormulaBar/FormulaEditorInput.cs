namespace FreeX.App.Presentation.FormulaBar;

public enum FormulaEditorKey
{
    None,
    System,
    Enter,
    Tab,
    Escape,
    F2,
    F4,
    F8,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown
}

public enum FormulaEditorEnterDirection
{
    Down,
    Right,
    Up,
    Left
}

[Flags]
public enum FormulaEditorModifiers
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Meta = 1 << 3
}
