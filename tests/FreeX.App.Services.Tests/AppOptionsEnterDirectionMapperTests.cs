using FreeX.App.Presentation.FormulaBar;

namespace FreeX.App.Services.Tests;

public sealed class AppOptionsEnterDirectionMapperTests
{
    [Theory]
    [InlineData(AppOptionsEnterDirection.Down, FormulaEditorEnterDirection.Down)]
    [InlineData(AppOptionsEnterDirection.Right, FormulaEditorEnterDirection.Right)]
    [InlineData(AppOptionsEnterDirection.Up, FormulaEditorEnterDirection.Up)]
    [InlineData(AppOptionsEnterDirection.Left, FormulaEditorEnterDirection.Left)]
    public void ToFormulaEditor_MapsEveryOption(
        AppOptionsEnterDirection option,
        FormulaEditorEnterDirection expected)
    {
        Assert.Equal(expected, AppOptionsEnterDirectionMapper.ToFormulaEditor(option));
    }
}
