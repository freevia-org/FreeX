using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class SharedCompactDialogButtonSourceGuardTests
{
    [Fact]
    public void SharedAvaloniaButtonOwnsWpfShapedTemplateWithoutFluentPartDependencies()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.Shell.Avalonia",
            "AvaloniaCompactDialogChrome.cs"));

        var templateStart = source.IndexOf("private static FuncControlTemplate<Button> CreateCompactButtonTemplate", StringComparison.Ordinal);
        var templateEnd = source.IndexOf("public static void ApplyTextBox", templateStart, StringComparison.Ordinal);
        templateStart.Should().BeGreaterThanOrEqualTo(0);
        templateEnd.Should().BeGreaterThan(templateStart);
        var templateSource = source[templateStart..templateEnd];
        source.Should().Contain("button.Template = CreateCompactButtonTemplate(style)");
        templateSource.Should().Contain("Name = \"CompactButtonBorder\"");
        templateSource.Should().Contain("CornerRadius = style.ButtonCornerRadius");
        templateSource.Should().Contain("nameof(TemplatedControl.Background)");
        templateSource.Should().Contain("nameof(TemplatedControl.BorderBrush)");
        templateSource.Should().Contain("nameof(TemplatedControl.BorderThickness)");
        templateSource.Should().Contain("nameof(TemplatedControl.Padding)");
        templateSource.Should().Contain("RecognizesAccessKey = true");
        templateSource.Should().NotContain("PART_ButtonChrome");
        templateSource.Should().NotContain("PART_ContentPresenter");
    }
}
