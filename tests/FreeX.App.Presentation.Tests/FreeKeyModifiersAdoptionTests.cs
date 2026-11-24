using FluentAssertions;
using Free.Shared.Shell;

namespace FreeX.App.Presentation.Tests;

public sealed class FreeKeyModifiersAdoptionTests
{
    [Fact]
    public void NeutralEnumKeepsTheDocumentedBitValues()
    {
        ((int)FreeKeyModifiers.Control).Should().Be(1);
        ((int)FreeKeyModifiers.Shift).Should().Be(2);
        ((int)FreeKeyModifiers.Alt).Should().Be(4);
        ((int)FreeKeyModifiers.Meta).Should().Be(8);
    }

    [Theory]
    [InlineData("DrawingObjectNudgeModifiers")]
    [InlineData("ExcelWorksheetNavigationModifiers")]
    [InlineData("FormulaEditorModifiers")]
    [InlineData("ShortcutModifierKeys")]
    [InlineData("NativeMenuGestureModifiers")]
    [InlineData("WorkbookShortcutModifiers")]
    public void FreeXPresentationDoesNotRedeclareAProductModifierEnum(string removedEnumName)
    {
        var presentation = Path.GetDirectoryName(
            TestWorkspaceFileLocator.Find("src", "FreeX.App.Presentation", "FreeX.App.Presentation.csproj"))!;

        foreach (var file in Directory.EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            File.ReadAllText(file).Should().NotContain($"enum {removedEnumName}", file);
        }
    }
}
