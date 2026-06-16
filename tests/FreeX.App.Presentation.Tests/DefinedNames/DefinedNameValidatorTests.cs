using FreeX.App.Presentation.DefinedNames;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNameValidatorTests
{
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
