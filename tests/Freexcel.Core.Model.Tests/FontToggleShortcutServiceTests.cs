using Freexcel.Core.Commands;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class FontToggleShortcutServiceTests
{
    [Theory]
    [InlineData(FontToggleShortcut.Bold, true)]
    [InlineData(FontToggleShortcut.Italic, true)]
    [InlineData(FontToggleShortcut.Underline, true)]
    [InlineData(FontToggleShortcut.Strikethrough, true)]
    public void CreateDiff_SetsOnlyRequestedFontToggle(FontToggleShortcut shortcut, bool enabled)
    {
        var diff = FontToggleShortcutService.CreateDiff(shortcut, enabled);

        diff.Bold.Should().Be(shortcut == FontToggleShortcut.Bold ? enabled : null);
        diff.Italic.Should().Be(shortcut == FontToggleShortcut.Italic ? enabled : null);
        diff.Underline.Should().Be(shortcut == FontToggleShortcut.Underline ? enabled : null);
        diff.Strikethrough.Should().Be(shortcut == FontToggleShortcut.Strikethrough ? enabled : null);
    }
}
