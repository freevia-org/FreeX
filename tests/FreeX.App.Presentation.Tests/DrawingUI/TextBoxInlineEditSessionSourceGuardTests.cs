using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class TextBoxInlineEditSessionSourceGuardTests
{
    private static readonly string[] RemovedHostState =
    [
        "_textBoxInlineEditingId",
        "_textBoxInlineOriginalText"
    ];

    private static readonly string[] SessionOwnedPlannerCalls =
    [
        "TextBoxInlineEditPlanner.CreateCommitPlan(",
        "TextBoxInlineEditPlanner.PlanKeyDown(",
        "TextBoxInlineEditPlanner.ShouldCommitLostFocus("
    ];

    [Fact]
    public void Session_RemainsRendererNeutral()
    {
        var source = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "DrawingUI",
            "TextBoxInlineEditSession.cs");

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    [Theory]
    [InlineData("FreeX.App.Host")]
    [InlineData("FreeX.App.Avalonia")]
    public void Hosts_DelegateTextBoxEditStateAndTransitionsToPortableSession(string projectName)
    {
        var hostRoot = RepositoryFileLocator.FindDirectory("src", projectName);
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(hostRoot, "MainWindow*.cs")
                .Select(File.ReadAllText));

        source.Should().Contain("TextBoxInlineEditSession _textBoxInlineEditSession = new();");
        foreach (var removedState in RemovedHostState)
            source.Should().NotContain(removedState);
        foreach (var plannerCall in SessionOwnedPlannerCalls)
            source.Should().NotContain(plannerCall);
        source.Should().NotContain("new SetTextBoxTextCommand(");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { RepositoryFileLocator.FindDirectory(parts[0]) }
                .Concat(parts[1..])
                .ToArray()));
}
