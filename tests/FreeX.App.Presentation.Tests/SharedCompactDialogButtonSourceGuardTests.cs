using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class SharedCompactDialogButtonSourceGuardTests
{
    [Fact]
    public void SharedAvaloniaButtonStylesTheNativeTemplateWithoutReplacingItsInteractionContract()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        var applyStart = source.IndexOf("public static void ApplyButton", StringComparison.Ordinal);
        var applyEnd = source.IndexOf("public static void ApplyTextBox", applyStart, StringComparison.Ordinal);
        applyStart.Should().BeGreaterThanOrEqualTo(0);
        applyEnd.Should().BeGreaterThan(applyStart);
        var applySource = source[applyStart..applyEnd];
        applySource.Should().Contain("button.CornerRadius = style.ButtonCornerRadius");
        applySource.Should().Contain("button.Padding = style.ButtonPadding");
        applySource.Should().Contain("AvaloniaDialogButtonContent.Apply(button, content)");
        applySource.Should().Contain("button.Classes.Add(CompactButtonClass)");
        applySource.Should().NotContain("button.Template =");
        source.Should().NotContain("CreateCompactButtonTemplate");
        source.Should().NotContain("Name = \"CompactButtonBorder\"");
    }
}
