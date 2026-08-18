using FluentAssertions;
using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DefinedNames;

/// <summary>
/// Regression coverage for R144-io-defined-names-83-2 (finding F2): the shared, portable
/// <see cref="DefinedNameValidator"/> that drives BOTH shells' live Name Manager/Define Name
/// dialogs (WPF's NamedRangeDialog.xaml.cs/NameDefinitionDialog.cs and Avalonia's
/// MainWindow.DefinedNames.cs, both through <see cref="DefinedNamesSession"/>) never rejected a
/// name starting with the "_xlnm."/"_xlchart." reserved prefix, even though
/// <see cref="Workbook.ValidateNamedRangeName"/> -- the gate the actual
/// DefineNamedRangeCommand/DefineNamedFormulaCommand consult on Save/OK -- has always rejected it.
/// That mismatch let a user type "_xlnm.Foo" into the dialog, see it accepted as the user typed
/// (live validation reported no error and enabled OK/Save), and then get an unexplained rejection
/// only when the command actually ran.
/// </summary>
public sealed class R144_DefinedNameValidatorReservedPrefixTests
{
    [Theory]
    [InlineData("_xlnm.Foo")]
    [InlineData("_XLNM.Foo")] // case-insensitive, matches Workbook.HasReservedExcelPrefix
    [InlineData("_xlnm.Print_Area")] // even the exact built-in text, as a user-typed candidate
    [InlineData("_xlchart.Bar")]
    [InlineData("_XLCHART.Bar")]
    public void Validate_RejectsReservedPrefix_MatchingWorkbookValidateNamedRangeName(string name)
    {
        var liveResult = DefinedNameValidator.Validate(name);
        liveResult.IsValid.Should().BeFalse(
            "the live dialog validator must reject a reserved-prefix name exactly like the " +
            "command layer does, instead of accepting it and failing later on Save");
        liveResult.Error.Should().Be(DefinedNameError.ReservedPrefix);

        // Cross-check against the real command-layer gate so the two never drift apart again.
        var workbook = new Workbook();
        workbook.ValidateNamedRangeName(name).Should().NotBeNull(
            "premise: the command layer must also reject this name");
    }

    [Theory]
    [InlineData("My_xlnm.Foo")] // does not START with the prefix
    [InlineData("_FilterDatabase")] // bare reserved word, unrelated to the "_xlnm." prefix rule
    [InlineData("Print_Area")] // ditto
    [InlineData("_Foo")]
    [InlineData("Revenue")]
    public void Validate_NonPrefixedNames_StillAccepted(string name)
    {
        // Sibling/no-regression coverage: only a genuine leading "_xlnm."/"_xlchart." match is
        // rejected by the new check -- everything else the validator already accepted keeps working.
        var liveResult = DefinedNameValidator.Validate(name);
        liveResult.IsValid.Should().BeTrue(liveResult.Error.ToString());

        var workbook = new Workbook();
        workbook.ValidateNamedRangeName(name).Should().BeNull(
            "premise: the command layer must agree this name is legal");
    }

    [Fact]
    public void DescribeValidationMessage_ReservedPrefixHasANonGenericMessage()
    {
        var message = DefinedNameValidationMessages.Describe(DefinedNameError.ReservedPrefix);

        message.Text.ResourceKey.Should().Be("InsertLoc_NameErrorReserved");
        message.Text.FallbackText.Should().Be("That name is reserved.");
    }
}
