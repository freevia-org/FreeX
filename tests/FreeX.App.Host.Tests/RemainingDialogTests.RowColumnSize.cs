using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RemainingDialogTests
{
    [Fact]
    public void RowHeightDialog_TryCreateResult_RejectsNegativeHeights()
    {
        RowHeightDialog.TryCreateResult("-1", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a row height from 0 to 409.5.");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("409", 409)]
    [InlineData("409.5", 409.5)]
    public void RowHeightDialog_TryCreateResult_AcceptsExcelRowHeightBounds(string input, double expected)
    {
        RowHeightDialog.TryCreateResult(input, out var result, out var error).Should().BeTrue(error);

        result.Should().Be(new RowHeightDialogResult(expected));
    }

    [Fact]
    public void RowHeightDialog_TryCreateResult_RejectsOversizedExcelRowHeight()
    {
        RowHeightDialog.TryCreateResult("409.6", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a row height from 0 to 409.5.");
    }

    [Fact]
    public void RowHeightDialogOpenedFromKeyboard_FocusesHeightBox()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class RowHeightDialog", "public sealed record ColumnWidthDialogResult");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_heightBox);");
    }

    [Fact]
    public void RowHeightDialog_FieldExposesAutomationMetadata()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class RowHeightDialog", "public sealed record ColumnWidthDialogResult");

        source.Should().Contain("AutomationProperties.SetName(_heightBox, UiText.Get(\"Remaining_RowHeight\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_heightBox, \"RowHeightBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_heightBox, UiText.Get(\"Remaining_EnterARowHeightFrom0To4095\"));");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void RowHeightDialog_TryCreateResult_RejectsNonFiniteHeights(string input)
    {
        RowHeightDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a row height from 0 to 409.5.");
    }

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_AcceptsPositiveWidth()
    {
        ColumnWidthDialog.TryCreateResult("8.5", out var result, out _).Should().BeTrue();

        result.Should().Be(new ColumnWidthDialogResult(8.5));
    }

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_RejectsNegativeWidth()
    {
        ColumnWidthDialog.TryCreateResult("-1", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a column width from 0 to 255.");
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("255", 255)]
    public void ColumnWidthDialog_TryCreateResult_AcceptsExcelColumnWidthBounds(string input, double expected)
    {
        ColumnWidthDialog.TryCreateResult(input, out var result, out var error).Should().BeTrue(error);

        result.Should().Be(new ColumnWidthDialogResult(expected));
    }

    [Fact]
    public void ColumnWidthDialog_TryCreateResult_RejectsOversizedExcelColumnWidth()
    {
        ColumnWidthDialog.TryCreateResult("255.1", out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a column width from 0 to 255.");
    }

    [Fact]
    public void ColumnWidthDialogOpenedFromKeyboard_FocusesWidthBox()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ColumnWidthDialog", "public sealed record __NoNextRemainingDialog");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("DialogFocus.FocusAndSelect(_widthBox);");
    }

    [Fact]
    public void ColumnWidthDialog_FieldExposesAutomationMetadata()
    {
        var source = ReadClassSource("RemainingDialogs.cs", "public sealed class ColumnWidthDialog", "public sealed record __NoNextRemainingDialog");

        source.Should().Contain("AutomationProperties.SetName(_widthBox, UiText.Get(\"Remaining_ColumnWidth\"));");
        source.Should().Contain("AutomationProperties.SetAutomationId(_widthBox, \"ColumnWidthBox\");");
        source.Should().Contain("AutomationProperties.SetHelpText(_widthBox, UiText.Get(\"Remaining_EnterAColumnWidthFrom0To255\"));");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ColumnWidthDialog_TryCreateResult_RejectsNonFiniteWidths(string input)
    {
        ColumnWidthDialog.TryCreateResult(input, out _, out var error).Should().BeFalse();

        error.Should().Be("Enter a column width from 0 to 255.");
    }

    [Fact]
    public void RowAndColumnSizeDialogsInvalidInput_ShowOwnedWarningsAndRefocusInputs()
    {
        var rowSource = ReadClassSource("RemainingDialogs.cs", "public sealed class RowHeightDialog", "public sealed record ColumnWidthDialogResult");
        var columnSource = ReadClassSource("RemainingDialogs.cs", "public sealed class ColumnWidthDialog", "public sealed record SheetNameDialogResult");

        rowSource.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        rowSource.Should().Contain("error ?? UiText.Get(\"Remaining_EnterARowHeightFrom0To409\")");
        rowSource.Should().Contain("FocusInvalidHeightInput();");
        rowSource.Should().Contain("private void FocusInvalidHeightInput()");
        rowSource.Should().Contain("DialogFocus.FocusAndSelect(_heightBox);");

        columnSource.Should().Contain("DialogMessageHelper.ShowWarning(this,");
        columnSource.Should().Contain("error ?? UiText.Get(\"Remaining_EnterAColumnWidthFrom0To255\")");
        columnSource.Should().Contain("FocusInvalidWidthInput();");
        columnSource.Should().Contain("private void FocusInvalidWidthInput()");
        columnSource.Should().Contain("DialogFocus.FocusAndSelect(_widthBox);");
    }
}
