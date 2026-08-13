using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Services;

public static class AppOptionsEnterDirectionMapper
{
    public static FormulaEditorEnterDirection ToFormulaEditor(AppOptionsEnterDirection direction) =>
        direction switch
        {
            AppOptionsEnterDirection.Right => FormulaEditorEnterDirection.Right,
            AppOptionsEnterDirection.Up => FormulaEditorEnterDirection.Up,
            AppOptionsEnterDirection.Left => FormulaEditorEnterDirection.Left,
            _ => FormulaEditorEnterDirection.Down
        };
}
