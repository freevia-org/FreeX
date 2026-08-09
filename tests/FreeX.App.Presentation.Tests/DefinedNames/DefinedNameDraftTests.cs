using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNameDraftTests
{
    [Theory]
    [InlineData(RefersToError.Blank, "InsertLoc_RefersToErrorBlank", "Enter a Refers To expression.")]
    [InlineData(RefersToError.NotAFormula, "InsertLoc_RefersToErrorNotAFormula", "Refers To must be a valid formula or reference.")]
    [InlineData(RefersToError.None, "InsertLoc_EnterValidRefersTo", "Enter a valid Refers To expression.")]
    public void DescribeRefersToValidationMessage_MapsErrorToResourceAndFallback(
        RefersToError error,
        string resourceKey,
        string fallbackText)
    {
        LocalizedValidationMessage<RefersToError> message =
            RefersToValidationMessages.Describe(error);

        message.Error.Should().Be(error);
        message.ResourceKey.Should().Be(resourceKey);
        message.FallbackText.Should().Be(fallbackText);
    }

    [Fact]
    public void DescribeRefersToValidationMessage_UnknownErrorUsesFormulaOrReferenceFallback()
    {
        var error = (RefersToError)int.MaxValue;

        LocalizedValidationMessage<RefersToError> message =
            RefersToValidationMessages.Describe(error);

        message.Error.Should().Be(error);
        message.ResourceKey.Should().Be("InsertLoc_EnterValidRefersTo");
        message.FallbackText.Should().Be("Enter a valid Refers To expression.");
    }

    [Fact]
    public void RefersToValidationMessage_ResolvesRendererTextAndFallsBackForBlankResult()
    {
        var message = RefersToValidationMessages.Describe(RefersToError.NotAFormula);

        message.Resolve(key => $"localized:{key}")
            .Should().Be("localized:InsertLoc_RefersToErrorNotAFormula");
        message.Resolve(_ => string.Empty).Should().Be(message.FallbackText);
    }

    [Theory]
    [InlineData("=Sheet1!$A$1")]
    [InlineData("Sheet1!A1:B2")]
    [InlineData("=SUM(A1:A10)")]
    [InlineData("=A1*2")]
    [InlineData("$A$1")]
    [InlineData("=42")]
    public void ValidateRefersTo_AcceptsFormulaExpressions(string refersTo)
    {
        DefinedNameDraft.ValidateRefersTo(refersTo).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateRefersTo_RejectsBlank(string? refersTo)
    {
        DefinedNameDraft.ValidateRefersTo(refersTo).Error.Should().Be(RefersToError.Blank);
    }

    [Theory]
    [InlineData("=A1+")]
    [InlineData("=SUM(")]
    [InlineData("=(1+2")]
    [InlineData("=*3")]
    public void ValidateRefersTo_RejectsUnparseableExpressions(string refersTo)
    {
        DefinedNameDraft.ValidateRefersTo(refersTo).Error.Should().Be(RefersToError.NotAFormula);
    }

    [Fact]
    public void ValidateRefersTo_InstanceMethodUsesDraftValue()
    {
        var draft = new DefinedNameDraft("Sales", DefinedNameScope.Workbook, "=Sheet1!$A$1:$A$10");

        draft.ValidateRefersTo().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Draft_CarriesAllParts()
    {
        var sheet = SheetId.New();
        var scope = DefinedNameScope.ForSheet(sheet, "Budget");
        var draft = new DefinedNameDraft("Total", scope, "=Budget!$B$2", "Year total");

        draft.Name.Should().Be("Total");
        draft.Scope.Should().Be(scope);
        draft.Scope.Sheet.Should().Be(sheet);
        draft.RefersTo.Should().Be("=Budget!$B$2");
        draft.Comment.Should().Be("Year total");
    }
}

public sealed class DefinedNameScopeTests
{
    [Fact]
    public void Workbook_IsGlobal()
    {
        var scope = DefinedNameScope.Workbook;

        scope.IsWorkbook.Should().BeTrue();
        scope.Sheet.Should().BeNull();
        scope.Label.Should().Be(DefinedNameScope.WorkbookLabel);
    }

    [Fact]
    public void ForSheet_CarriesSheetAndLabel()
    {
        var sheet = SheetId.New();
        var scope = DefinedNameScope.ForSheet(sheet, "Sheet1");

        scope.IsWorkbook.Should().BeFalse();
        scope.Sheet.Should().Be(sheet);
        scope.Label.Should().Be("Sheet1");
    }

    [Theory]
    [InlineData("Workbook", true)]
    [InlineData("workbook", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("Sheet1", false)]
    public void IsWorkbookLabel_MatchesCaseInsensitively(string? label, bool expected)
    {
        DefinedNameScope.IsWorkbookLabel(label).Should().Be(expected);
    }
}
