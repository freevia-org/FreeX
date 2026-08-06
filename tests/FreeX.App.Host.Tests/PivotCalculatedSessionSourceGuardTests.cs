using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotCalculatedSessionSourceGuardTests
{
    [Fact]
    public void WpfCalculatedDialogs_KeepPortableWorkflowInPresentationSessions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotCalculatedDialogs.cs");

        source.Should().Contain("PivotCalculatedFieldSession.CreateDraft(");
        source.Should().Contain("PivotCalculatedItemSession.CreateDraft(");
        source.Should().Contain("_session.PlanSave(");
        source.Should().Contain("_session.SelectSourceField(");
        source.Should().Contain("_session.InsertReference(");

        source.Should().NotContain("private bool ValidateInputs()");
        source.Should().NotContain("CreateFieldNames(");
        source.Should().NotContain("CreateFieldOptions(");
        source.Should().NotContain("CreateItemOptions(");
        source.Should().NotContain("PivotFormulaInsertion");
        source.Should().NotContain("string.IsNullOrWhiteSpace(_nameBox.Text)");
    }
}
