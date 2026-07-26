using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class FormatCellsFillEditorTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Editor_SeedsConcreteStyleAndKeepsClearCheckboxIndependent()
    {
        var path = TempPath();
        try
        {
            await Session.Dispatch(() =>
            {
                var editor = CreateEditor(path, new CellStyle
                {
                    FillColor = new CellColor(226, 239, 218),
                    FillPatternStyle = CellFillPatternStyle.DarkGrid,
                    FillPatternColor = new CellColor(0, 112, 192),
                });

                editor.FillColor.Should().Be(new CellColor(226, 239, 218));
                editor.PatternColor.Should().Be(new CellColor(0, 112, 192));
                editor.ClearFill.Should().BeFalse();

                editor.ClearFillCheckBox.IsChecked = true;
                editor.ClearFill.Should().BeTrue();
                editor.FillColor.Should().Be(new CellColor(226, 239, 218),
                    "checking Clear Fill is an independent explicit operation");

                editor.ClearFillCheckBox.IsChecked = false;
                editor.ClearFill.Should().BeFalse();
                editor.FillColor.Should().Be(new CellColor(226, 239, 218),
                    "unchecking Clear Fill must not force a new color");
            }, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Editor_RejectsInvalidTypedFillAndPatternColorsBeforeAcceptance()
    {
        var path = TempPath();
        try
        {
            await Session.Dispatch(() =>
            {
                var editor = CreateEditor(path, new CellStyle());

                editor.FillColorTextBox.Text = "not-a-color";
                editor.TryCommitInput(out var message, out var invalidControl).Should().BeFalse();
                message.Should().Be("FormatCells_InvalidFillColorMessage");
                invalidControl.Should().BeSameAs(editor.FillColorTextBox);

                editor.FillColorTextBox.Text = "#123456";
                editor.PatternColorTextBox.Text = "1,2,3";
                editor.TryCommitInput(out _, out _).Should().BeTrue();
                editor.FillColor.Should().Be(new CellColor(0x12, 0x34, 0x56));
                editor.PatternColor.Should().Be(new CellColor(1, 2, 3));
            }, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"freex-fill-editor-{Guid.NewGuid():N}.json");

    private static FormatCellsFillEditor CreateEditor(string path, CellStyle style)
    {
        var recent = new RecentColorsStore(path);
        return new FormatCellsFillEditor(
            recent,
            (_, initial) => Task.FromResult<CellColor?>(initial),
            key => key,
            WorkbookTheme.Office,
            style);
    }
}
