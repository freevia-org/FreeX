using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class ChangeCaseDialogPlannerTests
{
    [Fact]
    public void ChoicesMatchTheWpfAuthorityInDisplayOrder()
    {
        ChangeCaseDialogPlanner.Choices.Should().Equal(
            new ChangeCaseDialogChoice("UPPERCASE", CaseKind.Upper),
            new ChangeCaseDialogChoice("lowercase", CaseKind.Lower),
            new ChangeCaseDialogChoice("Sentence case", CaseKind.Sentence),
            new ChangeCaseDialogChoice("Capitalize Each Word", CaseKind.Capitalize),
            new ChangeCaseDialogChoice("tOGGLE cASE", CaseKind.Toggle));
    }

    [Theory]
    [InlineData(CaseKind.Upper, "HELLO WORLD")]
    [InlineData(CaseKind.Lower, "hello world")]
    [InlineData(CaseKind.Sentence, "Hello world")]
    [InlineData(CaseKind.Capitalize, "Hello World")]
    [InlineData(CaseKind.Toggle, "hELLO wORLD")]
    public void ApplyUsesTheCanonicalCoreTransformation(CaseKind kind, string expected)
    {
        ChangeCaseDialogPlanner.Apply("Hello World", kind).Should().Be(expected);
    }

    [Fact]
    public void RenderersConsumeTheSharedChoiceCatalogAndAvaloniaDoesNotCycleCase()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));
        var avaloniaDialog = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "ChangeCaseDialog.cs"));
        var avaloniaRegistry = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWAvaloniaRibbonCommands.cs"));
        var sharedProfile = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Presentation", "Ribbon", "FreeWRibbonHostExecutionProfile.cs"));
        var avaloniaEditor = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs"));

        wpf.Should().Contain("ChangeCaseDialogPlanner.Choices");
        wpf.Should().Contain("ChangeCasePickerWindow : Free.Shared.Ribbon.Wpf.DialogWindow");
        avaloniaDialog.Should().Contain("ChangeCaseDialogPlanner.Choices");
        sharedProfile.Should().Contain("FreeWRibbonCommandAction.ChangeCase, ports.OpenChangeCaseDialog");
        avaloniaRegistry.Should().NotContain("FreeWRibbonCommandAction.ChangeCase");
        avaloniaEditor.Should().Contain("public void ChangeSelectionCase(CaseKind kind)");
        avaloniaEditor.Should().NotContain("private static string CycleCase(string text)");
    }
}
