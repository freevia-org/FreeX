using FreeX.App.Presentation.DefinedNames;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNameValidatorTests
{
    [Theory]
    [InlineData(DefinedNameError.Blank, "NamedRange_NameRequiredMessage", "Please enter a name.")]
    [InlineData(DefinedNameError.TooLong, "InsertLoc_NameErrorTooLong", "The name is too long (255 characters maximum).")]
    [InlineData(DefinedNameError.InvalidFirstCharacter, "InsertLoc_NameErrorInvalidFirstChar", "A name must start with a letter, underscore, or backslash.")]
    [InlineData(DefinedNameError.InvalidCharacter, "InsertLoc_NameErrorInvalidChar", "A name may contain only letters, digits, periods, and underscores (no spaces).")]
    [InlineData(DefinedNameError.LooksLikeReference, "InsertLoc_NameErrorLooksLikeReference", "A name cannot look like a cell reference.")]
    [InlineData(DefinedNameError.Reserved, "InsertLoc_NameErrorReserved", "That name is reserved.")]
    [InlineData(DefinedNameError.Duplicate, "InsertLoc_NameErrorDuplicate", "A name with that text already exists in this scope.")]
    [InlineData(DefinedNameError.None, "InsertLoc_NameErrorGeneric", "Enter a valid name.")]
    public void DescribeValidationMessage_MapsErrorToResourceAndFallback(
        DefinedNameError error,
        string resourceKey,
        string fallbackText)
    {
        LocalizedValidationMessage<DefinedNameError> message =
            DefinedNameValidationMessages.Describe(error);

        message.Error.Should().Be(error);
        message.ResourceKey.Should().Be(resourceKey);
        message.FallbackText.Should().Be(fallbackText);
    }

    [Fact]
    public void DescribeValidationMessage_UnknownErrorUsesGenericDescriptor()
    {
        var error = (DefinedNameError)int.MaxValue;

        LocalizedValidationMessage<DefinedNameError> message =
            DefinedNameValidationMessages.Describe(error);

        message.Error.Should().Be(error);
        message.ResourceKey.Should().Be("InsertLoc_NameErrorGeneric");
        message.FallbackText.Should().Be("Enter a valid name.");
    }

    [Fact]
    public void ValidationMessage_ResolvesRendererTextAndFallsBackForBlankResult()
    {
        var message = DefinedNameValidationMessages.Describe(DefinedNameError.Duplicate);

        message.Resolve(key => $"localized:{key}")
            .Should().Be("localized:InsertLoc_NameErrorDuplicate");
        message.Resolve(_ => string.Empty).Should().Be(message.FallbackText);
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("_hidden")]
    [InlineData("Tax.Rate")]
    [InlineData("Region_1")]
    [InlineData("a")]
    [InlineData("Q1Total")]
    [InlineData("\\macro")]
    public void Validate_AcceptsLegalNames(string name)
    {
        var result = DefinedNameValidator.Validate(name);

        result.IsValid.Should().BeTrue();
        result.Error.Should().Be(DefinedNameError.None);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_RejectsBlank(string? name)
    {
        DefinedNameValidator.Validate(name).Error.Should().Be(DefinedNameError.Blank);
    }

    [Fact]
    public void Validate_RejectsTooLong()
    {
        var name = new string('a', 256);

        DefinedNameValidator.Validate(name).Error.Should().Be(DefinedNameError.TooLong);
    }

    [Fact]
    public void Validate_AcceptsExactlyMaxLength()
    {
        var name = new string('a', 255);

        DefinedNameValidator.Validate(name).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1Name")]
    [InlineData(".dotted")]
    [InlineData("9")]
    [InlineData("-dash")]
    public void Validate_RejectsBadFirstCharacter(string name)
    {
        DefinedNameValidator.Validate(name).Error.Should().Be(DefinedNameError.InvalidFirstCharacter);
    }

    [Theory]
    [InlineData("Has Space")]
    [InlineData("trailing ")]
    [InlineData("a-b")]
    [InlineData("a+b")]
    [InlineData("name!")]
    public void Validate_RejectsInvalidBodyCharacter(string name)
    {
        DefinedNameValidator.Validate(name).Error.Should().Be(DefinedNameError.InvalidCharacter);
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("B7")]
    [InlineData("XFD1048576")]
    [InlineData("R1C1")]
    [InlineData("R12C34")]
    public void Validate_RejectsCellReferenceLikeNames(string name)
    {
        DefinedNameValidator.Validate(name).Error.Should().Be(DefinedNameError.LooksLikeReference);
    }

    [Theory]
    [InlineData("R")]
    [InlineData("r")]
    [InlineData("C")]
    [InlineData("c")]
    public void Validate_RejectsReservedSingleLetters(string name)
    {
        DefinedNameValidator.Validate(name).Error.Should().Be(DefinedNameError.Reserved);
    }

    [Fact]
    public void Validate_AllowsLettersThatAreNotReservedMacros()
    {
        DefinedNameValidator.Validate("D").IsValid.Should().BeTrue();
        DefinedNameValidator.Validate("x").IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_DetectsDuplicateCaseInsensitively()
    {
        var existing = new[] { "Sales", "Region" };

        DefinedNameValidator.Validate("sales", existing).Error
            .Should().Be(DefinedNameError.Duplicate);
    }

    [Fact]
    public void Validate_UniqueWithinScopeIsAccepted()
    {
        var existing = new[] { "Sales", "Region" };

        DefinedNameValidator.Validate("Profit", existing).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ExcludesOriginalNameFromDuplicateCheck()
    {
        var existing = new[] { "Sales", "Region" };

        // Editing "Sales" in place: same text must not collide with itself.
        DefinedNameValidator.Validate("Sales", existing, originalName: "Sales").IsValid
            .Should().BeTrue();
    }

    [Fact]
    public void Validate_RenamingOntoAnotherExistingNameIsRejected()
    {
        var existing = new[] { "Sales", "Region" };

        DefinedNameValidator.Validate("Region", existing, originalName: "Sales").Error
            .Should().Be(DefinedNameError.Duplicate);
    }

    [Fact]
    public void Validate_StructuralErrorTakesPrecedenceOverDuplicate()
    {
        var existing = new[] { "A1" };

        DefinedNameValidator.Validate("A1", existing).Error
            .Should().Be(DefinedNameError.LooksLikeReference);
    }
}
