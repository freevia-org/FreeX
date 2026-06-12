using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class FormulaRangeEntryPlannerTests
{
    [Fact]
    public void TryApplySelectionText_InsertsGetPivotDataCallAfterFormulaEquals()
    {
        FormulaRangeEntryPlanner.TryApplySelectionText(
                "=",
                caretIndex: 1,
                selectionLength: 0,
                previousReferenceStart: null,
                previousReferenceLength: null,
                "GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")",
                out var edit)
            .Should()
            .BeTrue();

        edit.TextEdit.Text.Should().Be("=GETPIVOTDATA(\"Sum of Amount\",E2,\"Region\",\"West\")");
        edit.ReferenceStart.Should().Be(1);
        edit.ReferenceLength.Should().Be(48);
    }
}
