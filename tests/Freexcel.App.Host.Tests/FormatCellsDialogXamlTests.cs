using System.IO;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class FormatCellsDialogXamlTests
{
    [Fact]
    public void FormatCellsDialog_ContainsSupportedExcelTabs()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));

        foreach (var tab in new[] { "Number", "Alignment", "Font", "Fill", "Border", "Protection" })
        {
            xaml.Should().Contain($"<TabItem Header=\"{tab}\"");
        }
    }

    [Fact]
    public void FormatCellsDialog_MapsAllSupportedStyleDiffFields()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        foreach (var field in new[]
        {
            "Bold", "Italic", "Underline", "Strikethrough", "FontName", "FontSize",
            "FontColor", "FillColor", "HAlign", "VAlign", "WrapText", "ShrinkToFit",
            "NumberFormat", "DoubleUnderline", "IndentLevel", "TextRotation",
            "BorderTop", "BorderRight", "BorderBottom", "BorderLeft", "Locked", "ClearFill",
        })
        {
            source.Should().Contain($"{field}:");
        }

        source.Should().Contain("s.DoubleUnderline");
        source.Should().Contain("s.IndentLevel");
        source.Should().Contain("s.TextRotation");
        source.Should().Contain("s.BorderTop");
        source.Should().Contain("s.BorderRight");
        source.Should().Contain("s.BorderBottom");
        source.Should().Contain("s.BorderLeft");
        source.Should().Contain("s.Locked");
    }

    [Fact]
    public void FormatCellsDialog_ExposesShrinkToFitAndMapsItIntoStyleDiff()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml"));
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "FormatCellsDialog.xaml.cs"));

        xaml.Should().Contain("x:Name=\"DlgShrinkToFitCheck\"");
        xaml.Should().Contain("Content=\"Shrink to fit\"");
        source.Should().Contain("DlgShrinkToFitCheck.IsChecked = s.ShrinkToFit;");
        source.Should().Contain("ShrinkToFit:");
        source.Should().Contain("DlgShrinkToFitCheck.IsChecked");
        source.Should().Contain("Enum.GetNames(typeof(CellHAlign))");
        source.Should().Contain("Enum.GetNames(typeof(CellVAlign))");
    }
}
