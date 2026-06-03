using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void SheetNameDialog_CreateResult_TrimsSheetName()
    {
        SheetNameDialog.CreateResult("  Report  ").Should().Be(new SheetNameDialogResult("Report"));
    }

    [Theory]
    [InlineData("", "Sheet name is invalid: it cannot be blank.")]
    [InlineData("   ", "Sheet name is invalid: it cannot be blank.")]
    [InlineData("This sheet name is far too long for Excel", "Sheet name is invalid: it cannot exceed 31 characters.")]
    [InlineData("Bad/Name", "Sheet name is invalid: it cannot contain : \\ / ? * [ or ].")]
    [InlineData("'Report", "Sheet name is invalid: it cannot begin or end with an apostrophe.")]
    [InlineData("Report'", "Sheet name is invalid: it cannot begin or end with an apostrophe.")]
    public void SheetNameDialog_TryCreateResult_RejectsInvalidExcelSheetNames(string input, string expectedError)
    {
        SheetNameDialog.TryCreateResult(input, out _, out var error)
            .Should()
            .BeFalse();

        error.Should().Be(expectedError);
    }

    [Fact]
    public void SheetNameDialog_TryCreateResult_AcceptsTrimmedValidSheetName()
    {
        SheetNameDialog.TryCreateResult("  Report  ", out var result, out var error)
            .Should()
            .BeTrue(error);

        result.Should().Be(new SheetNameDialogResult("Report"));
    }

    [Fact]
    public void SheetNameDialog_AcceptWarnsAndRefocusesInvalidName()
    {
        var source = ReadClassSource("SheetNameDialog.cs", "public sealed class SheetNameDialog", "public sealed record __NoNextSheetNameDialog");

        source.Should().Contain("Content = ObjectSizeDialog.CreateSingleInputContent(UiText.Get(\"SheetName_SheetName2\"), _nameBox, Accept);");
        source.Should().Contain("if (!TryCreateResult(_nameBox.Text, out var result, out var error))");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title);");
        source.Should().Contain("_nameBox.Focus();");
        source.Should().Contain("_nameBox.SelectAll();");
        source.Should().Contain("Keyboard.Focus(_nameBox);");
    }

    [Fact]
    public void SheetNameDialogOpenedFromKeyboard_FocusesNameBox()
    {
        var source = ReadClassSource("SheetNameDialog.cs", "public sealed class SheetNameDialog", "public sealed record __NoNextSheetNameDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_nameBox);");
    }
}
