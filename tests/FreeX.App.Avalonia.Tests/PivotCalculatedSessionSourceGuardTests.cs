using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class PivotCalculatedSessionSourceGuardTests
{
    [Fact]
    public void AvaloniaCalculatedDialogs_KeepPortableWorkflowInPresentationSessions()
    {
        var fieldSource = ReadAppSource("MainWindow.PivotCalculatedField.cs");
        fieldSource.Should().Contain("PivotCalculatedFieldSession.Create(");
        fieldSource.Should().Contain("calculatedFieldSession.SelectExisting(");
        fieldSource.Should().Contain("calculatedFieldSession.PlanSave(");
        fieldSource.Should().Contain("calculatedFieldSession.PlanDelete(");
        fieldSource.Should().Contain("calculatedFieldSession.Commit(");
        fieldSource.Should().NotContain("PivotCalculatedFieldPlanner.TryCreateResult(");
        fieldSource.Should().NotContain("PivotCalculatedFieldPlanner.TryRemove(");
        fieldSource.Should().NotContain("PivotCalculatedFieldPlanner.Upsert(");
        fieldSource.Should().NotContain("PivotCalcFieldOutcome");

        var itemSource = ReadAppSource("MainWindow.PivotCalculatedItem.cs");
        itemSource.Should().Contain("PivotCalculatedItemSession.Create(");
        itemSource.Should().Contain("calculatedItemSession.SelectSourceField(");
        itemSource.Should().Contain("calculatedItemSession.SelectExisting(");
        itemSource.Should().Contain("calculatedItemSession.PlanSave(");
        itemSource.Should().Contain("calculatedItemSession.PlanDelete(");
        itemSource.Should().Contain("calculatedItemSession.Commit(");
        itemSource.Should().NotContain("PivotCalculatedItemPlanner.TryCreateResult(");
        itemSource.Should().NotContain("PivotCalculatedItemPlanner.TryRemove(");
        itemSource.Should().NotContain("PivotCalculatedItemPlanner.Upsert(");
        itemSource.Should().NotContain("PivotCalcItemOutcome");
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", fileName));

    private static string RepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "FreeX.slnx")))
            current = current.Parent;

        current.Should().NotBeNull();
        return Path.Combine([current!.FullName, .. parts]);
    }
}
